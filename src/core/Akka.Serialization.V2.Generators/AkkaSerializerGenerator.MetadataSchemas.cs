//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.MetadataSchemas.cs" company="Akka.NET Project">
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

// Decision 16 (openspec/changes/messagepack-sourcegen-validation/design.md): schemas from
// referenced-assembly metadata, for a nested field type and a union member type. A closed generic
// registration's own definition already resolves this way today (ExtractClosedGenericRegistrations
// reads the type argument straight off the symbol) -- this file extends the SAME technique to the
// two cases that used to be syntax-only: a plain [AkkaSerializable] type named as a nested field, and
// one named as a union member.
//
// WHERE THIS STAGE LIVES: a per-compilation stage next to CompilationFacts (AkkaSerializerGenerator.Facts.cs),
// wired the same way -- context.CompilationProvider.Combine(...).Select(ComputeMetadataSchemas) -- but
// kept in its own file because, unlike Facts.cs's walks (which only enumerate/bucket symbols), this
// stage does real extraction work: it resolves a symbol from metadata and runs it through
// ExtractMessageCore, the same routine a local type's own extraction uses.
//
// CACHE KEY: the set of referenced type keys the local serializers actually reference (Object-mapped
// nested fields and union members whose ForeignAssemblyName is non-empty and whose TypeKey is
// non-generic -- see ComputeReferencedTypeKeys). That set is a plain value computed from the already-
// collected, symbol-free messages/serializers arrays, so it is stable across an edit that touches
// neither: this stage is wired as context.CompilationProvider.Combine(messages).Combine(serializers).Select(...),
// exactly like ComputeCompilationFacts, so it recomputes on every edit (Compilation never itself
// compares equal across a run) but its OUTPUT compares equal -- reporting Unchanged, never Cached --
// whenever the referenced-type-key set and everything it resolves to are unchanged. See
// TrackingNames.MetadataSchemas and Initialize's own wiring comment for the run-reason consequence
// this has on ResolvedSerializers (which must combine this stage directly, unlike CompilationFacts).
public sealed partial class AkkaSerializerGenerator
{
    /// <summary>
    /// Builds the whole-compilation <see cref="MetadataSchemaTable"/>: resolves every referenced-
    /// assembly type reachable (directly, or nested arbitrarily deep through other referenced-assembly
    /// types) from <paramref name="messages"/>' and <paramref name="serializers"/>' own foreign
    /// Object/union references, the same way <c>ExtractMessageCore</c> resolves a local type.
    /// </summary>
    internal static MetadataSchemaTable ComputeMetadataSchemas(
        Compilation compilation,
        ImmutableArray<MessageInfo?> messages,
        ImmutableArray<SerializerInfo?> serializers,
        CancellationToken cancellationToken)
    {
        var seeds = ComputeReferencedTypeKeys(messages, serializers);
        if (seeds.IsEmpty)
            return MetadataSchemaTable.Empty;

        var knownTypes = GetKnownTypes(compilation);
        var candidateSchemas = new Dictionary<TypeKey, MessageInfo>();
        var failures = new Dictionary<TypeKey, AccessibilityFailure>();
        var visited = new HashSet<TypeKey>(seeds);
        var pending = new Queue<TypeKey>(seeds);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = pending.Dequeue();

            var symbol = compilation.GetTypeByMetadataName(key.MetadataName);
            if (symbol == null)
            {
                // No metadata symbol resolves for this key (an ambiguous name across several
                // referenced assemblies, or a stale key) -- leave it out of both maps. The existing
                // AKKASG007/AKKASG015 "missing schema" diagnostics already cover "no schema found"
                // with no further help needed here.
                continue;
            }

            if (!IsAccessibleFromCompilation(symbol, compilation))
            {
                failures[key] = DescribeTypeFailure(symbol);
                continue;
            }

            if (knownTypes.SerializableAttribute == null || !HasSerializableAttribute(symbol, knownTypes))
            {
                // Not [AkkaSerializable] at all -- not an accessibility problem. Falls through to the
                // existing missing-attribute diagnostic, unchanged.
                continue;
            }

            var (manifest, allowEmpty) = ReadSerializableArguments(symbol, knownTypes);
            var message = ExtractMessageCore(symbol, key, manifest, allowEmpty, knownTypes, compilation, definitionFullName: string.Empty);

            // ExtractMessageCore ALREADY tells apart an [AkkaField] property this generated code
            // cannot read: a property whose OWN accessibility is high enough to appear in
            // symbol.GetMembers() (so ExtractMessageCore's ordinary walk sees it at all) but whose
            // getter is separately restricted resolves with member.GetMethod == null -- Roslyn's
            // metadata importer hides an accessor this compilation cannot call, exactly the "getter
            // hidden" shape a public-property/internal-getter split produces. ExtractMessageCore
            // records that as an InvalidFieldInfo ("has no accessible getter"), the SAME path a local
            // static or inaccessible-getter property already takes (AKKASG028). A NO-MATCHING-
            // CONSTRUCTOR result can have the same root cause: every accessible constructor Roslyn
            // shows this compilation may fail to cover every required field for the same reason. A
            // referenced type carrying either shows up here as an accessibility failure (AKKASG039)
            // instead of AKKASG028/AKKASG026 with no local site to report at -- see
            // DescribeSchemaShapeFailure. A property whose OWN accessibility (not just its getter's)
            // is too low is invisible to symbol.GetMembers() entirely and produces neither shape here;
            // that field is silently absent from the schema, a Roslyn-level limit no diagnostic can
            // observe from B's side.
            if (!message.InvalidFields.IsDefaultOrEmpty || !message.ConstructionPlan.Errors.IsDefaultOrEmpty)
            {
                failures[key] = DescribeSchemaShapeFailure(symbol, message);
                continue;
            }

            candidateSchemas[key] = message;

            var nestedKeys = new HashSet<TypeKey>();
            CollectForeignReferences(message, nestedKeys);
            foreach (var nestedKey in nestedKeys)
            {
                if (visited.Add(nestedKey))
                    pending.Enqueue(nestedKey);
            }
        }

        PropagateNestedFailures(candidateSchemas, failures);

        return new MetadataSchemaTable(candidateSchemas.ToImmutableDictionary(), failures.ToImmutableDictionary());
    }

    /// <summary>
    /// A candidate schema that itself names a type now known to be a failure is a failure too
    /// (Decision 16: "the failure can sit one level down, inside A's own schema ... it does not stop
    /// at the property in B that referenced it"). Runs to a fixed point: propagating one failure can
    /// turn a second, previously-clean candidate into a failure too, when it nests the first.
    /// Attributes the propagated failure to whichever nested reference was found broken, in
    /// <see cref="TypeKey.MetadataName"/> order, for a deterministic choice when a candidate nests
    /// more than one broken reference. Only ever does real work when <paramref name="candidateSchemas"/>
    /// is non-empty, which requires at least one referenced-assembly type to have resolved -- never
    /// the case for a compilation with no cross-assembly references at all.
    /// </summary>
    private static void PropagateNestedFailures(Dictionary<TypeKey, MessageInfo> candidateSchemas, Dictionary<TypeKey, AccessibilityFailure> failures)
    {
        if (candidateSchemas.Count == 0)
            return;

        var nestedKeys = new HashSet<TypeKey>();
        bool changed;
        do
        {
            changed = false;
            foreach (var key in candidateSchemas.Keys.ToImmutableArray())
            {
                if (failures.ContainsKey(key))
                    continue;

                nestedKeys.Clear();
                CollectForeignReferences(candidateSchemas[key], nestedKeys);

                TypeKey? brokenNestedKey = null;
                foreach (var nestedKey in nestedKeys)
                {
                    if (!failures.ContainsKey(nestedKey))
                        continue;

                    if (brokenNestedKey == null || string.CompareOrdinal(nestedKey.MetadataName, brokenNestedKey.Value.MetadataName) < 0)
                        brokenNestedKey = nestedKey;
                }

                if (brokenNestedKey == null)
                    continue;

                failures[key] = failures[brokenNestedKey.Value];
                candidateSchemas.Remove(key);
                changed = true;
            }
        } while (changed);
    }

    /// <summary>
    /// Every distinct, non-generic referenced-assembly <see cref="TypeKey"/> that a local message's
    /// field (an Object mapping, recursively through collections) or union member names, plus every
    /// closed-generic schema a serializer has already registered (its own fields can reference a
    /// foreign type too) -- the seed set for <see cref="ComputeMetadataSchemas"/>'s resolution walk.
    /// A generic construction is excluded (<see cref="TypeMapping.IsGenericConstruction"/>, or a
    /// union member key with type arguments): Decision 16 does not change how a closed generic
    /// construction resolves -- that already works through <c>ExtractClosedGenericRegistrations</c>'
    /// own symbol-based extraction. Sorted by <see cref="TypeKey.MetadataName"/> for a stable,
    /// order-independent result.
    /// </summary>
    /// <remarks>
    /// This runs on EVERY compilation change (see this file's own header comment on the cache key),
    /// over every declared message, so it deliberately avoids an iterator-method per message: an
    /// iterator method allocates its state machine the moment it is called, even when it yields
    /// nothing, which is the overwhelmingly common case (a message with no cross-assembly reference
    /// at all). <see cref="CollectForeignReferences(MessageInfo,HashSet{TypeKey})"/> instead writes
    /// directly into a reused <see cref="HashSet{T}"/>.
    /// </remarks>
    private static ImmutableArray<TypeKey> ComputeReferencedTypeKeys(ImmutableArray<MessageInfo?> messages, ImmutableArray<SerializerInfo?> serializers)
    {
        var seen = new HashSet<TypeKey>();

        foreach (var message in messages)
        {
            if (message == null || message.IsGenericDefinition)
                continue;

            CollectForeignReferences(message, seen);
        }

        foreach (var serializer in serializers)
        {
            if (serializer == null || serializer.ClosedGenericSchemas.IsDefaultOrEmpty)
                continue;

            foreach (var schema in serializer.ClosedGenericSchemas)
                CollectForeignReferences(schema, seen);
        }

        if (seen.Count == 0)
            return ImmutableArray<TypeKey>.Empty;

        var array = seen.ToImmutableArray();
        return array.Sort(static (a, b) => string.CompareOrdinal(a.MetadataName, b.MetadataName));
    }

    /// <summary>
    /// Adds every non-generic, foreign-assembly <see cref="TypeKey"/> one message's fields name,
    /// either as a nested Object mapping (recursively through collections) or as a union member, into
    /// <paramref name="destination"/>. Shared by <see cref="ComputeReferencedTypeKeys"/> (the seed
    /// set), <see cref="ComputeMetadataSchemas"/> itself (continuing the walk from a resolved
    /// metadata schema's own fields), and <see cref="PropagateNestedFailures"/>. Writes directly into
    /// the caller's set instead of yielding, so a message with no cross-assembly reference at all --
    /// the common case for every message in a compilation with none -- costs no allocation beyond the
    /// (already-materialized) field/mapping walk itself.
    /// </summary>
    private static void CollectForeignReferences(MessageInfo message, HashSet<TypeKey> destination)
    {
        foreach (var field in message.Fields)
        {
            CollectForeignObjectMappings(field.Mapping, destination);

            foreach (var unionMember in field.UnionMembers)
            {
                if (unionMember.ForeignAssemblyName.Length > 0 && unionMember.Key.TypeArguments.IsEmpty)
                    destination.Add(unionMember.Key);
            }
        }
    }

    /// <summary>
    /// Recursive counterpart of <see cref="EnumerateObjectMappings"/>, narrowed to foreign,
    /// non-generic Object mappings and writing directly into <paramref name="destination"/> instead
    /// of yielding every Object mapping regardless of origin.
    /// </summary>
    private static void CollectForeignObjectMappings(TypeMapping mapping, HashSet<TypeKey> destination)
    {
        if (mapping.Kind == FieldKind.Object && mapping.ForeignAssemblyName.Length > 0 && !mapping.IsGenericConstruction && mapping.Key.TypeArguments.IsEmpty)
            destination.Add(mapping.Key);

        foreach (var argument in mapping.TypeArguments)
            CollectForeignObjectMappings(argument, destination);
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> would be accessible from code written anywhere in this
    /// compilation's own assembly -- correctly honoring <c>[InternalsVisibleTo]</c>, unlike
    /// <see cref="IsAccessibleFromGeneratedCode"/>'s plain declared-accessibility check (which is only
    /// safe for a type declared in THIS compilation, where internal always means accessible).
    /// </summary>
    private static bool IsAccessibleFromCompilation(ISymbol symbol, Compilation compilation)
    {
        return compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);
    }

    private static bool HasSerializableAttribute(INamedTypeSymbol symbol, KnownTypes knownTypes)
    {
        return symbol.OriginalDefinition.GetAttributes()
            .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
    }

    private static (string Manifest, bool AllowEmpty) ReadSerializableArguments(INamedTypeSymbol symbol, KnownTypes knownTypes)
    {
        var attribute = symbol.OriginalDefinition.GetAttributes()
            .First(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));

        var manifest = string.Empty;
        var allowEmpty = false;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Manifest" && argument.Value.Value is string value)
                manifest = value;
            else if (argument.Key == "AllowEmpty" && argument.Value.Value is bool allowEmptyValue)
                allowEmpty = allowEmptyValue;
        }

        return (manifest, allowEmpty);
    }

    private static AccessibilityFailure DescribeTypeFailure(INamedTypeSymbol symbol)
    {
        var typeName = ToDisplayName(GetFullyQualifiedTypeName(symbol));
        return new AccessibilityFailure($"type '{typeName}' {DescribeAccessibilityReason(symbol.DeclaredAccessibility)}");
    }

    /// <summary>
    /// Describes why <paramref name="message"/>'s extraction hit a structural problem
    /// <c>ExtractMessageCore</c> already detects (see the caller's own comment):
    /// <see cref="MessageInfo.InvalidFields"/> non-empty (a property this compilation cannot fully
    /// read -- most often the "non-public property" shape from Decision 16, a property visible
    /// enough to appear in <c>GetMembers()</c> but whose getter this compilation cannot call), or
    /// <see cref="ConstructionPlan.Errors"/> non-empty (no accessible constructor covers every
    /// required field, which can have the identical root cause: every constructor this compilation
    /// can see fails to cover a field whose own property this compilation cannot read either).
    /// Reports the FIRST problem found, in each collection's own declaration order, for a
    /// deterministic choice when more than one field is affected.
    /// </summary>
    private static AccessibilityFailure DescribeSchemaShapeFailure(INamedTypeSymbol symbol, MessageInfo message)
    {
        var typeName = ToDisplayName(GetFullyQualifiedTypeName(symbol));

        if (!message.InvalidFields.IsDefaultOrEmpty)
        {
            var invalidField = message.InvalidFields[0];
            return new AccessibilityFailure($"property '{invalidField.PropertyName}' on type '{typeName}' {invalidField.Reason}");
        }

        var error = message.ConstructionPlan.Errors[0];
        return new AccessibilityFailure($"type '{typeName}' cannot be reconstructed from this assembly: {error}");
    }

    private static string DescribeAccessibilityReason(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Private => "is private",
            Accessibility.ProtectedAndInternal => "is private protected",
            Accessibility.Protected => "is protected",
            Accessibility.Internal => "is internal, and its assembly does not grant this assembly access via [InternalsVisibleTo]",
            Accessibility.ProtectedOrInternal => "is protected internal, and its assembly does not grant this assembly access via [InternalsVisibleTo]",
            _ => "is not accessible from this assembly"
        };
    }
}
