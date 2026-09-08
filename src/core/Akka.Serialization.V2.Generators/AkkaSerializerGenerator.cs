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
//                                             ExtractUnionMembers, field-type mapping, KnownTypes,
//                                             constructor matching.
//   AkkaSerializerGenerator.Validation.cs  - validation over the collected models and diagnostic
//                                             reporting: ValidateMessages, ValidateUnionField,
//                                             ValidateClosedGenericProtocolCoverage,
//                                             CollectReachableMessages, the Report* helpers.
//   AkkaSerializerGenerator.Emission.cs    - source emission: EmitSerializers, Generate*, union
//                                             helper planning, naming/folding, collision handling.
//   AkkaSerializerGenerator.Models.cs      - the model records (SerializerInfo, MessageInfo,
//                                             FieldInfo, TypeMapping, UnionMemberInfo, ...).
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

        public static ImmutableArray<string> All { get; } = ImmutableArray.Create(
            ExtractedSerializers, CollectedSerializers, ExtractedMessages, CollectedMessages);
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

        // Code emission consumes ONLY the collected, value-equatable, symbol-free models -- never
        // the Compilation. An edit anywhere that does not change an extracted model therefore
        // reuses the cached emission output instead of regenerating every serializer per keystroke.
        context.RegisterSourceOutput(
            serializers.Combine(messages),
            static (ctx, pair) => EmitSerializers(ctx, pair.Left, pair.Right));

        // AKKASG029's whole-compilation protocol-coverage scan (ValidateProtocolCoverage) is the
        // one check that genuinely needs the Compilation ("does any source-declared type implement
        // this protocol interface without [AkkaSerializable]?"), so it lives in this SEPARATE,
        // diagnostics-only output: the Compilation input changes on every edit, but only this cheap
        // re-scan pays for that -- code emission above stays cached.
        //
        // Design decision: coverage errors no longer gate emission (the old terminal stage skipped
        // AddSource for a serializer whose coverage check failed). This is the standard split for
        // whole-compilation diagnostics, and it is build-outcome-equivalent: AKKASG029 is an Error,
        // so a coverage gap still fails the build and the source emitted alongside it never ships.
        // Emitting anyway gives strictly better IDE behavior (the generated members stay resolvable
        // while the user fixes the gap) and lets the emission stage surface OTHER diagnostics that
        // the old early-return used to hide until the coverage error was fixed.
        context.RegisterSourceOutput(
            serializers.Combine(messages).Combine(context.CompilationProvider),
            static (ctx, tuple) => ReportProtocolCoverage(ctx, tuple.Left.Left, tuple.Left.Right, tuple.Right));
    }
}
