//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Expansion.cs" company="Akka.NET Project">
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

// S7 (a follow-up to Decisions 18/19/21 in design.md): ManifestPrefix expansion moves out of the
// per-node serializer extraction transform and into a per-compilation stage, right beside
// MetadataSchemas.cs and Facts.cs. F2 built the closed-set walk (ComputeLocalMarkedProtocolImplementors/
// EnumerateReferencedAssemblyMarkedImplementors, AkkaSerializerGenerator.Extraction.cs) directly into
// ExpandClosedGenericRegistration, so a project with ManifestPrefix registrations paid a fresh
// whole-compilation walk per registration on every keystroke. CompilationFacts already computes and
// caches the identical closed-set buckets for top-level dispatch (Decisions 19/21) -- this stage
// consumes THAT cached result instead of re-walking.
//
// WHAT MOVED, WHAT DID NOT: the serializer extraction transform (AkkaSerializerGenerator.Extraction.cs,
// BuildPrefixExpansionSpec/TryClassifyPrefixArgumentPosition) still does everything that needs only
// the registration's own symbols -- validity, AllowEmpty, and classifying each type-argument position
// into Fixed/ExplicitClosedSet/DiscoveredClosedSet (see PrefixArgumentKind). It also still resolves
// every immediate AKKASG040 failure (an invalid target, an unresolvable fixed argument, no
// closed-set-flavored position at all) exactly as before, since none of those depend on a
// whole-compilation walk. What moved here is ONLY the part that genuinely needs one: resolving a
// DiscoveredClosedSet position's own member set, constructing each combination's closed generic
// symbol, and extracting its schema.
//
// THIS STAGE'S OWN CACHING: registered as context.CompilationProvider.Combine(serializers).Combine(
// compilationFacts).Select(ComputeClosedGenericExpansions) in Initialize -- the same shape as
// CompilationFacts and MetadataSchemas. It reruns on every edit (it combines the live Compilation
// directly) but its TABLE half compares equal whenever nothing it cares about changed, letting a
// downstream consumer (MergeSerializerClosedGenericExpansions, and everything after it) report
// Unchanged/Cached for an edit this stage does not care about -- see TrackingNames.ClosedGenericExpansions.
public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// The raw per-compilation output of <see cref="ComputeClosedGenericExpansions"/>: the
    /// value-equatable, symbol-free <see cref="Table"/> (feeds every downstream consumer through
    /// <see cref="MergeSerializerClosedGenericExpansions"/>) plus this run's own
    /// <see cref="LocationBag"/> (an expanded member's own <c>[AkkaField]</c> property locations,
    /// resolved from the SAME substituted symbols <see cref="ExtractMessageCore"/> already inlines
    /// this capture for -- see that method's own doc comment). Kept separate exactly like
    /// <see cref="ExtractedSerializer"/>/<see cref="ExtractedMessage"/> split their own schema from
    /// their own location bag: <see cref="Locations"/> is whitespace-sensitive (a text edit anywhere
    /// earlier in the file shifts the generic definition's own property declarations), so only
    /// <see cref="Table"/> -- projected out separately in <see cref="Initialize"/> -- feeds the
    /// pipeline's cached stages.
    /// </summary>
    internal readonly record struct ExpandedPrefixRegistrations(PrefixExpansionTable Table, LocationBag Locations);

    /// <summary>
    /// Builds every serializer's own expanded <c>ManifestPrefix</c> registrations and schemas, once
    /// per compilation change, from the light <see cref="SerializerInfo.PrefixExpansions"/> specs the
    /// extraction transform already classified (see AkkaSerializerGenerator.Extraction.cs's
    /// <c>BuildPrefixExpansionSpec</c>) and <paramref name="facts"/>' own cached closed-set buckets
    /// (<see cref="CompilationFacts.LocalMarkedImplementorsByClosedSetKey"/>/
    /// <see cref="CompilationFacts.ReferencedAssemblyImplementorsByProtocol"/>) -- no walk of the
    /// compilation's declared types happens in this method or anything it calls; that walk already
    /// ran once, in <see cref="ComputeCompilationFacts"/>, shared by every requester. Returns an empty
    /// table when no serializer has a <see cref="SerializerInfo.PrefixExpansions"/> entry at all --
    /// the overwhelming common case, and the one every edit re-checks cheaply (a single pass over the
    /// already-collected serializers array, no symbol resolution at all).
    /// </summary>
    internal static ExpandedPrefixRegistrations ComputeClosedGenericExpansions(
        Compilation compilation,
        ImmutableArray<SerializerInfo?> serializers,
        CompilationFacts facts,
        CancellationToken cancellationToken)
    {
        var hasAnyExpansion = false;
        foreach (var serializer in serializers)
        {
            if (serializer != null && !serializer.PrefixExpansions.IsDefaultOrEmpty)
            {
                hasAnyExpansion = true;
                break;
            }
        }

        if (!hasAnyExpansion)
            return new ExpandedPrefixRegistrations(PrefixExpansionTable.Empty, LocationBag.Empty);

        var knownTypes = GetKnownTypes(compilation);
        var entriesBySerializer = ImmutableDictionary.CreateBuilder<TypeKey, PrefixExpansionEntry>();
        var locationEntries = ImmutableArray.CreateBuilder<LocationEntry>();

        foreach (var serializer in serializers)
        {
            if (serializer == null || serializer.PrefixExpansions.IsDefaultOrEmpty)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            // Decision 18's override rule ("an explicit registration for this exact construction
            // wins") needs the SAME raw, validity-independent target/manifest map the extraction
            // transform already built once from this serializer's own attributes -- rebuilt here from
            // SerializerInfo.ExplicitManifestsByTarget rather than re-read from the serializer's
            // symbol, which this per-compilation stage never touches.
            var explicitManifestByTarget = new Dictionary<TypeKey, string>();
            foreach (var explicitManifest in serializer.ExplicitManifestsByTarget)
                explicitManifestByTarget[explicitManifest.Target] = explicitManifest.Manifest;

            var groupsBuilder = ImmutableArray.CreateBuilder<PrefixExpansionGroup>();
            foreach (var spec in serializer.PrefixExpansions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = ExpandOne(compilation, knownTypes, facts, spec, explicitManifestByTarget, locationEntries, cancellationToken);
                if (group != null)
                    groupsBuilder.Add(group);
            }

            if (groupsBuilder.Count > 0)
                entriesBySerializer[serializer.Key] = new PrefixExpansionEntry(groupsBuilder.ToImmutable());
        }

        var table = entriesBySerializer.Count == 0 ? PrefixExpansionTable.Empty : new PrefixExpansionTable(entriesBySerializer.ToImmutable());
        return new ExpandedPrefixRegistrations(table, new LocationBag(locationEntries.ToImmutable()));
    }

    /// <summary>
    /// Expands one <see cref="PrefixExpansionSpec"/>: re-resolves <see cref="PrefixExpansionSpec.TargetDefinitionKey"/>
    /// and every position's own candidate keys back to symbols via <see cref="ResolveTypeSymbol"/> --
    /// the same technique <see cref="ComputeMetadataSchemas"/> already uses for a referenced-assembly
    /// type key -- merges in a <see cref="PrefixArgumentKind.DiscoveredClosedSet"/> position's own
    /// member set from <paramref name="facts"/> (see <see cref="GetClosedSetImplementors"/>, each
    /// paired with its own top-level manifest read fresh from its symbol), then runs the SAME
    /// cartesian product, manifest formula, and <see cref="ExtractMessageCore"/> extraction F2 ran
    /// inline. A combination whose constructed target already has an EXPLICIT registration on this
    /// serializer (<paramref name="explicitManifestByTarget"/>) is skipped, exactly as Decision 18
    /// specifies. Returns null when the spec's own definition no longer resolves, or every combination
    /// was skipped/empty -- see <see cref="PrefixExpansionEntry"/>'s own doc comment for why a spec
    /// with nothing to contribute has no group at all, rather than an empty one.
    /// </summary>
    private static PrefixExpansionGroup? ExpandOne(
        Compilation compilation,
        KnownTypes knownTypes,
        CompilationFacts facts,
        PrefixExpansionSpec spec,
        Dictionary<TypeKey, string> explicitManifestByTarget,
        ImmutableArray<LocationEntry>.Builder locationEntries,
        CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName(spec.TargetDefinitionKey.MetadataName) is not { } definitionSymbol)
        {
            // The definition this spec was built from no longer resolves (a stale key across an
            // edit this stage's own value equality would already have caught downstream) -- nothing
            // to expand; defensive only, not expected to happen in practice.
            return null;
        }

        var resolvedPositions = new List<List<(INamedTypeSymbol Symbol, string Manifest)>>(spec.Positions.Length);
        foreach (var position in spec.Positions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = new List<(INamedTypeSymbol, string)>();
            foreach (var candidate in position.Candidates)
            {
                if (ResolveTypeSymbol(compilation, candidate.Argument) is { } candidateSymbol)
                    candidates.Add((candidateSymbol, candidate.Manifest));
            }

            if (position.Kind == PrefixArgumentKind.DiscoveredClosedSet)
            {
                foreach (var implementorKey in GetClosedSetImplementors(facts, position.DiscoveredKey))
                {
                    if (ResolveTypeSymbol(compilation, implementorKey) is not { } implementorSymbol)
                        continue;

                    candidates.Add((implementorSymbol, GetOwnManifest(implementorSymbol, knownTypes) ?? string.Empty));
                }
            }

            resolvedPositions.Add(candidates);
        }

        var registrationsBuilder = ImmutableArray.CreateBuilder<ClosedGenericRegistrationInfo>();
        var schemasBuilder = ImmutableArray.CreateBuilder<MessageInfo>();

        var combinations = CartesianProduct(resolvedPositions);
        foreach (var combination in combinations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var constructedSymbol = definitionSymbol.Construct(combination.Select(c => (ITypeSymbol)c.Symbol).ToArray());
            var constructedKey = TypeKey.FromSymbol(constructedSymbol);

            // An explicit registration for this exact construction wins -- it keeps its own manifest
            // and already has its own registration/schema entry on SerializerInfo.
            if (explicitManifestByTarget.ContainsKey(constructedKey))
                continue;

            var derivedManifest = spec.ManifestPrefix + "/" + string.Join("/", combination.Select(c => c.Manifest));
            var constructedMessage = ExtractMessageCore(
                constructedSymbol,
                constructedKey,
                derivedManifest,
                spec.AllowEmpty,
                knownTypes,
                compilation,
                definitionFullName: GetFullyQualifiedTypeName(definitionSymbol),
                locationEntries);

            registrationsBuilder.Add(new ClosedGenericRegistrationInfo(constructedKey, derivedManifest, spec.AllowEmpty, expansionGroup: spec.BaseTargetDisplayName));
            schemasBuilder.Add(constructedMessage);
        }

        if (registrationsBuilder.Count == 0)
            return null;

        return new PrefixExpansionGroup(spec.RegistrationInsertionIndex, spec.SchemaInsertionIndex, registrationsBuilder.ToImmutable(), schemasBuilder.ToImmutable());
    }

    /// <summary>
    /// Re-resolves <paramref name="key"/> to a symbol, recursively <c>Construct()</c>-ing it back
    /// together when it is itself a closed generic construction (<see cref="TypeKey.TypeArguments"/>
    /// non-empty) -- <see cref="Compilation.GetTypeByMetadataName(string)"/> alone only ever resolves
    /// a type's own OPEN DEFINITION (a generic type's metadata name carries its arity, never its
    /// arguments), so a "Fixed" position's own candidate (Decision 18's "G&lt;H&lt;M&gt;&gt;" nested
    /// case, e.g. <c>Envelope&lt;AcceptCassette&gt;</c> as a sibling registration's target) needs this
    /// to resolve correctly; a non-generic implementor key degenerates to a single
    /// <see cref="Compilation.GetTypeByMetadataName(string)"/> call, same as before. Returns null when
    /// any part of the key -- the definition, or any nested argument -- no longer resolves.
    /// </summary>
    private static INamedTypeSymbol? ResolveTypeSymbol(Compilation compilation, TypeKey key)
    {
        if (compilation.GetTypeByMetadataName(key.MetadataName) is not { } definition)
            return null;

        if (key.TypeArguments.IsEmpty)
            return definition;

        var arguments = new ITypeSymbol[key.TypeArguments.Length];
        for (var i = 0; i < key.TypeArguments.Length; i++)
        {
            if (ResolveTypeSymbol(compilation, key.TypeArguments[i]) is not { } argumentSymbol)
                return null;

            arguments[i] = argumentSymbol;
        }

        return definition.Construct(arguments);
    }

    /// <summary>
    /// Every implementor <paramref name="key"/>'s closed set carries in <paramref name="facts"/> --
    /// local first, then referenced-assembly, matching the order the pre-S7 walk
    /// (<c>AddDiscoveredClosedSetMembers</c>) added them in. Both dictionaries already sort their own
    /// per-key array by <see cref="TypeKey.MetadataName"/> (ordinal) -- see
    /// <see cref="CompilationFacts.LocalMarkedImplementorsByClosedSetKey"/>/<see cref="CompilationFacts.ReferencedAssemblyImplementorsByProtocol"/>'s
    /// own doc comments -- so this stays a deterministic expansion order without any sort of its own.
    /// A key with no entry in either dictionary (nothing implements it, or nothing asked about it
    /// before this compilation change) is not an error: the position simply resolves to zero
    /// candidates, exactly as a compilation-wide search that legitimately found nothing did before
    /// this hoist.
    /// </summary>
    private static IEnumerable<TypeKey> GetClosedSetImplementors(CompilationFacts facts, TypeKey key)
    {
        if (facts.LocalMarkedImplementorsByClosedSetKey.TryGetValue(key, out var local))
        {
            foreach (var implementorKey in local)
                yield return implementorKey;
        }

        if (facts.ReferencedAssemblyImplementorsByProtocol.TryGetValue(key, out var referenced))
        {
            foreach (var implementorKey in referenced)
                yield return implementorKey;
        }
    }

    /// <summary>
    /// Every combination of one choice per position, preserving each position's own order and the
    /// positions' own order -- the deterministic order every downstream consumer (dispatch arms,
    /// golden output, the AKKASG042 count) relies on. Moved here verbatim from
    /// AkkaSerializerGenerator.Extraction.cs as part of the S7 hoist: cartesian-product combination is
    /// pure data shuffling with no symbol-walk dependency of its own, but it belongs beside the
    /// symbol-resolution step that now feeds it (<see cref="ExpandOne"/>), not the extraction
    /// transform that no longer does either.
    /// </summary>
    private static List<List<(INamedTypeSymbol Symbol, string Manifest)>> CartesianProduct(
        List<List<(INamedTypeSymbol Symbol, string Manifest)>> positions)
    {
        var result = new List<List<(INamedTypeSymbol, string)>> { new() };
        foreach (var position in positions)
        {
            var next = new List<List<(INamedTypeSymbol, string)>>(result.Count * position.Count);
            foreach (var partial in result)
            {
                foreach (var choice in position)
                {
                    var combination = new List<(INamedTypeSymbol, string)>(partial) { choice };
                    next.Add(combination);
                }
            }

            result = next;
        }

        return result;
    }

    /// <summary>
    /// Merges the S7 expansion stage's own table into every serializer's <see cref="SerializerInfo.ClosedGenericRegistrations"/>/
    /// <see cref="SerializerInfo.ClosedGenericSchemas"/> (see <see cref="SerializerInfo.WithClosedGenericExpansion"/>),
    /// immediately after <see cref="ComputeClosedGenericExpansions"/> runs. This is the ONE merge
    /// point that lets every other consumer of either property -- <see cref="ResolveSerializerMessages"/>,
    /// validation, location resolution, placement diagnostics, and Decision 16's own metadata-schema
    /// seed scan -- keep reading them exactly as before, with no separate expansion-table parameter of
    /// their own. Returns <paramref name="serializers"/> UNCHANGED (same array reference) when the
    /// table is empty -- the overwhelming common case -- rather than rebuilding an identical array.
    /// </summary>
    internal static ImmutableArray<SerializerInfo?> MergeSerializerClosedGenericExpansions(
        ImmutableArray<SerializerInfo?> serializers,
        PrefixExpansionTable expansions,
        CancellationToken cancellationToken)
    {
        if (expansions.EntriesBySerializer.IsEmpty)
            return serializers;

        var builder = ImmutableArray.CreateBuilder<SerializerInfo?>(serializers.Length);
        foreach (var serializer in serializers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (serializer == null)
            {
                builder.Add(null);
                continue;
            }

            var entry = expansions.GetForSerializer(serializer.Key);
            builder.Add(serializer.WithClosedGenericExpansion(entry.Groups));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Splices <paramref name="groups"/> into <paramref name="baseArray"/>, inserting each group's own
    /// <paramref name="itemsSelector"/> items immediately BEFORE the <paramref name="baseArray"/>
    /// element at <paramref name="insertionIndexSelector"/>'s own index (an index equal to
    /// <paramref name="baseArray"/>'s length means "append at the end"). <paramref name="groups"/> must
    /// already be sorted by non-decreasing insertion index -- true by construction for
    /// <see cref="PrefixExpansionEntry.Groups"/>, which is built in <see cref="SerializerInfo.PrefixExpansions"/>'
    /// own (attribute declaration) order, and whose insertion indices only ever grow as that order
    /// advances (see <see cref="PrefixExpansionSpec.RegistrationInsertionIndex"/>'s own doc comment).
    /// Two groups sharing the same insertion index splice in <paramref name="groups"/>' own relative
    /// order (a stable merge), which is exactly the two registrations' own declaration order on the
    /// class. This is what reproduces F2/F3's inline, per-attribute expansion order byte-for-byte,
    /// after S7 moved expansion out of the per-node extraction transform and into its own stage.
    /// </summary>
    internal static ImmutableArray<T> SpliceExpansionGroups<T>(
        ImmutableArray<T> baseArray,
        ImmutableArray<PrefixExpansionGroup> groups,
        Func<PrefixExpansionGroup, int> insertionIndexSelector,
        Func<PrefixExpansionGroup, ImmutableArray<T>> itemsSelector)
    {
        if (groups.IsEmpty)
            return baseArray;

        var builder = ImmutableArray.CreateBuilder<T>();
        var groupIndex = 0;
        for (var i = 0; i <= baseArray.Length; i++)
        {
            while (groupIndex < groups.Length && insertionIndexSelector(groups[groupIndex]) == i)
            {
                builder.AddRange(itemsSelector(groups[groupIndex]));
                groupIndex++;
            }

            if (i < baseArray.Length)
                builder.Add(baseArray[i]);
        }

        return builder.ToImmutable();
    }
}
