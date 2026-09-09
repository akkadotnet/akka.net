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
        public const string ExtractedSerializers = nameof(ExtractedSerializers);
        public const string CollectedSerializers = nameof(CollectedSerializers);
        public const string ExtractedMessages = nameof(ExtractedMessages);
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
        /// The S5 whole-compilation facts stage: <c>context.CompilationProvider.Combine(CollectedSerializers).Select(...)</c>,
        /// producing one cached <see cref="CompilationFacts"/> value per compilation change (see
        /// <see cref="ComputeCompilationFacts"/>). Feeds the AKKASG029 coverage output in place of
        /// the raw <see cref="Compilation"/> that output used to combine with directly -- see
        /// <see cref="ReportProtocolCoverage"/>. Deliberately NOT combined into
        /// <see cref="ResolvedSerializers"/>'s own inputs yet: nothing this stage computes today
        /// (Decision 19's referenced-assembly implementor map is always empty in this change) is
        /// something <see cref="ResolveSerializer"/> needs to consume, so wiring it in now would add
        /// an equality-risk surface for no behavioral change. That wiring is expected once Decision
        /// 19's referenced-assembly implementor walk is real.
        /// </summary>
        public const string CompilationFacts = nameof(CompilationFacts);

        public static ImmutableArray<string> All { get; } = ImmutableArray.Create(
            ExtractedSerializers, CollectedSerializers, ExtractedMessages, CollectedMessages, ResolvedSerializers, CompilationFacts);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var serializers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializerAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, cancellationToken) => ExtractSerializer(ctx, cancellationToken))
            .WithTrackingName(TrackingNames.ExtractedSerializers)
            .Where(static info => info != null)
            .Collect()
            .WithTrackingName(TrackingNames.CollectedSerializers);

        var messages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, cancellationToken) => ExtractMessage(ctx, cancellationToken))
            .WithTrackingName(TrackingNames.ExtractedMessages)
            .Where(static info => info != null)
            .Collect()
            .WithTrackingName(TrackingNames.CollectedMessages);

        // The per-serializer resolve stage: each serializer plus ALL collected messages (a message's
        // top-level/reachable status can only be judged against the full set) resolves, via
        // ResolveSerializer, to ONE cached, value-equatable ResolvedSerializer. SelectMany splits the
        // single (serializers, messages) combined value back out into one independently-cached
        // element per serializer -- the driver diffs each element against its previous run by VALUE
        // (ResolvedSerializer.Equals), so a serializer whose resolved model is unaffected by an
        // edit reports Unchanged here even though the whole stage recomputed. See ResolveSerializer's
        // doc comment for why this dependency shape is unavoidable, and the RegisterSourceOutput
        // below for why an Unchanged/Cached element here is what makes that serializer's own emitted
        // file Cached, not just this stage.
        var resolvedSerializers = serializers
            .Combine(messages)
            .SelectMany(static (pair, cancellationToken) =>
            {
                var (allSerializers, allMessages) = pair;
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
                    builder.Add(ResolveSerializer(serializer, declaredMessages, duplicateSerializerIds, duplicateProtocolBindings, genericDefinitions));
                }

                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.ResolvedSerializers);

        // The S5 whole-compilation facts stage: everything the pipeline needs to know about the
        // WHOLE compilation, computed ONCE per compilation change and shared by every serializer --
        // see ComputeCompilationFacts and CompilationFacts's own doc comment. Combines the live
        // Compilation (this stage genuinely needs it, exactly like the coverage scan it replaces)
        // with the collected serializers (only for their protocol keys: which protocol interfaces
        // anyone actually asked about, so the local-implementor walk below doesn't have to consider
        // every interface of every source-declared type). The OUTPUT is symbol-free and
        // value-equatable, so -- unlike the raw Compilation this used to be combined with directly
        // -- a downstream consumer wired to THIS stage can report Unchanged/Cached whenever nothing
        // these facts care about changed, even though the stage itself reruns on every edit (the
        // CompilationProvider input never itself compares equal across edits).
        var compilationFacts = context.CompilationProvider
            .Combine(serializers)
            .Select(static (pair, cancellationToken) => ComputeCompilationFacts(pair.Left, pair.Right, cancellationToken))
            .WithTrackingName(TrackingNames.CompilationFacts);

        // Code emission consumes ONLY the cached, value-equatable, symbol-free ResolvedSerializer
        // model -- never the Compilation, and never another serializer's data. Registered on the
        // VALUES provider (one independent output per serializer) rather than a Collect()'d array,
        // so editing a message owned by one serializer re-emits only that serializer's file: the
        // driver skips this callback entirely for any OTHER serializer whose resolved model still
        // compares equal to last run.
        context.RegisterSourceOutput(resolvedSerializers, static (ctx, resolved) => EmitResolvedSerializer(ctx, resolved));

        // The cross-serializer diagnostics (AKKASG013 duplicate ids, AKKASG031 duplicate protocol
        // bindings, AKKASG037 manifest ignored on a generic definition) cannot be attached to any
        // one serializer's ResolvedSerializer without either duplicating them or picking an
        // arbitrary "owner" -- see ReportCrossSerializerDiagnostics's doc comment. Reported exactly
        // once each, from a small diagnostics-only output over the same two collected arrays.
        context.RegisterSourceOutput(
            serializers.Combine(messages),
            static (ctx, pair) => ReportCrossSerializerDiagnostics(ctx, pair.Left, pair.Right));

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
        // Cached instead of Modified for an edit these facts do not care about.
        //
        // Design decision: coverage errors no longer gate emission (the old terminal stage skipped
        // AddSource for a serializer whose coverage check failed). This is the standard split for
        // whole-compilation diagnostics, and it is build-outcome-equivalent: AKKASG029 is an Error,
        // so a coverage gap still fails the build and the source emitted alongside it never ships.
        // Emitting anyway gives strictly better IDE behavior (the generated members stay resolvable
        // while the user fixes the gap) and lets the emission stage surface OTHER diagnostics that
        // the old early-return used to hide until the coverage error was fixed.
        context.RegisterSourceOutput(
            resolvedSerializers.Combine(compilationFacts),
            static (ctx, pair) => ReportProtocolCoverage(ctx, pair.Left, pair.Right));
    }
}
