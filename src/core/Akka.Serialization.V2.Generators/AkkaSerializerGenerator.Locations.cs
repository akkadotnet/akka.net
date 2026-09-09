//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.Locations.cs" company="Akka.NET Project">
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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akka.Serialization.V2.Generators;

// S6 "locations": every diagnostic must report at the local reference site (Decision 16 in
// openspec/changes/messagepack-sourcegen-validation/design.md), never at Location.None. A Location
// itself can never enter a cached model (a whitespace edit changes every span, so a model carrying
// one would never compare equal across an edit -- see GeneratorArchitectureSpec). This file carries
// locations BESIDE the model instead:
//   - LocationSpec   a value-equatable stand-in for a real Location, built once at extraction time.
//   - LocationKey    identifies WHAT a location is for -- a type declaration, its own attribute, or
//                     one of its named members (a field, or a specific attribute application) --
//                     without saying WHERE it is. DiagnosticSpec.At carries one of these, never a
//                     LocationSpec directly.
//   - LocationBag    the extracted location entries for ONE serializer or message declaration.
//                     ExtractedSerializer/ExtractedMessage carry a LocationBag alongside (never
//                     inside) their schema model, so the schema stays whitespace-insensitive while
//                     the location bag alone absorbs a text-shifting edit.
//
// PERFORMANCE: a location bag is built for EVERY attributed declaration on EVERY extraction re-run
// (the same "every node reruns" cost every other extraction transform already pays -- see
// GeneratorIncrementalScenariosSpec's own doc comment), so its construction cost multiplies by the
// corpus size on every edit, not just the edited node. Two rules keep that cost down:
//   1. A bag stores its entries as a plain ImmutableArray, never an ImmutableDictionary: building a
//      handful of array elements is one allocation; building a handful of dictionary entries is one
//      tree-node allocation PER entry. Lookup (TryGetLocation) is a linear scan, which is fine -- a
//      bag holds a handful of entries, and a lookup only happens when a diagnostic is actually being
//      reported, which is rare compared to how often a bag is built.
//   2. Field-location capture is INLINED into the SAME symbol.GetMembers()/GetAttributes() walk
//      ExtractMessageCore already does for schema extraction (via the optional locationEntries
//      parameter), instead of a second full re-walk. A second walk would re-run GetAttributes() on
//      every property a second time -- redundant work Roslyn does not cache away for free.
//
// THE LOCATION RULE (applied at every DiagnosticSpec call site in Validation.cs/Emission.cs):
//   - field-level diagnostic            -> LocationKey(message.Key, field.Name): the offending
//                                           [AkkaField] property, including a cross-assembly variant
//                                           (Decision 16: report at the LOCAL referencing property,
//                                           never inside the foreign assembly, which gets no
//                                           diagnostic at all).
//   - message type-level diagnostic     -> LocationKey(message.Key, ""): the message type's own
//                                           declaration (its identifier).
//   - serializer-level diagnostic       -> LocationKey(serializer.Key, ""): the [AkkaSerializer<T>]
//                                           attribute application, including AKKASG029 protocol
//                                           coverage (the unmarked implementor is not an attributed
//                                           type in THIS generator's model at all, so there is no
//                                           local site on it to point at, and the facts stage must
//                                           stay whitespace-insensitive to that implementor's own
//                                           declaration).
//   - a specific formatter registration -> LocationKey(serializer.Key, FormatterLocationMember(...)):
//                                           the [AkkaSerializerFormatter<TTarget,TFormatter>]
//                                           application for that target.
//   - a specific closed-generic
//     registration                      -> LocationKey(serializer.Key, ClosedGenericLocationMember(...)):
//                                           the [AkkaSerializable<T>] application for that target
//                                           (Decision 16: "the registration attribute for a closed
//                                           generic"). A closed-generic SCHEMA's own fields are keyed
//                                           by the CONSTRUCTION's TypeKey (e.g. Wrapper<int>), not the
//                                           serializer -- ExtractMessageCore's substituted property
//                                           symbols still resolve Locations back to the GENERIC
//                                           DEFINITION's own declared property, which is exactly the
//                                           right site (there is no separate syntax for Wrapper<int>).
//   - a diagnostic spanning MULTIPLE messages or serializers (duplicate manifest, duplicate
//     generated name, duplicate serializer id, duplicate protocol binding) has no single owning
//     message -> reported at the serializer's attribute (or, for a duplicate-id/duplicate-protocol
//     group spanning several serializers, the first serializer in the group).
public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// A value-equatable stand-in for a real <see cref="Microsoft.CodeAnalysis.Location"/>, captured
    /// once at extraction time. Every field here shifts on a text edit anywhere earlier in the same
    /// file -- that is expected and correct, not a caching bug: it is what lets
    /// <see cref="ToLocation"/> reconstruct a squiggle at the exact right place on THIS run, while
    /// keeping the location entirely OUT of <see cref="SerializerInfo"/>/<see cref="MessageInfo"/>
    /// so neither one's equality (and therefore Collect/Resolve/Emit caching) is ever affected by it.
    /// </summary>
    internal readonly record struct LocationSpec(
        string FilePath,
        int Start,
        int Length,
        int StartLine,
        int StartCharacter,
        int EndLine,
        int EndCharacter)
    {
        /// <summary>Reconstructs a real <see cref="Microsoft.CodeAnalysis.Location"/> from this spec.</summary>
        public Location ToLocation() => Location.Create(
            FilePath,
            new TextSpan(Start, Length),
            new LinePositionSpan(new LinePosition(StartLine, StartCharacter), new LinePosition(EndLine, EndCharacter)));

        /// <summary>
        /// Captures a live <see cref="Microsoft.CodeAnalysis.Location"/> into this value-equatable
        /// form. This is the only place in the generator that reads a live <see cref="Location"/>'s
        /// span/line data; every other consumer works from the captured spec.
        /// </summary>
        public static LocationSpec FromLocation(Location location)
        {
            var span = location.SourceSpan;
            var lineSpan = location.GetLineSpan();
            return new LocationSpec(
                lineSpan.Path ?? string.Empty,
                span.Start,
                span.Length,
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character,
                lineSpan.EndLinePosition.Line,
                lineSpan.EndLinePosition.Character);
        }
    }

    /// <summary>
    /// Identifies WHAT a location is for -- never WHERE it is (that is <see cref="LocationSpec"/>'s
    /// job). <see cref="Owner"/> is the declaring serializer's or message's own <see cref="TypeKey"/>;
    /// <see cref="Member"/> names a specific child site within it. An empty <see cref="Member"/>
    /// means "the type itself" for a message (its declaration) or "its own attribute" for a
    /// serializer (the <c>[AkkaSerializer&lt;TProtocol&gt;]</c> application) -- see
    /// <see cref="FormatterLocationMember"/>/<see cref="ClosedGenericLocationMember"/> for the two
    /// other member shapes a serializer's location bag carries.
    /// </summary>
    internal readonly record struct LocationKey(TypeKey Owner, string Member);

    /// <summary>One (<see cref="LocationKey"/>, <see cref="LocationSpec"/>) pair inside a <see cref="LocationBag"/>.</summary>
    internal readonly record struct LocationEntry(LocationKey Key, LocationSpec Location);

    /// <summary>
    /// The <see cref="LocationKey.Member"/> for a specific <c>[AkkaSerializerFormatter&lt;TTarget,
    /// TFormatter&gt;]</c> application, keyed by its target type's display name (the same string
    /// <see cref="FormatterInfo.TargetTypeFullName"/> already carries, so a diagnostic built from a
    /// resolved <see cref="FormatterInfo"/> always finds the matching entry the location bag recorded
    /// from the same attribute at extraction time).
    /// </summary>
    private static string FormatterLocationMember(string targetTypeFullName) => "formatter:" + targetTypeFullName;

    /// <summary>
    /// The <see cref="LocationKey.Member"/> for a specific <c>[AkkaSerializable&lt;T&gt;]</c>
    /// registration application, keyed by its target type's display name (the same string
    /// <see cref="ClosedGenericRegistrationInfo.TargetDisplayName"/> already carries).
    /// </summary>
    private static string ClosedGenericLocationMember(string targetTypeDisplayName) => "closedGeneric:" + targetTypeDisplayName;

    /// <summary>
    /// The location entries extracted for ONE serializer or message declaration. Kept beside -- never
    /// inside -- <see cref="SerializerInfo"/>/<see cref="MessageInfo"/>, and merged (see
    /// <see cref="MergeLocationBags"/>) into one compilation-wide bag that every diagnostics-only
    /// output combines, purely to resolve <see cref="DiagnosticSpec.At"/> at report time. Backed by a
    /// plain <see cref="ImmutableArray{T}"/>, not a dictionary -- see this file's own header comment
    /// for why that matters for a bag built on every extraction re-run.
    /// </summary>
    internal sealed class LocationBag : IEquatable<LocationBag>
    {
        public static readonly LocationBag Empty = new(ImmutableArray<LocationEntry>.Empty);

        public LocationBag(ImmutableArray<LocationEntry> entries)
        {
            Entries = entries.IsDefault ? ImmutableArray<LocationEntry>.Empty : entries;
        }

        public ImmutableArray<LocationEntry> Entries { get; }

        /// <summary>Linear scan: bags are small, and a lookup only happens when a diagnostic is actually reported.</summary>
        public bool TryGetLocation(LocationKey key, out LocationSpec location)
        {
            foreach (var entry in Entries)
            {
                if (entry.Key.Equals(key))
                {
                    location = entry.Location;
                    return true;
                }
            }

            location = default;
            return false;
        }

        public bool Equals(LocationBag? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return ValueEquality.SequenceEquals(Entries, other.Entries);
        }

        public override bool Equals(object? obj) => Equals(obj as LocationBag);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Entries);
            return hash;
        }
    }

    /// <summary>
    /// The raw per-node output of the serializer extraction transform: the schema
    /// (<see cref="Info"/>, symbol-free and whitespace-insensitive) plus this declaration's
    /// <see cref="LocationBag"/> (whitespace-SENSITIVE by design). <see cref="Initialize"/> projects
    /// out a schemas-only <see cref="TrackingNames.SerializerSchemas"/> stage for Collect/Resolve/Emit
    /// -- unaffected by a text-shifting edit that changes only <see cref="Locations"/> -- and
    /// collects THIS type directly (no separate locations-only projection) to build the merged
    /// location bag; see <see cref="MergeExtractedLocations"/>.
    /// </summary>
    internal readonly record struct ExtractedSerializer(SerializerInfo? Info, LocationBag Locations);

    /// <summary>The message extraction transform's counterpart to <see cref="ExtractedSerializer"/>.</summary>
    internal readonly record struct ExtractedMessage(MessageInfo? Info, LocationBag Locations);

    /// <summary>
    /// Merges every extracted serializer's and message's location bag into ONE compilation-wide bag,
    /// by plain concatenation into a single pre-sized array -- cheaper than folding into a dictionary
    /// one entry at a time. <see cref="LocationKey"/> values never collide across a serializer and a
    /// message (each key's <see cref="TypeKey"/> owner identifies exactly one declared type), so no
    /// de-duplication is needed. Reads <see cref="ExtractedSerializer.Locations"/>/
    /// <see cref="ExtractedMessage.Locations"/> straight off the COLLECTED raw extraction results --
    /// there is deliberately no separate locations-only <c>Select</c> stage feeding this (see
    /// <see cref="Initialize"/>'s own comment on <c>allLocations</c>): a per-node Select still builds
    /// its own incremental state table over every node on every edit for a value this function reads
    /// back only once, batched, so collecting the raw values directly and projecting HERE is the same
    /// merged bag for one fewer per-node Select.
    /// </summary>
    private static LocationBag MergeExtractedLocations(
        ImmutableArray<ExtractedSerializer> serializers,
        ImmutableArray<ExtractedMessage> messages,
        CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var extracted in serializers)
            total += extracted.Locations.Entries.Length;
        foreach (var extracted in messages)
            total += extracted.Locations.Entries.Length;

        var builder = ImmutableArray.CreateBuilder<LocationEntry>(total);
        foreach (var extracted in serializers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AddRange(extracted.Locations.Entries);
        }

        foreach (var extracted in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AddRange(extracted.Locations.Entries);
        }

        return new LocationBag(builder.MoveToImmutable());
    }

    /// <summary>Appends an entry for <paramref name="location"/> under <paramref name="key"/>, silently skipping a location this generator could not resolve (never fabricated).</summary>
    private static void AddLocationEntry(ImmutableArray<LocationEntry>.Builder entries, LocationKey key, Location? location)
    {
        if (location != null && location.SourceTree != null)
            entries.Add(new LocationEntry(key, LocationSpec.FromLocation(location)));
    }

    /// <summary>
    /// A symbol's first declared <see cref="Location"/>, or null. Avoids
    /// <see cref="System.Linq.Enumerable.FirstOrDefault{T}(IEnumerable{T})"/>'s interface-dispatch
    /// overhead on <see cref="ImmutableArray{T}"/> -- this runs once per <c>[AkkaField]</c> property
    /// on every extraction re-run (see this file's own header comment), so a plain index check is
    /// worth it here even though the two are otherwise equivalent.
    /// </summary>
    private static Location? FirstLocationOrNull(ImmutableArray<Location> locations)
    {
        return locations.Length > 0 ? locations[0] : null;
    }

    /// <summary>The <see cref="Location"/> of an attribute APPLICATION (e.g. <c>[AkkaSerializer&lt;T&gt;(...)]</c>), not the attribute list's brackets.</summary>
    private static Location? GetAttributeLocation(AttributeData attribute, CancellationToken cancellationToken)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
    }

    /// <summary>
    /// Appends the location entries for one <c>[AkkaSerializer&lt;TProtocol&gt;]</c> declaration into
    /// <paramref name="entries"/>: the serializer's own attribute (<see cref="LocationKey.Member"/>
    /// empty), plus one entry per <c>[AkkaSerializerFormatter&lt;TTarget,TFormatter&gt;]</c> and
    /// <c>[AkkaSerializable&lt;T&gt;]</c> attribute application found directly on the class. Re-scans
    /// <paramref name="symbol"/>'s attributes independently of <see cref="ExtractFormatters"/>/
    /// <see cref="ExtractClosedGenericRegistrations"/> -- there are at most a handful of attributes on
    /// any one serializer class (unlike a message's potentially many <c>[AkkaField]</c> properties, a
    /// cost this file's header comment calls out separately), so a second small scan here costs far
    /// less than the field-level duplication would. Reads <see cref="KnownTypes.FormatterAttribute"/>/
    /// <see cref="KnownTypes.GenericSerializableAttribute"/> from the S5 per-compilation cache (see
    /// <see cref="GetKnownTypes"/>) instead of its own <c>GetTypeByMetadataName</c> calls -- those two
    /// symbols never change within one compilation, so re-resolving them per serializer node would be
    /// exactly the redundant per-node lookup S5 already eliminated for every other attribute type.
    /// </summary>
    private static void BuildSerializerLocationBag(
        ImmutableArray<LocationEntry>.Builder entries,
        INamedTypeSymbol symbol,
        AttributeData serializerAttribute,
        TypeKey serializerKey,
        KnownTypes knownTypes,
        CancellationToken cancellationToken)
    {
        AddLocationEntry(entries, new LocationKey(serializerKey, string.Empty), GetAttributeLocation(serializerAttribute, cancellationToken));

        if (knownTypes.FormatterAttribute == null && knownTypes.GenericSerializableAttribute == null)
            return;

        foreach (var attribute in symbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is not { IsGenericType: true } attributeClass)
                continue;

            if (knownTypes.FormatterAttribute != null && SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, knownTypes.FormatterAttribute))
            {
                var targetKey = TypeKey.FromSymbol(attributeClass.TypeArguments[0]);
                var member = FormatterLocationMember(targetKey.DisplayName ?? string.Empty);
                AddLocationEntry(entries, new LocationKey(serializerKey, member), GetAttributeLocation(attribute, cancellationToken));
                continue;
            }

            if (knownTypes.GenericSerializableAttribute != null && SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, knownTypes.GenericSerializableAttribute))
            {
                var targetKey = TypeKey.FromSymbol(attributeClass.TypeArguments[0]);
                var member = ClosedGenericLocationMember(targetKey.DisplayName ?? string.Empty);
                AddLocationEntry(entries, new LocationKey(serializerKey, member), GetAttributeLocation(attribute, cancellationToken));
            }
        }
    }

    /// <summary>
    /// The <see cref="LocationKey"/> for a TYPE-LEVEL diagnostic (<see cref="LocationKey.Member"/>
    /// empty) on <paramref name="message"/> within <paramref name="serializer"/>'s table. Ordinarily
    /// this is the message's own declaration (<c>LocationKey(message.Key, "")</c>). But when
    /// <paramref name="message"/> IS one of <paramref name="serializer"/>'s closed-generic SCHEMA
    /// entries (its key matches a <see cref="ClosedGenericRegistrationInfo.Target"/>), the message's
    /// own key has no location bag entry to resolve: a CLOSED CONSTRUCTION (e.g. <c>Wrapper&lt;int&gt;</c>)
    /// has no separate syntax of its own -- only the GENERIC DEFINITION does. In that case the closed
    /// construction's own <c>[AkkaSerializable&lt;T&gt;]</c> registration attribute IS the local
    /// reference site (Decision 16), so this returns that instead -- the SAME key
    /// <see cref="BuildSerializerLocationBag"/> already populated for it.
    /// </summary>
    private static LocationKey MessageTypeLocationKey(SerializerInfo serializer, MessageInfo message)
    {
        foreach (var registration in serializer.ClosedGenericRegistrations)
        {
            if (registration.Target.Equals(message.Key))
                return new LocationKey(serializer.Key, ClosedGenericLocationMember(registration.TargetDisplayName));
        }

        return new LocationKey(message.Key, string.Empty);
    }

    /// <summary>A location bag holding only a type's own declaration entry -- used for a generic <c>[AkkaSerializable]</c> DEFINITION placeholder, which has no fields of its own to add (see <see cref="ExtractMessage"/>).</summary>
    private static LocationBag BuildTypeOnlyLocationBag(INamedTypeSymbol symbol, TypeKey key)
    {
        var location = FirstLocationOrNull(symbol.Locations);
        if (location == null || location.SourceTree == null)
            return LocationBag.Empty;

        return new LocationBag(ImmutableArray.Create(new LocationEntry(new LocationKey(key, string.Empty), LocationSpec.FromLocation(location))));
    }
}
