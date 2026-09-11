//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGenerator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akka.Serialization.V2.Generators;

// AkkaSerializerGenerator is split into phase files (all `partial class AkkaSerializerGenerator`,
// same namespace) so each of the pipeline's stages can be edited independently:
//   AkkaSerializerGenerator.cs             - this file: the [Generator] entry point, Initialize and
//                                             the incremental pipeline wiring, tracking-name constants.
//   AkkaSerializerGenerator.Diagnostics.cs - every DiagnosticDescriptor, in id order.
//   AkkaSerializerGenerator.Extraction.cs  - symbol-to-model extraction: ExtractSerializer*,
//                                             ExtractMessageCore, ExtractClosedGenericRegistrations,
//                                             ExtractUnionMembers, field-type mapping, KnownTypes
//                                             (and its per-Compilation cache), constructor matching.
//   AkkaSerializerGenerator.Facts.cs       - the S5 whole-compilation facts stage: ComputeCompilationFacts
//                                             and its helpers (the local-implementor walk, the
//                                             referenced-assembly-references-V2 filter, and the
//                                             Decision 19 referenced-assembly implementor skeleton).
//   AkkaSerializerGenerator.MetadataSchemas.cs - Decision 16's metadata-schema stage: ComputeMetadataSchemas
//                                             and its helpers. A second per-compilation stage, next to
//                                             Facts.cs but kept separate because it does real
//                                             extraction work (resolves a referenced-assembly symbol
//                                             and runs it through ExtractMessageCore), not just a walk.
//   AkkaSerializerGenerator.Validation.cs  - validation over the collected models and diagnostic
//                                             reporting: ValidateMessages, ValidateUnionField,
//                                             ValidateClosedGenericProtocolCoverage,
//                                             CollectReachableMessages, the Report* helpers.
//   AkkaSerializerGenerator.Emission.cs    - the per-serializer resolve stage (ResolveSerializer)
//                                             and source emission: EmitResolvedSerializer, Generate*,
//                                             union helper planning, naming/folding, collision handling.
//   AkkaSerializerGenerator.Models.cs      - the model records (SerializerInfo, MessageInfo,
//                                             FieldInfo, TypeMapping, UnionMemberInfo, CompilationFacts, ...).
[Generator]
public sealed partial class AkkaSerializerGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Stable names for the pipeline's cache-relevant incremental nodes. Exposed publicly for the
    /// incrementality regression spec, which runs the generator twice over compilations differing
    /// only by an unrelated edit and asserts every step named here reports
    /// <see cref="IncrementalStepRunReason.Cached"/> or <see cref="IncrementalStepRunReason.Unchanged"/>.
    /// </summary>
    public static class TrackingNames
    {
        /// <summary>
        /// The raw per-node serializer extraction transform: <see cref="ExtractedSerializer"/>, the
        /// schema PLUS its location bag. Whitespace-SENSITIVE (a text-shifting edit anywhere earlier
        /// in the file changes the location bag's spans), unlike every stage downstream of
        /// <see cref="SerializerSchemas"/> -- see that constant's own doc comment.
        /// </summary>
        public const string ExtractedSerializers = nameof(ExtractedSerializers);

        /// <summary>
        /// The schemas-only projection of <see cref="ExtractedSerializers"/> (<c>.Select(e =&gt;
        /// e.Info)</c>): whitespace-INSENSITIVE, since <see cref="SerializerInfo"/> carries no
        /// location. Feeds <see cref="CollectedSerializers"/>, <see cref="ResolvedSerializers"/>, and
        /// code emission -- exactly what <see cref="ExtractedSerializers"/> itself used to feed before
        /// S6 added locations, so those stages stay cached on an edit that only shifts spans. There is
        /// deliberately NO locations-only counterpart to this stage: an earlier version of this
        /// pipeline had one (<c>SerializerLocations</c>/<c>MessageLocations</c>), but a per-node Select
        /// still builds its own incremental state table over every node on every edit, for a value read
        /// back only once, batched -- see <see cref="Initialize"/>'s own comment on
        /// <c>allLocations</c> for why collecting the raw extracted values directly is cheaper.
        /// </summary>
        public const string SerializerSchemas = nameof(SerializerSchemas);

        public const string CollectedSerializers = nameof(CollectedSerializers);

        /// <summary>The message extraction transform's counterpart to <see cref="ExtractedSerializers"/>. See that constant's doc comment.</summary>
        public const string ExtractedMessages = nameof(ExtractedMessages);

        /// <summary>The message extraction transform's counterpart to <see cref="SerializerSchemas"/>. See that constant's doc comment.</summary>
        public const string MessageSchemas = nameof(MessageSchemas);

        public const string CollectedMessages = nameof(CollectedMessages);

        /// <summary>
        /// The per-serializer <see cref="ResolvedSerializer"/> stage: a <c>Select</c> over each
        /// serializer PLUS the full collected messages array (see <see cref="ResolveSerializer"/>).
        /// An edit to any message recomputes every serializer's step here -- that dependency cannot
        /// be avoided, since a message's top-level/reachable status depends on every other message
        /// too -- but the RESULT for a serializer untouched by the edit compares equal to its
        /// previous run, which is what lets code emission (registered directly on this stage) skip
        /// re-emitting that one serializer's file.
        /// </summary>
        public const string ResolvedSerializers = nameof(ResolvedSerializers);

        /// <summary>
        /// The S5 whole-compilation facts stage: <c>context.CompilationProvider.Combine(CollectedSerializers).Combine(CollectedMessages).Select(...)</c>,
        /// producing one cached <see cref="CompilationFacts"/> value per compilation change (see
        /// <see cref="ComputeCompilationFacts"/>). Feeds the AKKASG029 coverage output and the
        /// placement-diagnostics output in place of the raw <see cref="Compilation"/> those outputs
        /// used to combine with directly -- see <see cref="ReportProtocolCoverage"/> and
        /// <see cref="ReportPlacementDiagnostics"/>. As of Decisions 19 and 21, this IS combined
        /// directly into <see cref="ResolvedSerializers"/>'s own inputs: a referenced-assembly
        /// protocol or marked-union-base implementor's closed-set membership must be visible to
        /// <see cref="ResolveSerializerMessages"/> for top-level dispatch widening and the implicit
        /// protocol/union-field rule to resolve at all -- the same reason <see cref="MetadataSchemas"/>
        /// is combined in directly. This stage's own walk (<see cref="ComputeLocalMarkedImplementorsByClosedSetKey"/>,
        /// <see cref="ComputeReferencedAssemblyImplementors"/>, <see cref="ComputeUpstreamSerializerBindings"/>)
        /// is real, not a skeleton.
        /// </summary>
        public const string CompilationFacts = nameof(CompilationFacts);

        /// <summary>
        /// Decision 16's whole-compilation metadata-schema stage: <c>context.CompilationProvider.Combine(messages).Combine(serializers).Select(...)</c>,
        /// producing one cached <see cref="MetadataSchemaTable"/> value per compilation change (see
        /// <see cref="ComputeMetadataSchemas"/>). Unlike <see cref="CompilationFacts"/> -- which is
        /// deliberately NOT combined into <see cref="ResolvedSerializers"/>' own inputs yet -- this
        /// stage IS combined directly into <see cref="ResolvedSerializers"/>: a referenced-assembly
        /// nested field or union member cannot resolve at all unless <see cref="ResolveSerializerMessages"/>
        /// can see its schema. Because this stage combines <see cref="Microsoft.CodeAnalysis.IIncrementalGenerator"/>'s
        /// <c>CompilationProvider</c> directly, it recomputes on every edit like
        /// <see cref="CompilationFacts"/> does -- but its OUTPUT compares equal whenever the
        /// referenced type-key set and everything it resolves to are unchanged: an edit that touches
        /// neither the referenced-assembly reference list nor any message that names a
        /// referenced-assembly type leaves this stage reporting
        /// <see cref="Microsoft.CodeAnalysis.IncrementalStepRunReason.Unchanged"/> (recomputed, but
        /// equal). Empirically (see GeneratorIncrementalScenariosSpec's scenario (f)) that equality is
        /// enough for the driver to still recognize <see cref="ResolvedSerializers"/>' own combined
        /// input as unchanged and report <see cref="Microsoft.CodeAnalysis.IncrementalStepRunReason.Cached"/>
        /// for it too, exactly like scenario (a)'s pre-existing <c>serializers.Combine(messages)</c>
        /// input -- adding this third combined input does not, in practice, downgrade
        /// <see cref="ResolvedSerializers"/>' best case for an edit this stage does not care about.
        /// </summary>
        public const string MetadataSchemas = nameof(MetadataSchemas);

        public static ImmutableArray<string> All { get; } = ImmutableArray.Create(
            ExtractedSerializers, SerializerSchemas, CollectedSerializers,
            ExtractedMessages, MessageSchemas, CollectedMessages,
            ResolvedSerializers, CompilationFacts, MetadataSchemas);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // S6 "locations": the raw per-node transform returns the schema PLUS a location bag (see
        // ExtractedSerializer/AkkaSerializerGenerator.Locations.cs) -- a text-shifting edit anywhere
        // earlier in the file changes the bag's spans, so this raw stage is whitespace-SENSITIVE,
        // unlike every stage before S6. A schemas-only projection immediately splits the schema back
        // out and feeds Collect/Resolve/Emit exactly as the raw stage itself used to
        // (whitespace-insensitive, since SerializerInfo carries no location). This is what lets a
        // comment-only edit still report Collect/Resolve/Emit as Cached even though the raw
        // extraction step itself reports Modified for it -- see GeneratorIncrementalScenariosSpec's
        // scenario (c).
        //
        // There is deliberately NO separate locations-only Select node here (an earlier version of
        // this stage had one per side): a per-node Select still builds its own incremental state
        // table over every node on every edit, for a value (LocationBag) that is read back only once,
        // batched, a few lines down. Collecting the raw ExtractedSerializer/ExtractedMessage values
        // directly and reading .Locations inside MergeExtractedLocations gets the same merged bag one
        // Select cheaper. The schemas-only projections below are NOT removed the same way: Resolve and
        // Emit depend on them directly, so they earn their keep as separate nodes.
        var extractedSerializers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializerAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, cancellationToken) => ExtractSerializer(ctx, cancellationToken))
            .WithTrackingName(TrackingNames.ExtractedSerializers);

        var serializerSchemas = extractedSerializers
            .Select(static (extracted, _) => extracted.Info)
            .WithTrackingName(TrackingNames.SerializerSchemas);

        var serializers = serializerSchemas
            .Where(static info => info != null)
            .Collect()
            .WithTrackingName(TrackingNames.CollectedSerializers);

        var extractedMessages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, cancellationToken) => ExtractMessage(ctx, cancellationToken))
            .WithTrackingName(TrackingNames.ExtractedMessages);

        var messageSchemas = extractedMessages
            .Select(static (extracted, _) => extracted.Info)
            .WithTrackingName(TrackingNames.MessageSchemas);

        var messages = messageSchemas
            .Where(static info => info != null)
            .Collect()
            .WithTrackingName(TrackingNames.CollectedMessages);

        // Every serializer's and message's location bag, merged into ONE compilation-wide lookup.
        // Feeds every diagnostics-only Report output below, never Collect/Resolve/Emit -- see
        // MergeExtractedLocations's own doc comment.
        var allLocations = extractedSerializers.Collect()
            .Combine(extractedMessages.Collect())
            .Select(static (pair, cancellationToken) => MergeExtractedLocations(pair.Left, pair.Right, cancellationToken));

        // The S5 whole-compilation facts stage: everything the pipeline needs to know about the
        // WHOLE compilation, computed ONCE per compilation change and shared by every serializer --
        // see ComputeCompilationFacts and CompilationFacts's own doc comment. Combines the live
        // Compilation (this stage genuinely needs it, exactly like the coverage scan it replaces)
        // with the collected serializers (their protocol keys: which protocol interfaces anyone
        // actually asked about) and, as of Decisions 19 and 21, the collected messages too (every
        // discovered-mode union field's own static-type key -- a marked union base with no listed
        // members). The OUTPUT is symbol-free and value-equatable, so -- unlike the raw Compilation
        // this used to be combined with directly -- a downstream consumer wired to THIS stage can
        // report Unchanged/Cached whenever nothing these facts care about changed, even though the
        // stage itself reruns on every edit (the CompilationProvider input never itself compares
        // equal across edits).
        var compilationFacts = context.CompilationProvider
            .Combine(serializers)
            .Combine(messages)
            .Select(static (pair, cancellationToken) => ComputeCompilationFacts(pair.Left.Left, pair.Left.Right, pair.Right, cancellationToken))
            .WithTrackingName(TrackingNames.CompilationFacts);

        // Decision 16's whole-compilation metadata-schema stage: everything the pipeline needs to
        // read a nested field's or a union member's schema from a REFERENCED assembly's compiled
        // metadata, computed once per compilation change and shared by every serializer -- see
        // ComputeMetadataSchemas and TrackingNames.MetadataSchemas's own doc comment for the
        // resulting run-reason trade-off. Combines the live Compilation (real symbol resolution is
        // unavoidable here, exactly like CompilationFacts) with the collected messages and serializers
        // (only for the referenced type keys they actually name -- ComputeReferencedTypeKeys -- so
        // the resolution walk below only ever visits types someone actually referenced) and, as of
        // Decisions 19 and 21, compilationFacts too: a referenced-assembly protocol/marked-union-base
        // implementor CompilationFacts' own walk found also needs its full schema extracted here, the
        // same way a locally-named nested field's foreign type already does.
        var metadataSchemas = context.CompilationProvider
            .Combine(messages)
            .Combine(serializers)
            .Combine(compilationFacts)
            .Select(static (pair, cancellationToken) => ComputeMetadataSchemas(pair.Left.Left.Left, pair.Left.Left.Right, pair.Left.Right, pair.Right, cancellationToken))
            .WithTrackingName(TrackingNames.MetadataSchemas);

        // The per-serializer resolve stage: each serializer plus ALL collected messages (a message's
        // top-level/reachable status can only be judged against the full set) resolves, via
        // ResolveSerializer, to ONE cached, value-equatable ResolvedSerializer. SelectMany splits the
        // single (serializers, messages, metadataSchemas, compilationFacts) combined value back out
        // into one independently-cached element per serializer -- the driver diffs each element
        // against its previous run by VALUE (ResolvedSerializer.Equals), so a serializer whose
        // resolved model is unaffected by an edit reports Unchanged here even though the whole stage
        // recomputed. See ResolveSerializer's doc comment for why this dependency shape is
        // unavoidable, and the RegisterSourceOutput below for why an Unchanged/Cached element here is
        // what makes that serializer's own emitted file Cached, not just this stage. metadataSchemas
        // and compilationFacts are BOTH combined in directly, as of Decisions 19 and 21: a
        // referenced-assembly nested field, union member, or top-level protocol/marked-union-base
        // implementor needs its schema (metadataSchemas) and its closed-set membership
        // (compilationFacts) visible to ResolveSerializerMessages to resolve at all. See
        // TrackingNames.MetadataSchemas and TrackingNames.CompilationFacts for the run-reason
        // consequence: neither, in practice, costs this stage its Cached best case for an edit that
        // stage itself does not care about.
        var resolvedSerializers = serializers
            .Combine(messages)
            .Combine(metadataSchemas)
            .Combine(compilationFacts)
            .SelectMany(static (pair, cancellationToken) =>
            {
                var (((allSerializers, allMessages), schemas), facts) = pair;
                var duplicateSerializerIds = ComputeDuplicateSerializerIds(allSerializers);
                var duplicateProtocolBindings = ComputeDuplicateProtocolBindings(allSerializers);
                var declaredMessages = ComputeDeclaredMessages(allMessages);
                var genericDefinitions = ComputeGenericDefinitions(declaredMessages);

                var builder = ImmutableArray.CreateBuilder<ResolvedSerializer>();
                foreach (var serializer in allSerializers)
                {
                    if (serializer == null)
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();
                    builder.Add(ResolveSerializer(serializer, declaredMessages, duplicateSerializerIds, duplicateProtocolBindings, genericDefinitions, schemas, facts));
                }

                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.ResolvedSerializers);

        // Code emission consumes ONLY the cached, value-equatable, symbol-free ResolvedSerializer
        // model -- never the Compilation, never a location, and never another serializer's data.
        // Registered on the VALUES provider (one independent output per serializer) rather than a
        // Collect()'d array, so editing a message owned by one serializer re-emits only that
        // serializer's file: the driver skips this callback entirely for any OTHER serializer whose
        // resolved model still compares equal to last run. As of S6 this callback no longer reports
        // any diagnostic (see ReportResolvedSerializerDiagnostics below) -- it only decides, from
        // ResolvedSerializer.IsEmittable and ResolvedSerializer.ValidationDiagnostics' severities,
        // whether to skip AddSource. That split keeps emission's own caching untouched by a
        // location-only edit: this stage never combines the location bag, so it stays Cached exactly
        // when it always did.
        context.RegisterSourceOutput(resolvedSerializers, static (ctx, resolved) => EmitResolvedSerializer(ctx, resolved));

        // S6 "locations": ALL diagnostic reporting for a resolved serializer's own gate/validation
        // diagnostics moved OUT of EmitResolvedSerializer and into this diagnostics-only output, so it
        // can combine the merged location bag without dragging that whitespace-sensitive input into
        // code emission's own cache key. Diagnostic id, text, and trigger conditions are unchanged --
        // only WHERE they are reported from, and now WITH a real Location, has moved.
        context.RegisterSourceOutput(
            resolvedSerializers.Combine(allLocations),
            static (ctx, pair) => ReportResolvedSerializerDiagnostics(ctx, pair.Left, pair.Right));

        // The cross-serializer diagnostics (AKKASG013 duplicate ids, AKKASG031 duplicate protocol
        // bindings, AKKASG037 manifest ignored on a generic definition) cannot be attached to any
        // one serializer's ResolvedSerializer without either duplicating them or picking an
        // arbitrary "owner" -- see ReportCrossSerializerDiagnostics's doc comment. Reported exactly
        // once each, from a small diagnostics-only output over the same two collected arrays, now also
        // combined with the merged location bag so each can report at its chosen local site instead of
        // Location.None. Kept as its OWN output (rather than folded into ReportResolvedSerializerDiagnostics
        // above) because its input shape -- the whole collected arrays, not one resolved serializer at
        // a time -- is genuinely different: folding them together would force this output to
        // re-execute once per serializer instead of once per compilation change.
        context.RegisterSourceOutput(
            serializers.Combine(messages).Combine(allLocations),
            static (ctx, pair) => ReportCrossSerializerDiagnostics(ctx, pair.Left.Left, pair.Left.Right, pair.Right));

        // AKKASG029's whole-compilation protocol-coverage check ("does any source-declared type
        // implement this protocol interface without [AkkaSerializable]?") no longer touches the
        // live Compilation from inside this per-serializer output. As of S5, the walk itself runs
        // exactly once per compilation change, in the CompilationFacts stage above -- for every
        // serializer's protocol at once, not once per serializer -- so this diagnostics-only output
        // now combines the cached ResolvedSerializer (for its gate) with the cached CompilationFacts
        // (for the precomputed implementor list): ValidateProtocolCoverage is a pure function of the
        // two, with no Compilation parameter at all. Diagnostic id, text, and trigger conditions are
        // unchanged -- only where the whole-compilation walk lives, and how many times per edit it
        // runs, has moved. CompilationFacts still recomputes on every edit (it combines
        // context.CompilationProvider directly), but its OUTPUT compares equal whenever nothing it
        // tracks changed, which is what lets this output -- like code emission above -- report
        // Cached instead of Modified for an edit these facts do not care about. As of S6 this ALSO
        // combines the merged location bag: AKKASG029 reports at the serializer's own attribute
        // (LocationKey(serializer.Key, "")) because the unmarked implementor found by the facts stage
        // is not itself an attributed type in this generator's model -- there is no local site on it
        // to point at, and the facts stage must stay whitespace-insensitive to that implementor's own
        // declaration (see ComputeCompilationFacts's doc comment).
        //
        // Design decision: coverage errors no longer gate emission (the old terminal stage skipped
        // AddSource for a serializer whose coverage check failed). This is the standard split for
        // whole-compilation diagnostics, and it is build-outcome-equivalent: AKKASG029 is an Error,
        // so a coverage gap still fails the build and the source emitted alongside it never ships.
        // Emitting anyway gives strictly better IDE behavior (the generated members stay resolvable
        // while the user fixes the gap) and lets the emission stage surface OTHER diagnostics that
        // the old early-return used to hide until the coverage error was fixed.
        context.RegisterSourceOutput(
            resolvedSerializers.Combine(compilationFacts).Combine(allLocations),
            static (ctx, pair) => ReportProtocolCoverage(ctx, pair.Left.Left, pair.Left.Right, pair.Right));

        // Decision 19's four placement diagnostics ("making the compiler complain: the pit of
        // success" in design.md): a misplaced serializer or message becomes a compile-time error or
        // warning instead of a silent gap. A whole-compilation, cross-serializer concern -- like
        // ReportCrossSerializerDiagnostics and ReportProtocolCoverage above -- so it is its own
        // diagnostics-only output over the collected serializers and messages plus the cached
        // CompilationFacts (for the upstream-serializer-binding and referenced-assembly-implementor
        // input every rule needs), combined with the merged location bag so each can report at its
        // own local site (a message's own declaration, or a serializer's own attribute).
        context.RegisterSourceOutput(
            serializers.Combine(messages).Combine(compilationFacts).Combine(allLocations),
            static (ctx, pair) => ReportPlacementDiagnostics(ctx, pair.Left.Left.Left, pair.Left.Left.Right, pair.Left.Right, pair.Right));
    }
}
