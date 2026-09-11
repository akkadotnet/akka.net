//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Facts.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Akka.Serialization.V2.Generators;

public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// The S5 whole-compilation facts stage: builds the cached, symbol-free <see cref="CompilationFacts"/>
    /// for one compilation change, shared by every serializer -- replacing the former shape, where
    /// <c>ValidateProtocolCoverage</c> re-walked every source-declared type once PER SERIALIZER,
    /// inside its own diagnostics-only <c>RegisterSourceOutput</c> callback. Registered as
    /// <c>context.CompilationProvider.Combine(collectedSerializers).Select(...)</c> with tracking
    /// name <see cref="TrackingNames.CompilationFacts"/> (see <see cref="Initialize"/>).
    /// <paramref name="serializers"/> is consulted ONLY for each serializer's protocol key -- which
    /// protocol interfaces anyone actually declared a <c>[AkkaSerializer&lt;TProtocol&gt;]</c> for --
    /// so the local-implementor walk below considers only the protocols that matter, not every
    /// interface of every source-declared type.
    /// </summary>
    internal static CompilationFacts ComputeCompilationFacts(
        Compilation compilation,
        ImmutableArray<SerializerInfo?> serializers,
        CancellationToken cancellationToken)
    {
        var protocolKeys = ComputeDistinctProtocolKeys(serializers);
        var referencedAssembliesUsingV2 = ComputeReferencedAssembliesUsingV2(compilation, cancellationToken);
        var localUnmarkedImplementorsByProtocol = ComputeLocalUnmarkedImplementorsByProtocol(compilation, protocolKeys, cancellationToken);
        var referencedAssemblyImplementorsByProtocol = EnumerateReferencedAssemblyImplementors(
            compilation, referencedAssembliesUsingV2, protocolKeys, cancellationToken);

        return new CompilationFacts(localUnmarkedImplementorsByProtocol, referencedAssemblyImplementorsByProtocol, referencedAssembliesUsingV2);
    }

    /// <summary>
    /// Every distinct protocol key at least one collected serializer declares, in first-seen order.
    /// A serializer whose <c>[AkkaSerializer&lt;TProtocol&gt;]</c> type argument was not a named
    /// type (<see cref="SerializerInfo.ProtocolTypeFullName"/> empty) contributes nothing -- there
    /// is no protocol interface for the facts below to scan for.
    /// </summary>
    private static ImmutableArray<TypeKey> ComputeDistinctProtocolKeys(ImmutableArray<SerializerInfo?> serializers)
    {
        var seen = new HashSet<TypeKey>();
        var builder = ImmutableArray.CreateBuilder<TypeKey>();
        foreach (var serializer in serializers)
        {
            if (serializer == null || serializer.ProtocolTypeFullName.Length == 0)
                continue;

            if (seen.Add(serializer.ProtocolTypeKey))
                builder.Add(serializer.ProtocolTypeKey);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The AKKASG029 input (see <see cref="ValidateProtocolCoverage"/>): walks every source-declared
    /// type in <paramref name="compilation"/> EXACTLY ONCE -- not once per protocol, and not once
    /// per serializer, as the pre-S5 shape inside <c>ValidateProtocolCoverage</c> did -- testing
    /// each non-abstract class/struct candidate against every requested protocol key at once and
    /// bucketing the unmarked matches. Returns one entry per <paramref name="protocolKeys"/> element,
    /// even one whose bucket ends up empty (a protocol with clean coverage), so a lookup miss in
    /// <see cref="CompilationFacts.LocalUnmarkedImplementorsByProtocol"/> unambiguously means "no
    /// serializer asked about this protocol", not "no implementors, or we never looked". Each
    /// bucket's implementor list is sorted by <see cref="TypeKey.MetadataName"/> (ordinal) for a
    /// deterministic diagnostic order across runs.
    /// </summary>
    private static ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> ComputeLocalUnmarkedImplementorsByProtocol(
        Compilation compilation,
        ImmutableArray<TypeKey> protocolKeys,
        CancellationToken cancellationToken)
    {
        if (protocolKeys.IsEmpty)
            return ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty;

        var knownTypes = GetKnownTypes(compilation);
        if (knownTypes.SerializableAttribute == null)
            return ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty;

        var buckets = new Dictionary<TypeKey, List<TypeKey>>();
        foreach (var protocolKey in protocolKeys)
            buckets[protocolKey] = new List<TypeKey>();

        foreach (var candidate in GetSourceDeclaredTypes(compilation))
        {
            // This whole-compilation walk re-runs on every edit (its output combines the
            // CompilationProvider by necessity); honor IDE cancellation between candidates, exactly
            // as the pre-S5 per-serializer walk did.
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct) || candidate.IsAbstract)
                continue;

            var isMarked = candidate.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
            if (isMarked)
                continue;

            foreach (var protocolKey in protocolKeys)
            {
                if (ImplementsProtocol(candidate, protocolKey))
                    buckets[protocolKey].Add(TypeKey.FromSymbol(candidate));
            }
        }

        var result = ImmutableDictionary.CreateBuilder<TypeKey, ImmutableArray<TypeKey>>();
        foreach (var pair in buckets)
        {
            pair.Value.Sort(static (a, b) => string.CompareOrdinal(a.MetadataName, b.MetadataName));
            result[pair.Key] = pair.Value.ToImmutableArray();
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// The sorted (ordinal) names of every assembly <paramref name="compilation"/> references that
    /// itself references Akka.Serialization.V2 -- Decision 19's pre-filter (see
    /// <see cref="CompilationFacts.ReferencedAssembliesUsingV2"/>). An assembly that does not
    /// reference V2 cannot declare an <c>[AkkaSerializable]</c> type (the attribute lives in V2), so
    /// this narrows the candidate set the eventual Decision 19 walk needs to visit down to only the
    /// assemblies that could possibly matter. Reads each referenced assembly's OWN metadata
    /// AssemblyRef table (<see cref="IModuleSymbol.ReferencedAssemblies"/>), never its member types,
    /// so this stays cheap even for a large reference closure.
    /// </summary>
    private static ImmutableArray<string> ComputeReferencedAssembliesUsingV2(Compilation compilation, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReferencesAkkaSerializationV2(assembly))
                names.Add(assembly.Name);
        }

        names.Sort(StringComparer.Ordinal);
        return names.ToImmutableArray();
    }

    private const string AkkaSerializationV2AssemblyName = "Akka.Serialization.V2";

    private static bool ReferencesAkkaSerializationV2(IAssemblySymbol assembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var identity in module.ReferencedAssemblies)
            {
                if (string.Equals(identity.Name, AkkaSerializationV2AssemblyName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decision 19's referenced-assembly implementor walk (design.md's "Protocol Implementors From
    /// Referenced Assemblies" section), plus its Decision 21 extension to marked union bases: for
    /// every protocol key, the <c>[AkkaSerializable]</c>-marked implementors of that protocol
    /// declared in one of <paramref name="referencedAssembliesUsingV2"/>.
    /// A SKELETON today: it always returns an empty implementor list for every protocol key.
    /// Actually walking a qualifying referenced assembly's public type members from metadata --
    /// matching its own <c>[AkkaSerializable]</c> usages, extracting a schema the way
    /// <c>ExtractMessageCore</c> does for a local type -- is Decision 19's own follow-up
    /// implementation work, not part of this change. What IS real here: the filter above
    /// (<paramref name="referencedAssembliesUsingV2"/>) already narrows the candidate assembly set
    /// down to only the ones such a walk could ever need to visit, <paramref name="protocolKeys"/>
    /// already narrows which protocols it would need to test against, and this method already takes
    /// and honors a <paramref name="cancellationToken"/> -- so wiring in the real walk later needs
    /// no signature change here or at either call site.
    /// </summary>
    private static ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> EnumerateReferencedAssemblyImplementors(
        Compilation compilation,
        ImmutableArray<string> referencedAssembliesUsingV2,
        ImmutableArray<TypeKey> protocolKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty;
    }
}
