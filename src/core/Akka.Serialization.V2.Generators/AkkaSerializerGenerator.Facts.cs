//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Facts.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akka.Serialization.V2.Generators;

public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// The S5 whole-compilation facts stage: builds the cached, symbol-free <see cref="CompilationFacts"/>
    /// for one compilation change, shared by every serializer -- replacing the former shape, where
    /// <c>ValidateProtocolCoverage</c> re-walked every source-declared type once PER SERIALIZER,
    /// inside its own diagnostics-only <c>RegisterSourceOutput</c> callback. Registered as
    /// <c>context.CompilationProvider.Combine(collectedSerializers).Combine(collectedMessages).Select(...)</c>
    /// with tracking name <see cref="TrackingNames.CompilationFacts"/> (see <see cref="Initialize"/>).
    /// <paramref name="serializers"/> is consulted for each serializer's protocol key -- which
    /// protocol interfaces anyone actually declared a <c>[AkkaSerializer&lt;TProtocol&gt;]</c> for
    /// -- and its <see cref="SerializerInfo.ClosedGenericSchemas"/> (a registered construction's own
    /// fields can name a union base too). <paramref name="messages"/> is consulted for every
    /// discovered-mode (Decision 21) union field's own static-type key, AND (see
    /// <see cref="ComputeLocalMarkedImplementorsFromMessages"/>) as the SOURCE of every local marked
    /// implementor fact -- no symbol walk is needed for that half, since every marked local message
    /// already passed through extraction and left a cached <see cref="MessageInfo"/> behind with
    /// everything this stage needs (<see cref="MessageInfo.Protocols"/>, <see cref="MessageInfo.BaseTypeNames"/>).
    /// The only symbol walk left in this stage is <see cref="ComputeLocalImplementorFacts"/>
    /// (AKKASG029's own local-only input, unchanged since S5) and the referenced-assembly walk below,
    /// which is itself cached per assembly REFERENCE (see <see cref="ReferencedAssemblyFactsCache"/>).
    /// </summary>
    internal static CompilationFacts ComputeCompilationFacts(
        Compilation compilation,
        ImmutableArray<SerializerInfo?> serializers,
        ImmutableArray<MessageInfo?> messages,
        CancellationToken cancellationToken)
    {
        var knownTypes = GetKnownTypes(compilation);
        var protocolKeys = ComputeDistinctProtocolKeys(serializers);
        var referencedAssembliesUsingV2 = ComputeReferencedAssembliesUsingV2(compilation, cancellationToken);
        var closedSetKeys = AddReferencedUnionFieldKeys(compilation, referencedAssembliesUsingV2,
            ComputeDistinctClosedSetKeys(protocolKeys, messages, serializers), knownTypes, cancellationToken);

        var unionWalkKeys = ComputeUnionKeys(protocolKeys, messages, serializers);
        var nestedValueKeys = ComputeNestedValueKeys(messages, serializers);
        var (localUnmarkedImplementorsByProtocol, unionHierarchies) = ComputeLocalImplementorFacts(
            compilation, protocolKeys, unionWalkKeys, nestedValueKeys, knownTypes, cancellationToken);
        var localMarkedImplementorsByClosedSetKey = ComputeLocalMarkedImplementorsFromMessages(messages, closedSetKeys);
        var (referencedMarked, referencedUnmarked) = ComputeReferencedAssemblyImplementors(
            compilation, referencedAssembliesUsingV2, closedSetKeys, protocolKeys, knownTypes, cancellationToken);
        var upstreamSerializerBindings = ComputeUpstreamSerializerBindings(compilation, referencedAssembliesUsingV2, knownTypes, cancellationToken);

        return new CompilationFacts(
            localUnmarkedImplementorsByProtocol,
            localMarkedImplementorsByClosedSetKey,
            referencedMarked,
            referencedUnmarked,
            upstreamSerializerBindings,
            referencedAssembliesUsingV2,
            unionHierarchies);
    }

    /// <summary>
    /// The union keys the source-type walk must bucket unmarked implementors for (AKKASG048): every
    /// discovered union field's static-type key (a <see cref="FieldKind.Union"/> field whose
    /// <see cref="FieldInfo.UnionMembers"/> is still empty after extraction), except a protocol key,
    /// whose unmarked implementors the same walk already buckets for AKKASG029. Scans ordinary
    /// declarations and every serializer's <see cref="SerializerInfo.ClosedGenericSchemas"/>.
    /// Sorted by <see cref="TypeKey.MetadataName"/> (ordinal) for a stable facts value.
    /// </summary>
    private static ImmutableArray<TypeKey> ComputeUnionKeys(
        ImmutableArray<TypeKey> protocolKeys,
        ImmutableArray<MessageInfo?> messages,
        ImmutableArray<SerializerInfo?> serializers)
    {
        var discovered = new HashSet<TypeKey>();

        void CollectFrom(MessageInfo message)
        {
            foreach (var field in message.Fields)
            {
                // A union field whose static type is not a named type has a default key with a null
                // MetadataName; there is no hierarchy to walk for it.
                if (field.Mapping.Kind != FieldKind.Union || !field.UnionMembers.IsEmpty || string.IsNullOrEmpty(field.Mapping.Key.MetadataName))
                    continue;

                discovered.Add(field.Mapping.Key);
            }
        }

        foreach (var message in messages)
        {
            if (message != null)
                CollectFrom(message);
        }

        foreach (var serializer in serializers)
        {
            if (serializer == null)
                continue;

            foreach (var schema in serializer.ClosedGenericSchemas)
                CollectFrom(schema);
        }

        foreach (var protocolKey in protocolKeys)
            discovered.Remove(protocolKey);

        if (discovered.Count == 0)
            return ImmutableArray<TypeKey>.Empty;

        var walk = new List<TypeKey>(discovered);
        walk.Sort(static (a, b) => string.CompareOrdinal(a.MetadataName, b.MetadataName));
        return walk.ToImmutableArray();
    }

    /// <summary>
    /// The AKKASG052 keys: every non-sealed, concrete, non-generic <c>[AkkaSerializable]</c> class
    /// declared in this compilation that some field uses as a nested value (a
    /// <see cref="FieldKind.Object"/> mapping). A field like that is written as the class's own
    /// fields, so a value of a subclass would lose the subclass's fields; the walk looks for such
    /// subclasses. Scans ordinary declarations and every serializer's
    /// <see cref="SerializerInfo.ClosedGenericSchemas"/>. Sorted by <see cref="TypeKey.MetadataName"/> (ordinal).
    /// </summary>
    private static ImmutableArray<TypeKey> ComputeNestedValueKeys(
        ImmutableArray<MessageInfo?> messages,
        ImmutableArray<SerializerInfo?> serializers)
    {
        var unsealed = new HashSet<TypeKey>();
        foreach (var message in messages)
        {
            if (message is { IsSealed: false, IsValueType: false, IsAbstract: false, IsGenericDefinition: false } && message.ForeignAssemblyName.Length == 0)
                unsealed.Add(message.Key);
        }

        if (unsealed.Count == 0)
            return ImmutableArray<TypeKey>.Empty;

        var used = new HashSet<TypeKey>();

        void CollectFrom(MessageInfo message)
        {
            foreach (var field in message.Fields)
            {
                foreach (var nested in EnumerateNestedValueMappings(field.Mapping))
                {
                    if (unsealed.Contains(nested.Key))
                        used.Add(nested.Key);
                }
            }
        }

        foreach (var message in messages)
        {
            if (message != null)
                CollectFrom(message);
        }

        foreach (var serializer in serializers)
        {
            if (serializer == null)
                continue;

            foreach (var schema in serializer.ClosedGenericSchemas)
                CollectFrom(schema);
        }

        if (used.Count == 0)
            return ImmutableArray<TypeKey>.Empty;

        var keys = new List<TypeKey>(used);
        keys.Sort(static (a, b) => string.CompareOrdinal(a.MetadataName, b.MetadataName));
        return keys.ToImmutableArray();
    }

    /// <summary>
    /// Every reference-type nested value (<see cref="FieldKind.Object"/>) a field writes: the field's
    /// own mapping, and any collection element, dictionary key, or dictionary value inside it, at any
    /// depth. Each one is written as that class's own fields, so a subclass value in any of these
    /// positions would lose its own fields (AKKASG052).
    /// </summary>
    internal static IEnumerable<TypeMapping> EnumerateNestedValueMappings(TypeMapping mapping)
    {
        if (mapping.Kind == FieldKind.Object && !mapping.IsValueType)
            yield return mapping;

        foreach (var argument in mapping.TypeArguments)
        {
            foreach (var nested in EnumerateNestedValueMappings(argument))
                yield return nested;
        }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> has <paramref name="key"/> anywhere in its base-class chain
    /// or among its interfaces. Compares by metadata identity, with the same cheap name pre-filter
    /// <see cref="ImplementsProtocol"/> uses.
    /// </summary>
    private static bool DerivesFromOrImplements(INamedTypeSymbol candidate, TypeKey key)
    {
        for (var baseType = candidate.BaseType; baseType != null && baseType.SpecialType != SpecialType.System_Object; baseType = baseType.BaseType)
        {
            if (CouldMatchByMetadataName(key.MetadataName, baseType.MetadataName)
                && TypeKey.FromSymbol(baseType, includeDisplayName: false).Equals(key))
                return true;
        }

        return ImplementsProtocol(candidate, key);
    }

    private const string IsClosedTypeAttributeFullName = "System.Runtime.CompilerServices.IsClosedTypeAttribute";

    /// <summary>
    /// A C# 15 <c>closed</c> class or record: the <c>closed</c> modifier on a source declaration, or
    /// <c>IsClosedTypeAttribute</c> (matched by full metadata name, so a consumer polyfill counts) on
    /// any declaration. A source-declared <c>closed</c> type is found by its modifier: the compiler only
    /// adds <c>IsClosedTypeAttribute</c> at emit time, and Roslyn does not return compiler-made
    /// attributes from a source symbol's <c>GetAttributes()</c>. The modifier is matched by text because
    /// this generator builds against a Roslyn that predates the keyword.
    /// </summary>
    private static bool HasClosedModifier(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(modifier => string.Equals(modifier.ValueText, "closed", StringComparison.Ordinal)))
                return true;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && string.Equals(attributeClass.ToDisplayString(), IsClosedTypeAttributeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
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
    /// Every distinct closed-set key this compilation actually asks about: every protocol key
    /// (<paramref name="protocolKeys"/>) plus, for Decision 21, every discovered-mode union field's
    /// own static-type key -- a <see cref="FieldKind.Union"/> field whose <see cref="FieldInfo.UnionMembers"/>
    /// is still empty after extraction, the unambiguous "resolve the set here" signal (see
    /// <c>IsInferredUnionBase</c>) -- plus, as of S7, every
    /// <see cref="PrefixArgumentKind.DiscoveredClosedSet"/> position any serializer's own
    /// <see cref="SerializerInfo.PrefixExpansions"/> asks about: a <c>ManifestPrefix</c> registration
    /// whose type argument is the protocol interface, or any other union base, needs
    /// EXACTLY the same closed-set bucket this stage already builds for every other consumer, so its
    /// own key must be requested here too for <see cref="ComputeLocalMarkedImplementorsFromMessages"/>/
    /// <see cref="ComputeReferencedAssemblyImplementors"/> to compute (and cache) a bucket for it.
    /// Scans <paramref name="messages"/> (ordinary declarations) and every serializer's
    /// <see cref="SerializerInfo.ClosedGenericSchemas"/> (a registered construction's own substituted
    /// fields can name a union base too) for the discovered-union-field half, the same two
    /// sources <c>ComputeReferencedTypeKeys</c> in AkkaSerializerGenerator.MetadataSchemas.cs scans
    /// for foreign type references.
    /// </summary>
    private static ImmutableArray<TypeKey> ComputeDistinctClosedSetKeys(
        ImmutableArray<TypeKey> protocolKeys,
        ImmutableArray<MessageInfo?> messages,
        ImmutableArray<SerializerInfo?> serializers)
    {
        var seen = new HashSet<TypeKey>(protocolKeys);
        var builder = ImmutableArray.CreateBuilder<TypeKey>(protocolKeys.Length);
        builder.AddRange(protocolKeys);

        void CollectFrom(MessageInfo message)
        {
            foreach (var field in message.Fields)
            {
                if (field.Mapping.Kind != FieldKind.Union || !field.UnionMembers.IsEmpty)
                    continue;

                var key = field.Mapping.Key;
                if (string.IsNullOrEmpty(key.MetadataName) || !seen.Add(key))
                    continue;

                builder.Add(key);
            }
        }

        void CollectKey(TypeKey key)
        {
            if (string.IsNullOrEmpty(key.MetadataName) || !seen.Add(key))
                return;

            builder.Add(key);
        }

        foreach (var message in messages)
        {
            if (message != null)
                CollectFrom(message);
        }

        foreach (var serializer in serializers)
        {
            if (serializer == null)
                continue;

            foreach (var schema in serializer.ClosedGenericSchemas)
                CollectFrom(schema);

            foreach (var expansion in serializer.PrefixExpansions)
            {
                foreach (var position in expansion.Positions)
                {
                    if (position.Kind == PrefixArgumentKind.DiscoveredClosedSet)
                        CollectKey(position.DiscoveredKey);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The AKKASG029 input (see <see cref="ValidateProtocolCoverage"/>), and in the same pass the
    /// AKKASG048 input (<see cref="UnionHierarchyFacts"/>): walks every source-declared
    /// type in <paramref name="compilation"/> EXACTLY ONCE -- not once per protocol, and not once
    /// per serializer, as the pre-S5 shape inside <c>ValidateProtocolCoverage</c> did -- testing
    /// each non-abstract class/struct candidate against every requested protocol key at once and
    /// bucketing the UNMARKED matches. A marked candidate is skipped immediately, before any
    /// <see cref="TypeKey"/> is built for it or any protocol membership is even tested -- there is
    /// nothing for this walk to do with a marked type at all, since every marked local message
    /// already has its own cached fact (see <see cref="ComputeLocalMarkedImplementorsFromMessages"/>,
    /// which needs no symbol walk of its own). Returns one entry per <paramref name="protocolKeys"/>
    /// element, even one whose bucket ends up empty (a protocol with clean coverage), so a lookup
    /// miss in <see cref="CompilationFacts.LocalUnmarkedImplementorsByProtocol"/> unambiguously means
    /// "no serializer asked about this protocol", not "no implementors, or we never looked". Each
    /// bucket's implementor list is sorted by <see cref="TypeKey.MetadataName"/> (ordinal) for a
    /// deterministic diagnostic order across runs.
    /// </summary>
    private static (ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> UnmarkedByProtocol, UnionHierarchyFacts UnionHierarchies) ComputeLocalImplementorFacts(
        Compilation compilation,
        ImmutableArray<TypeKey> protocolKeys,
        ImmutableArray<TypeKey> unionKeys,
        ImmutableArray<TypeKey> nestedValueKeys,
        KnownTypes knownTypes,
        CancellationToken cancellationToken)
    {
        if ((protocolKeys.IsEmpty && unionKeys.IsEmpty && nestedValueKeys.IsEmpty) || knownTypes.SerializableAttribute == null)
            return (ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty, UnionHierarchyFacts.Empty);

        var buckets = new Dictionary<TypeKey, List<TypeKey>>();
        foreach (var protocolKey in protocolKeys)
            buckets[protocolKey] = new List<TypeKey>();

        var unionUnmarked = new Dictionary<TypeKey, List<TypeKey>>();
        foreach (var unionKey in unionKeys)
            unionUnmarked[unionKey] = new List<TypeKey>();

        var nestedValueSubclasses = new Dictionary<TypeKey, List<TypeKey>>();
        foreach (var nestedValueKey in nestedValueKeys)
            nestedValueSubclasses[nestedValueKey] = new List<TypeKey>();

        // ONE pass over the source-declared types serves AKKASG029 (unmarked protocol implementors),
        // AKKASG048 (unmarked implementors of the union keys ComputeUnionKeys kept), and AKKASG052
        // (any subclass, marked or not, abstract or not, of a non-sealed nested value class).
        foreach (var candidate in GetSourceDeclaredTypes(compilation))
        {
            // This whole-compilation walk re-runs on every edit (its output combines the
            // CompilationProvider by necessity); honor IDE cancellation between candidates, exactly
            // as the pre-S5 per-serializer walk did.
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.TypeKind == TypeKind.Class)
            {
                foreach (var nestedValueKey in nestedValueKeys)
                {
                    if (DerivesFromOrImplements(candidate, nestedValueKey))
                        nestedValueSubclasses[nestedValueKey].Add(TypeKey.FromSymbol(candidate));
                }
            }

            if (candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct) || candidate.IsAbstract)
                continue;

            var isMarked = candidate.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));

            // A marked candidate is already a case wherever it belongs; nothing to report for it.
            if (isMarked)
                continue;

            TypeKey? candidateKey = null;
            foreach (var protocolKey in protocolKeys)
            {
                if (ImplementsProtocol(candidate, protocolKey))
                {
                    candidateKey ??= TypeKey.FromSymbol(candidate);
                    buckets[protocolKey].Add(candidateKey.Value);
                }
            }

            // A ref struct can never be converted to an interface or base type, so it can never
            // reach a union field.
            foreach (var unionKey in candidate.IsRefLikeType ? ImmutableArray<TypeKey>.Empty : unionKeys)
            {
                if (!DerivesFromOrImplements(candidate, unionKey))
                    continue;

                candidateKey ??= TypeKey.FromSymbol(candidate);
                unionUnmarked[unionKey].Add(candidateKey.Value);
            }
        }

        var unionHierarchies = unionKeys.IsEmpty && nestedValueKeys.IsEmpty
            ? UnionHierarchyFacts.Empty
            : new UnionHierarchyFacts(SortAndBuild(unionUnmarked), SortAndBuild(nestedValueSubclasses));

        return (BuildWithEveryEntry(buckets), unionHierarchies);
    }

    /// <summary>
    /// Decision 19's local closed-set half, and Decision 21's extension of it to any other union
    /// base -- derived ENTIRELY from <paramref name="messages"/>, the already-collected, already-
    /// cached <see cref="MessageInfo"/> array every other stage reads. No symbol walk runs here at
    /// all: every local marked message already passed through <c>ExtractMessageCore</c>, which
    /// recorded its own <see cref="MessageInfo.Protocols"/> (every interface, via <c>AllInterfaces</c>)
    /// and <see cref="MessageInfo.BaseTypeNames"/> (every base class) once, at EXTRACTION time --
    /// the same per-node, per-declaration caching every other message fact already relies on. A
    /// message matches a closed-set key when its own static-type name (the key's
    /// <see cref="TypeKey.DisplayName"/>) appears in either list -- an ordinal string membership
    /// test, not a symbol comparison. This is a pure function of <paramref name="messages"/> and
    /// <paramref name="closedSetKeys"/>: an edit that touches neither reports this stage's own
    /// output as value-equal (Unchanged), and an edit to a message unrelated to any requested key
    /// changes nothing here even though the compilation as a whole recomputed. Sorted by
    /// <see cref="TypeKey.MetadataName"/> (ordinal) for a deterministic expansion/dispatch order.
    /// </summary>
    private static ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> ComputeLocalMarkedImplementorsFromMessages(
        ImmutableArray<MessageInfo?> messages,
        ImmutableArray<TypeKey> closedSetKeys)
    {
        if (closedSetKeys.IsEmpty)
            return ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty;

        var buckets = new Dictionary<TypeKey, List<TypeKey>>();
        foreach (var key in closedSetKeys)
            buckets[key] = new List<TypeKey>();

        foreach (var message in messages)
        {
            // A generic definition placeholder (Wrapper<T> itself) is never a concrete implementor;
            // an abstract type is never a concrete runtime message type either -- both match the
            // exclusions the old symbol walk applied (candidate.IsAbstract, and generic definitions
            // never reached the walk in the first place since they have no separate declaration).
            if (message == null || message.IsGenericDefinition || message.IsAbstract)
                continue;

            foreach (var key in closedSetKeys)
            {
                var keyName = key.DisplayName;
                if (string.IsNullOrEmpty(keyName))
                    continue;

                if (message.Protocols.Contains(keyName) || message.BaseTypeNames.Contains(keyName))
                    buckets[key].Add(message.Key);
            }
        }

        return SortAndBuild(buckets);
    }

    /// <summary>
    /// Sorts and builds a bucket dictionary keeping EVERY key's own entry, even an empty one -- the
    /// convention <see cref="CompilationFacts.LocalUnmarkedImplementorsByProtocol"/> needs so a
    /// lookup MISS unambiguously means "no serializer asked about this protocol", distinguishing it
    /// from a HIT with zero unmarked implementors. Contrast <see cref="SortAndBuild"/>, which omits
    /// an empty bucket -- the right convention for the OTHER three closed-set dictionaries, whose
    /// consumers need no such distinction.
    /// </summary>
    private static ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> BuildWithEveryEntry(Dictionary<TypeKey, List<TypeKey>> buckets)
    {
        var result = ImmutableDictionary.CreateBuilder<TypeKey, ImmutableArray<TypeKey>>();
        foreach (var pair in buckets)
        {
            pair.Value.Sort(static (a, b) => string.CompareOrdinal(a.MetadataName, b.MetadataName));
            result[pair.Key] = pair.Value.ToImmutableArray();
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// One non-abstract, non-generic class/struct found in a referenced assembly's public type
    /// table: its own key, whether it carries <c>[AkkaSerializable]</c>, and (Decision 19/21) its
    /// own interface and base-class membership as symbol-free strings -- <see cref="MessageInfo.Protocols"/>/
    /// <see cref="MessageInfo.BaseTypeNames"/>'s own shape, computed the same way, so a referenced
    /// type's facts and a local message's facts are filtered by the identical string-membership
    /// test. <see cref="BaseTypeNames"/> is populated only when <see cref="IsMarked"/>: an unmarked
    /// candidate is only ever tested against a protocol key (AKKASG029), which never needs it.
    /// Cached per assembly REFERENCE in <see cref="ReferencedAssemblyFactsCache"/> -- see that
    /// field's own doc comment for why this makes the expensive part of the walk a once-per-reference cost.
    /// </summary>
    private readonly struct ReferencedTypeFacts
    {
        public ReferencedTypeFacts(TypeKey key, bool isMarked, ImmutableArray<string> protocols, ImmutableArray<string> baseTypeNames, ImmutableArray<TypeKey> unionFieldKeys)
        {
            Key = key;
            IsMarked = isMarked;
            Protocols = protocols;
            BaseTypeNames = baseTypeNames;
            UnionFieldKeys = unionFieldKeys;
        }

        public TypeKey Key { get; }
        public bool IsMarked { get; }
        public ImmutableArray<string> Protocols { get; }
        public ImmutableArray<string> BaseTypeNames { get; }

        /// <summary>
        /// For a marked type: the static type of every <c>[AkkaField]</c> property that is an inferred
        /// union base (see <c>IsInferredUnionBase</c>). A message from a referenced assembly can reach
        /// the serializer (as a protocol implementor or a nested value) and bring such a field with it;
        /// its key must be a requested closed-set key, or the union's members in OTHER referenced
        /// assemblies are never collected. Empty for an unmarked type.
        /// </summary>
        public ImmutableArray<TypeKey> UnionFieldKeys { get; }
    }

    /// <summary>One <c>[AkkaSerializer&lt;TProtocol&gt;]</c> declaration found in a referenced assembly, cached alongside <see cref="ReferencedTypeFacts"/> in the same per-assembly walk.</summary>
    private readonly struct ReferencedSerializerFacts
    {
        public ReferencedSerializerFacts(TypeKey protocolKey, string serializerFullName)
        {
            ProtocolKey = protocolKey;
            SerializerFullName = serializerFullName;
        }

        public TypeKey ProtocolKey { get; }
        public string SerializerFullName { get; }
    }

    /// <summary>
    /// One referenced assembly's full, symbol-free walk result -- every candidate
    /// <see cref="ReferencedTypeFacts"/> and every <see cref="ReferencedSerializerFacts"/> declared
    /// there. See <see cref="ReferencedAssemblyFactsCache"/> for why this is computed once per
    /// assembly, not once per request.
    /// </summary>
    private sealed class ReferencedAssemblyFacts
    {
        public ReferencedAssemblyFacts(ImmutableArray<ReferencedTypeFacts> types, ImmutableArray<ReferencedSerializerFacts> serializers)
        {
            Types = types;
            Serializers = serializers;
        }

        public ImmutableArray<ReferencedTypeFacts> Types { get; }
        public ImmutableArray<ReferencedSerializerFacts> Serializers { get; }
    }

    /// <summary>
    /// One process-lifetime cache entry per referenced assembly's own <see cref="MetadataReference"/>:
    /// the expensive part of Decision 19/21's referenced-assembly walk -- enumerating every type in
    /// an assembly's public type table, reading its attributes, interfaces, and base classes -- runs
    /// at most ONCE per reference identity, not once per compilation edit.
    /// </summary>
    /// <remarks>
    /// This is keyed on <see cref="MetadataReference"/>, NOT on <see cref="IAssemblySymbol"/> (an
    /// earlier version of this cache was, and was wrong): Roslyn reuses the same
    /// <see cref="IAssemblySymbol"/> instance for an unchanged reference only while an earlier bound
    /// symbol for it is still reachable through Roslyn's own internal (weak) symbol cache -- under
    /// memory pressure, or whenever a compilation that held the earlier symbol alive is collected,
    /// that reuse can simply not happen, and the next compilation gets a FRESH assembly symbol for
    /// the exact same, unchanged reference. Keyed on <see cref="IAssemblySymbol"/>, this cache would
    /// then silently re-walk -- rare in a quick unit test, but routine in a long IDE session, where
    /// compilations are created and dropped constantly. The workspace and the generator driver both
    /// keep the SAME <see cref="MetadataReference"/> instance across an edit for as long as the
    /// reference set itself is unchanged (that instance is exactly what "the reference set is
    /// unchanged" means), and the walk's result depends almost only on the PE file's own metadata,
    /// not on which symbol instance happens to represent it right now -- so keying on the reference
    /// is both safe and the thing that is actually stable. One exception, accepted knowingly:
    /// <see cref="ReferencedTypeFacts.UnionFieldKeys"/> also depends on what a field's type IS (an
    /// interface or not, collection-like or not), and that type can live in a different reference.
    /// If that other assembly changes shape while this reference stays the same instance, the cached
    /// keys go stale until this reference changes too. The worst outcome is a missing union key, which
    /// surfaces as an AKKASG019 that a full rebuild clears, never as wrong generated code. <see cref="Compilation.GetMetadataReference(IAssemblySymbol)"/>
    /// recovers it from an assembly symbol at lookup time; it returns null for an assembly with no
    /// corresponding reference in this compilation (a source assembly reached via
    /// <c>CompilationReference</c>, or the compilation's own assembly), so the assembly symbol itself
    /// is used as a fallback key in that case -- correct there too, since a compilation-referenced,
    /// in-source assembly has no separate PE metadata to reuse across edits in the first place; its
    /// own symbol identity IS the closest thing to a stable key available. A
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> entry is collected automatically once its key
    /// (reference or, in the fallback case, symbol) is, with no manual invalidation -- the same
    /// lifetime discipline <see cref="KnownTypesCache"/> already uses for a
    /// <see cref="Compilation"/>-keyed cache. <see cref="ReferencedAssemblyWalkCount"/> is test-only
    /// instrumentation proving this: a second request against the SAME reference must not increment
    /// it, and a request against a genuinely DIFFERENT reference (even one compiled from identical
    /// source) must (see the caching-proof spec in <c>GeneratorCompilationFactsSpec.cs</c>).
    /// </remarks>
    private static readonly ConditionalWeakTable<object, ReferencedAssemblyFacts> ReferencedAssemblyFactsCache = new();

    /// <summary>
    /// Test-only instrumentation: incremented once per actual (uncached) per-assembly walk in
    /// <see cref="ComputeReferencedAssemblyFacts"/>. Never read by production code. Not, by itself,
    /// a reliable signal for a test to assert on in THIS test project: xunit's own test-collection
    /// scheduler here dispatches work across a pool of worker threads, and different test methods'
    /// windows can genuinely overlap in wall-clock time even when nominally "not parallelized" (see
    /// <see cref="ReferencedAssemblyWalksByAssemblyName"/>'s own remarks) -- so a global counter's
    /// delta can be inflated by a DIFFERENT test's walk of its OWN, differently-named assembly
    /// landing inside this one's measurement window. Kept for any caller that only needs a coarse,
    /// total walk count; a caching-proof test should read <see cref="ReferencedAssemblyWalksByAssemblyName"/>
    /// instead, filtered to its own uniquely-named assembly.
    /// </summary>
    internal static int ReferencedAssemblyWalkCount;

    /// <summary>
    /// Test-only instrumentation, keyed by the walked assembly's own <see cref="IAssemblySymbol.Name"/>:
    /// how many times <see cref="ComputeReferencedAssemblyFacts"/> actually ran (a cache MISS) for an
    /// assembly with that name, across the whole process. A <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// because this test project's own tests do not, in practice, serialize onto a single thread (see
    /// this dictionary's own reader's caveat below) -- concurrent increments for the SAME name (two
    /// different <see cref="MetadataReference"/> instances built from source with the same assembly
    /// name, in two different tests) are possible and correctly summed; a caching-proof test avoids
    /// that collision entirely by giving its own test-only assemblies names no other test in this
    /// project uses (for example "FactsCaching.AssemblyA"). A caching-proof test reads this dictionary
    /// before and after a run, filtered to its own unique assembly name(s), and asserts on the DELTA
    /// for that name specifically -- immune to any OTHER, concurrently-running test's own walk of an
    /// assembly with a different name, which a single global counter is not.
    /// </summary>
    internal static readonly ConcurrentDictionary<string, int> ReferencedAssemblyWalksByAssemblyName = new(StringComparer.Ordinal);

    private static ReferencedAssemblyFacts GetReferencedAssemblyFacts(Compilation compilation, IAssemblySymbol assembly, KnownTypes knownTypes, CancellationToken cancellationToken)
    {
        // Prefer the assembly's own MetadataReference as the cache key (see this cache's own doc
        // remarks for why); fall back to the assembly symbol itself only when this compilation has
        // no separate reference for it (a source/compilation-referenced assembly).
        object cacheKey = (object?)compilation.GetMetadataReference(assembly) ?? assembly;
        return ReferencedAssemblyFactsCache.GetValue(cacheKey, _ => ComputeReferencedAssemblyFacts(assembly, knownTypes, cancellationToken));
    }

    /// <summary>
    /// The actual, expensive per-assembly walk: every non-abstract, non-generic class/struct (marked
    /// or not, since AKKASG029's referenced-assembly widening needs the unmarked ones too) plus every
    /// <c>[AkkaSerializer&lt;TProtocol&gt;]</c> declaration. Called at most once per assembly reference
    /// -- see <see cref="ReferencedAssemblyFactsCache"/>. An unmarked candidate with no interfaces at
    /// all is skipped: it could never match a protocol key (its own <see cref="MessageInfo.Protocols"/>
    /// analog would always be empty), so keeping it out of the cached array keeps that array's size
    /// proportional to what could ever match, not to the assembly's total type count.
    /// </summary>
    private static ReferencedAssemblyFacts ComputeReferencedAssemblyFacts(IAssemblySymbol assembly, KnownTypes knownTypes, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ReferencedAssemblyWalkCount);
        ReferencedAssemblyWalksByAssemblyName.AddOrUpdate(assembly.Name, static _ => 1, static (_, count) => count + 1);

        var types = ImmutableArray.CreateBuilder<ReferencedTypeFacts>();
        var serializers = ImmutableArray.CreateBuilder<ReferencedSerializerFacts>();

        foreach (var candidate in GetSourceDeclaredTypes(assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.TypeKind == TypeKind.Class && knownTypes.SerializerAttribute != null)
            {
                foreach (var attribute in candidate.GetAttributes())
                {
                    if (attribute.AttributeClass is not { IsGenericType: true } attributeClass ||
                        !SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, knownTypes.SerializerAttribute))
                        continue;

                    if (attributeClass.TypeArguments[0] is INamedTypeSymbol protocolType)
                        serializers.Add(new ReferencedSerializerFacts(TypeKey.FromSymbol(protocolType), GetFullyQualifiedTypeName(candidate)));
                }
            }

            if (candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct) || candidate.IsAbstract || candidate.IsGenericType)
                continue;

            var isMarked = knownTypes.SerializableAttribute != null && HasSerializableAttribute(candidate, knownTypes);
            var protocols = GetProtocolNames(candidate);
            if (!isMarked && protocols.IsEmpty)
                continue;

            var baseTypeNames = isMarked ? GetBaseTypeNames(candidate) : ImmutableArray<string>.Empty;
            var unionFieldKeys = isMarked ? GetUnionFieldKeys(candidate, knownTypes) : ImmutableArray<TypeKey>.Empty;
            types.Add(new ReferencedTypeFacts(TypeKey.FromSymbol(candidate), isMarked, protocols, baseTypeNames, unionFieldKeys));
        }

        return new ReferencedAssemblyFacts(types.ToImmutable(), serializers.ToImmutable());
    }

    /// <summary>
    /// The static types of <paramref name="type"/>'s own <c>[AkkaField]</c> properties that are inferred
    /// union bases. See <see cref="ReferencedTypeFacts.UnionFieldKeys"/>.
    /// </summary>
    private static ImmutableArray<TypeKey> GetUnionFieldKeys(INamedTypeSymbol type, KnownTypes knownTypes)
    {
        if (knownTypes.FieldAttribute == null)
            return ImmutableArray<TypeKey>.Empty;

        ImmutableArray<TypeKey>.Builder? keys = null;
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.Type is not INamedTypeSymbol fieldType
                || !property.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute))
                || !IsInferredUnionBase(fieldType, MapType(fieldType, knownTypes), knownTypes))
                continue;

            keys ??= ImmutableArray.CreateBuilder<TypeKey>();
            keys.Add(TypeKey.FromSymbol(fieldType));
        }

        return keys?.ToImmutable() ?? ImmutableArray<TypeKey>.Empty;
    }

    /// <summary>
    /// Adds to <paramref name="closedSetKeys"/> every union field key a marked type in a referenced
    /// V2 assembly declares (see <see cref="ReferencedTypeFacts.UnionFieldKeys"/>). Without this, a
    /// message from assembly A with an <c>IDestination</c> field, serialized in C, would resolve to an
    /// empty union whenever <c>IDestination</c>'s implementors live in a third assembly B: the key
    /// would never be requested, so B's implementors would never be collected. Reads only the cached
    /// per-assembly facts. Order is stable: this compilation's own keys first, then referenced ones in
    /// reference order.
    /// </summary>
    private static ImmutableArray<TypeKey> AddReferencedUnionFieldKeys(
        Compilation compilation,
        ImmutableArray<string> referencedAssembliesUsingV2,
        ImmutableArray<TypeKey> closedSetKeys,
        KnownTypes knownTypes,
        CancellationToken cancellationToken)
    {
        if (referencedAssembliesUsingV2.IsEmpty)
            return closedSetKeys;

        var qualifyingAssemblies = new HashSet<string>(referencedAssembliesUsingV2, StringComparer.Ordinal);
        var seen = new HashSet<TypeKey>(closedSetKeys);
        ImmutableArray<TypeKey>.Builder? builder = null;

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!qualifyingAssemblies.Contains(assembly.Name))
                continue;

            foreach (var type in GetReferencedAssemblyFacts(compilation, assembly, knownTypes, cancellationToken).Types)
            {
                foreach (var key in type.UnionFieldKeys)
                {
                    if (string.IsNullOrEmpty(key.MetadataName) || !seen.Add(key))
                        continue;

                    builder ??= closedSetKeys.ToBuilder();
                    builder.Add(key);
                }
            }
        }

        return builder?.ToImmutable() ?? closedSetKeys;
    }

    /// <summary>
    /// Decision 19's referenced-assembly half of the closed-set walk (and Decision 21's extension to
    /// any other union base): for each of <paramref name="referencedAssembliesUsingV2"/>, reads that
    /// assembly's cached <see cref="ReferencedAssemblyFacts"/> (walked at most once per assembly
    /// symbol -- see <see cref="ReferencedAssemblyFactsCache"/>) and filters it, by ordinal string
    /// membership, against every requested key. <paramref name="protocolKeysOnly"/> ALSO buckets an
    /// unmarked implementor of a PROTOCOL key (not a union-base key) into the second returned
    /// dictionary -- AKKASG029's Decision 19 widening; a union-base implementor missing the
    /// attribute is simply invisible to this walk, matching the same rule Decision 19/21's addenda in
    /// design.md record. No attribute is needed to opt an assembly in: any assembly that references
    /// <c>Akka.Serialization.V2</c> qualifies.
    /// </summary>
    private static (ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> Marked, ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> Unmarked) ComputeReferencedAssemblyImplementors(
        Compilation compilation,
        ImmutableArray<string> referencedAssembliesUsingV2,
        ImmutableArray<TypeKey> closedSetKeys,
        ImmutableArray<TypeKey> protocolKeysOnly,
        KnownTypes knownTypes,
        CancellationToken cancellationToken)
    {
        if (closedSetKeys.IsEmpty || referencedAssembliesUsingV2.IsEmpty)
            return (ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty, ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>>.Empty);

        var qualifyingAssemblies = new HashSet<string>(referencedAssembliesUsingV2, StringComparer.Ordinal);

        var markedBuckets = new Dictionary<TypeKey, List<TypeKey>>();
        var unmarkedBuckets = new Dictionary<TypeKey, List<TypeKey>>();
        foreach (var key in closedSetKeys)
            markedBuckets[key] = new List<TypeKey>();
        foreach (var key in protocolKeysOnly)
            unmarkedBuckets[key] = new List<TypeKey>();

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!qualifyingAssemblies.Contains(assembly.Name))
                continue;

            var facts = GetReferencedAssemblyFacts(compilation, assembly, knownTypes, cancellationToken);

            foreach (var type in facts.Types)
            {
                if (type.IsMarked)
                {
                    foreach (var key in closedSetKeys)
                    {
                        var keyName = key.DisplayName;
                        if (!string.IsNullOrEmpty(keyName) && (type.Protocols.Contains(keyName) || type.BaseTypeNames.Contains(keyName)))
                            markedBuckets[key].Add(type.Key);
                    }
                }
                else
                {
                    foreach (var key in protocolKeysOnly)
                    {
                        var keyName = key.DisplayName;
                        if (!string.IsNullOrEmpty(keyName) && type.Protocols.Contains(keyName))
                            unmarkedBuckets[key].Add(type.Key);
                    }
                }
            }
        }

        return (SortAndBuild(markedBuckets), SortAndBuild(unmarkedBuckets));
    }

    /// <summary>
    /// Decision 19's placement-diagnostics input: for each referenced assembly that itself
    /// references <c>Akka.Serialization.V2</c>, every public <c>[AkkaSerializer&lt;TProtocol&gt;]</c>
    /// class declared there (read from that assembly's cached <see cref="ReferencedAssemblyFacts"/>,
    /// not re-walked here), keyed by its protocol.
    /// </summary>
    private static ImmutableDictionary<TypeKey, ImmutableArray<UpstreamSerializerBinding>> ComputeUpstreamSerializerBindings(
        Compilation compilation,
        ImmutableArray<string> referencedAssembliesUsingV2,
        KnownTypes knownTypes,
        CancellationToken cancellationToken)
    {
        if (referencedAssembliesUsingV2.IsEmpty || knownTypes.SerializerAttribute == null)
            return ImmutableDictionary<TypeKey, ImmutableArray<UpstreamSerializerBinding>>.Empty;

        var qualifyingAssemblies = new HashSet<string>(referencedAssembliesUsingV2, StringComparer.Ordinal);
        var buckets = new Dictionary<TypeKey, List<UpstreamSerializerBinding>>();

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!qualifyingAssemblies.Contains(assembly.Name))
                continue;

            var facts = GetReferencedAssemblyFacts(compilation, assembly, knownTypes, cancellationToken);

            foreach (var serializer in facts.Serializers)
            {
                if (!buckets.TryGetValue(serializer.ProtocolKey, out var bindings))
                {
                    bindings = new List<UpstreamSerializerBinding>();
                    buckets[serializer.ProtocolKey] = bindings;
                }

                bindings.Add(new UpstreamSerializerBinding(assembly.Name, serializer.SerializerFullName));
            }
        }

        var result = ImmutableDictionary.CreateBuilder<TypeKey, ImmutableArray<UpstreamSerializerBinding>>();
        foreach (var pair in buckets)
        {
            pair.Value.Sort((a, b) => string.CompareOrdinal(a.SerializerFullName, b.SerializerFullName));
            result[pair.Key] = pair.Value.ToImmutableArray();
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Sorts and builds a closed-set-implementor dictionary, OMITTING any key whose bucket ended up
    /// empty -- unlike <see cref="ComputeLocalImplementorFacts"/>'s own "one entry per
    /// requested key, even an empty one" convention (that convention exists there so a lookup MISS
    /// unambiguously means "no serializer asked", distinguishing it from a HIT with zero unmarked
    /// implementors; nothing downstream of the walks this method serves needs that distinction, and
    /// omitting empty entries keeps a compilation with no referenced-assembly implementors at all --
    /// the overwhelming common case -- reporting a genuinely empty dictionary, not one padded with
    /// as many empty arrays as there are requested keys).
    /// </summary>
    private static ImmutableDictionary<TypeKey, ImmutableArray<TypeKey>> SortAndBuild(Dictionary<TypeKey, List<TypeKey>> buckets)
    {
        var result = ImmutableDictionary.CreateBuilder<TypeKey, ImmutableArray<TypeKey>>();
        foreach (var pair in buckets)
        {
            if (pair.Value.Count == 0)
                continue;

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
    /// this narrows the candidate set the Decision 19/21 walks need to visit down to only the
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
}
