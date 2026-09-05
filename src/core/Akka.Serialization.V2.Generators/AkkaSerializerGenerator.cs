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

[Generator]
public sealed class AkkaSerializerGenerator : IIncrementalGenerator
{
    private const string SerializerAttributeFullName = "Akka.Serialization.V2.AkkaSerializerAttribute`1";
    private const string SerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializableAttribute";
    private const string FieldAttributeFullName = "Akka.Serialization.V2.AkkaFieldAttribute";
    private const string EnvelopePayloadAttributeFullName = "Akka.Serialization.V2.AkkaEnvelopePayloadAttribute";
    private const string UnionAttributeFullName = "Akka.Serialization.V2.AkkaUnionAttribute";
    private const string GenericSerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializableAttribute`1";
    private const string FormatterAttributeFullName = "Akka.Serialization.V2.AkkaSerializerFormatterAttribute`2";
    private const string ExtendedActorSystemFullName = "Akka.Actor.ExtendedActorSystem";
    private const string AkkaSerializerBaseTypeFullName = "Akka.Serialization.V2.AkkaSerializer";

    // AkkaSerializerAttribute<TProtocol>(string name, int serializerId) requires both arguments at
    // every call site -- there is no longer a way to OMIT Name or SerializerId, so AKKASG001/002
    // no longer guard "missing" registration. They still guard the argument VALUES: a caller can
    // still write [AkkaSerializer<T>(null!, 0)] or an empty/whitespace name or a non-positive id,
    // and those remain compile-time errors from this generator.
    private static readonly DiagnosticDescriptor InvalidSerializerName = new(
        "AKKASG001",
        "Serializer name must be a non-empty string",
        "[AkkaSerializer] class '{0}' specifies an invalid Name: it must not be null, empty, or consist only of whitespace",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSerializerId = new(
        "AKKASG002",
        "Serializer id must be a positive integer",
        "[AkkaSerializer] class '{0}' specifies SerializerId {1}, which must be a positive, non-zero integer unique within the actor system",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedFieldType = new(
        "AKKASG003",
        "Unsupported field type",
        "Property '{0}' on type '{1}' has unsupported generated serializer field type '{2}'",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same id/title/severity as UnsupportedFieldType. Used only for an interface, abstract class,
    // or type parameter field, which is usually a forgotten [AkkaEnvelopePayload]/[AkkaUnion].
    private static readonly DiagnosticDescriptor UnsupportedFieldTypePolymorphic = new(
        "AKKASG003",
        "Unsupported field type",
        "Property '{0}' on type '{1}' has unsupported generated serializer field type '{2}'. Mark the property [AkkaEnvelopePayload], or declare a closed member set with [AkkaUnion].",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingFields = new(
        "AKKASG004",
        "No serializable fields",
        "[AkkaSerializable] type '{0}' must declare at least one [AkkaField] property, or set AllowEmpty = true if the message is deliberately fieldless",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateFieldIndex = new(
        "AKKASG005",
        "Duplicate field index",
        "[AkkaSerializable] type '{0}' has duplicate [AkkaField] index {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingManifest = new(
        "AKKASG006",
        "Top-level message manifest is required",
        "[AkkaSerializable] top-level protocol message '{0}' must specify Manifest for serializer dispatch",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingNestedSerializableDefinition = new(
        "AKKASG007",
        "Nested value object serialization definition is required",
        "Property '{0}' on type '{1}' uses nested value object type '{2}', which must be annotated with [AkkaSerializable] and explicit [AkkaField] fields",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same id/title/severity as MissingNestedSerializableDefinition. Used only when the nested
    // type's assembly is not the one being compiled. This generator can only read a schema from
    // the current compilation, so the type may already carry both attributes in its own assembly
    // and still be unreadable from here. The message must name only the fixes that work today, and
    // must not claim the type lacks the attributes.
    private static readonly DiagnosticDescriptor MissingNestedSerializableDefinitionCrossAssembly = new(
        "AKKASG007",
        "Nested value object serialization definition is required",
        "Property '{0}' on type '{1}' uses nested value object type '{2}', which is declared in assembly '{3}'. " +
        "This generator cannot read a schema from a referenced assembly yet. " +
        "Register [AkkaSerializerFormatter<{2}, TFormatter>] on '{4}', or declare the type in this assembly.",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // The `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint on
    // AkkaSerializerFormatterAttribute<TTarget, TFormatter> now makes interface conformance a
    // compile-time error at the attribute usage site, so this narrows to the one thing a generic
    // constraint cannot express: TFormatter must not be abstract (an abstract type still satisfies
    // the constraint, and there is deliberately no `new()` clause to rule it out, since a formatter
    // with only an ExtendedActorSystem constructor is legitimate).
    private static readonly DiagnosticDescriptor InvalidFormatterType = new(
        "AKKASG008",
        "Formatter type must not be abstract",
        "Formatter '{0}' on serializer '{1}' must not be abstract: it cannot be instantiated as the runtime IAkkaMessagePackFormatter<{2}>",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateFormatterRegistration = new(
        "AKKASG009",
        "Duplicate formatter registration",
        "Serializer '{0}' registers multiple formatters for type '{1}'",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FormatterConstructorNotUsable = new(
        "AKKASG010",
        "Formatter constructor not usable",
        "Formatter '{0}' on serializer '{1}' must have a public parameterless constructor or a public constructor taking ExtendedActorSystem",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FormatterTargetNotSupported = new(
        "AKKASG011",
        "Formatter target type is not supported",
        "Formatter target type '{0}' on serializer '{1}' must be a non-generic, non-array named type",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateManifest = new(
        "AKKASG012",
        "Duplicate top-level message manifest",
        "Serializer '{0}' has multiple top-level [AkkaSerializable] messages with manifest '{1}': {2}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateSerializerId = new(
        "AKKASG013",
        "Duplicate serializer id",
        "SerializerId {0} is used by multiple [AkkaSerializer] classes: {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedEnumUnderlyingType = new(
        "AKKASG014",
        "Enum underlying type is not supported",
        "Property '{0}' on type '{1}' uses enum type '{2}' whose underlying type '{3}' is not fully int32-representable; " +
        "generated serializers encode enums as int32, so use an enum backed by sbyte, byte, short, ushort, or int",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberNotSerializable = new(
        "AKKASG015",
        "Union member type is not serializable",
        "Union member '{0}' on property '{1}' of type '{2}' must be an [AkkaSerializable] class or struct handled by this serializer",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same id/title/severity as UnionMemberNotSerializable. Used only when the member type's
    // assembly is not the one being compiled. See MissingNestedSerializableDefinitionCrossAssembly
    // for why the member may already carry [AkkaSerializable] and still be unreadable from here.
    private static readonly DiagnosticDescriptor UnionMemberNotSerializableCrossAssembly = new(
        "AKKASG015",
        "Union member type is not serializable",
        "Union member '{0}' on property '{1}' of type '{2}' is declared in assembly '{3}'. " +
        "This generator cannot read a schema from a referenced assembly yet. " +
        "Register [AkkaSerializerFormatter<{0}, TFormatter>] on '{4}', or declare the member in this assembly.",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberMissingManifest = new(
        "AKKASG016",
        "Union member manifest is required",
        "Union member '{0}' on property '{1}' of type '{2}' must specify Manifest in its [AkkaSerializable] attribute: the manifest is the union discriminator",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberManifestCollision = new(
        "AKKASG017",
        "Union member manifests must be unique",
        "Union on property '{0}' of type '{1}' has multiple members with manifest '{2}': {3}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberNotAssignable = new(
        "AKKASG018",
        "Union member is not assignable to the field type",
        "Union member '{0}' on property '{1}' of type '{2}' is not implicitly convertible to the field type '{3}'",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidUnionMemberSet = new(
        "AKKASG019",
        "Union member set is invalid",
        "Union on property '{0}' of type '{1}' has an invalid member set: {2}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidClosedGenericRegistration = new(
        "AKKASG020",
        "Closed generic registration is invalid",
        "[AkkaSerializable<T>] registration '{0}' on serializer '{1}' must be a closed generic construction of a generic type annotated with [AkkaSerializable]",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateClosedGenericRegistration = new(
        "AKKASG021",
        "Duplicate closed generic registration",
        "Serializer '{0}' registers the closed construction '{1}' more than once",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericSerializableRequiresRegistration = new(
        "AKKASG022",
        "Generic serializable type requires closed generic registrations",
        "Generic [AkkaSerializable] type '{0}' implements protocol '{1}' of serializer '{2}' but has no [AkkaSerializable<T>] registrations; a source generator cannot serialize an open generic, so register each closed construction with [AkkaSerializable<T>(Manifest = ...)] on the serializer class",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnregisteredClosedGenericField = new(
        "AKKASG023",
        "Closed generic field type is not registered",
        "Property '{0}' on type '{1}' uses closed generic [AkkaSerializable] type '{2}', which must be registered on serializer '{3}' with [AkkaSerializable<T>]",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberNotSealed = new(
        "AKKASG025",
        "Union member type is not sealed",
        "Union member '{0}' on property '{1}' of type '{2}' is not sealed; union write dispatch matches the exact runtime type, so an undeclared subtype of '{0}' fails serialization -- consider sealing it",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateGeneratedName = new(
        "AKKASG024",
        "Generated member name collision",
        "Serializer '{0}' produces the same generated member name '{1}' for distinct message types {2}; rename one of the types to disambiguate",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoMatchingConstructor = new(
        "AKKASG026",
        "No matching constructor",
        "[AkkaSerializable] type '{0}' cannot be reconstructed on deserialize: {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConstructorParameterNotCovered = new(
        "AKKASG027",
        "Constructor parameter not covered by [AkkaField]",
        "Constructor parameter '{0}' of [AkkaSerializable] type '{1}' has a default value and is not covered by any [AkkaField] property; it silently resets to its default value on every deserialize",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FieldPropertyNotAccessible = new(
        "AKKASG028",
        "[AkkaField] must be on an accessible instance property",
        "[AkkaField] property '{0}' on type '{1}' {2}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ProtocolMessageNotSerializable = new(
        "AKKASG029",
        "Protocol message type is not [AkkaSerializable]",
        "Type '{0}' implements protocol '{1}' of serializer '{2}' but is not [AkkaSerializable]; it is invisible to the generated Manifest/Serialize/Deserialize switches and fails only at runtime, when it is first sent",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateProtocolBinding = new(
        "AKKASG031",
        "Protocol interface bound by multiple serializers",
        "Protocol '{0}' is bound by multiple [AkkaSerializer] classes: {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSerializerShape = new(
        "AKKASG032",
        "Serializer class shape is invalid",
        "[AkkaSerializer] class '{0}' {1}",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ProtocolTypeMustBeInterface = new(
        "AKKASG033",
        "Protocol type must be an interface",
        "[AkkaSerializer<{1}>] class '{0}' specifies a protocol type that is not an interface; dispatch matches messages via AllInterfaces, so a non-interface protocol type silently generates a serializer with empty Manifest/Serialize/Deserialize switches",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ClosedGenericRegistrationNotInProtocol = new(
        "AKKASG034",
        "Registered closed generic type does not implement the serializer protocol",
        "Closed generic construction '{0}' registered on serializer '{1}' does not implement protocol '{2}' and is not referenced by any [AkkaField] property of a message reachable from it; the registration has no effect",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionDeclarationIgnoredOnEnvelopePayload = new(
        "AKKASG035",
        "Union declaration is ignored on an envelope payload field",
        "Property '{0}' on type '{1}' has both [AkkaEnvelopePayload] and [AkkaUnion]; the union declaration is ignored -- envelope payload takes precedence",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnionMemberAbstract = new(
        "AKKASG036",
        "Union member type is abstract",
        "Union member '{0}' on property '{1}' of type '{2}' is abstract; union write dispatch matches the exact runtime type, and an abstract type is never a runtime type, so this member's dispatch branch is dead code -- declare its concrete subtypes as union members instead",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ManifestIgnoredOnGenericDefinition = new(
        "AKKASG037",
        "Manifest on a generic [AkkaSerializable] definition is ignored",
        "Generic [AkkaSerializable] type '{0}' specifies Manifest '{1}', which is ignored: a generic definition is never serialized directly, and each closed construction registered with [AkkaSerializable<T>] supplies its own Manifest",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

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

    private static void EmitSerializers(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        ImmutableArray<MessageInfo?> messages)
    {
        var duplicateSerializerIds = ComputeDuplicateSerializerIds(serializers);

        foreach (var duplicate in duplicateSerializerIds)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateSerializerId, Location.None, duplicate.Key, duplicate.Value));
        }

        // Same computation as duplicateSerializerIds above, grouped on the protocol interface
        // instead of the numeric id: two [AkkaSerializer] classes bound to the same protocol is
        // silent last-wins at runtime registration today (AKKASG031).
        var duplicateProtocolBindings = ComputeDuplicateProtocolBindings(serializers);

        foreach (var duplicate in duplicateProtocolBindings)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateProtocolBinding, Location.None, ToDisplayName(duplicate.Key), duplicate.Value));
        }

        var declaredMessages = messages
            .Where(message => message != null)
            .Cast<MessageInfo>()
            .ToImmutableArray();

        // Advisory only (AKKASG037): a Manifest on a generic [AkkaSerializable] DEFINITION is
        // silently ignored -- the definition is never serialized directly (see ExtractMessage),
        // and every registered closed construction carries its own per-construction Manifest from
        // [AkkaSerializable<T>]. Reported once per definition, independent of any serializer.
        foreach (var definition in declaredMessages.Where(message => message.IsGenericDefinition && !string.IsNullOrWhiteSpace(message.Manifest)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ManifestIgnoredOnGenericDefinition, Location.None, ToDisplayName(definition.FullyQualifiedName), definition.Manifest));
        }

        // Generic definitions are placeholders: never serialized, never top-level, never in
        // the message dictionary (their arity-less key could even collide with a same-named
        // non-generic type). They exist only for the AKKASG022/AKKASG037 checks.
        var genericDefinitions = declaredMessages
            .Where(message => message.IsGenericDefinition)
            .ToImmutableArray();

        foreach (var serializer in serializers)
        {
            if (serializer == null)
                continue;

            if (string.IsNullOrWhiteSpace(serializer.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidSerializerName, Location.None, serializer.ClassName));
                continue;
            }

            if (serializer.SerializerId <= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidSerializerId, Location.None, serializer.ClassName, serializer.SerializerId));
                continue;
            }

            if (duplicateSerializerIds.ContainsKey(serializer.SerializerId))
                continue;

            if (duplicateProtocolBindings.ContainsKey(serializer.ProtocolTypeFullName))
                continue;

            if (!ValidateSerializerShape(serializer, context.ReportDiagnostic))
                continue;

            if (!ValidateProtocolType(serializer, context.ReportDiagnostic))
                continue;

            if (!ValidateFormatters(serializer, context.ReportDiagnostic))
                continue;

            if (!ValidateClosedGenericRegistrations(serializer, context.ReportDiagnostic))
                continue;

            if (!ValidateGenericDefinitions(serializer, genericDefinitions, context.ReportDiagnostic))
                continue;

            var allMessages = declaredMessages
                .Where(message => !message.IsGenericDefinition)
                .Concat(serializer.ClosedGenericRegistrations
                    .Where(registration => registration.Message != null)
                    .Select(registration => registration.Message!))
                .ToImmutableArray();
            var allMessagesByType = allMessages.ToImmutableDictionary(message => message.FullyQualifiedName);
            var resolvedMessagesByType = ResolveMessages(allMessagesByType, serializer.Formatters);
            var topLevelMessages = allMessages
                .Where(message => serializer.ProtocolTypeFullName.Length > 0 && message.Protocols.Contains(serializer.ProtocolTypeFullName))
                .Select(message => resolvedMessagesByType[message.FullyQualifiedName])
                .ToImmutableArray();
            var reachableMessages = CollectReachableMessages(topLevelMessages, resolvedMessagesByType);

            if (!ValidateMessages(context, serializer, topLevelMessages, reachableMessages, resolvedMessagesByType))
                continue;

            if (!ValidateClosedGenericProtocolCoverage(context, serializer, reachableMessages))
                continue;

            context.AddSource(serializer.ClassName + ".AkkaSerialization.g.cs", Generate(serializer, topLevelMessages, reachableMessages, resolvedMessagesByType));
        }
    }

    /// <summary>
    /// Diagnostics-only output for AKKASG029 (see the comment in <see cref="Initialize"/>). To
    /// preserve the old terminal stage's semantics, a serializer only reaches the coverage scan
    /// after it passes every check that used to precede <see cref="ValidateProtocolCoverage"/> in
    /// the single-output pipeline -- those checks run here SILENTLY (null reporter): the emission
    /// output above is the one that reports them, and reporting them twice would duplicate every
    /// pre-coverage diagnostic.
    /// </summary>
    private static void ReportProtocolCoverage(
        SourceProductionContext context,
        ImmutableArray<SerializerInfo?> serializers,
        ImmutableArray<MessageInfo?> messages,
        Compilation compilation)
    {
        var duplicateSerializerIds = ComputeDuplicateSerializerIds(serializers);
        var duplicateProtocolBindings = ComputeDuplicateProtocolBindings(serializers);
        var genericDefinitions = messages
            .Where(message => message != null)
            .Cast<MessageInfo>()
            .Where(message => message.IsGenericDefinition)
            .ToImmutableArray();

        foreach (var serializer in serializers)
        {
            if (serializer == null)
                continue;

            if (string.IsNullOrWhiteSpace(serializer.Name) || serializer.SerializerId <= 0)
                continue;

            if (duplicateSerializerIds.ContainsKey(serializer.SerializerId))
                continue;

            if (duplicateProtocolBindings.ContainsKey(serializer.ProtocolTypeFullName))
                continue;

            if (!ValidateSerializerShape(serializer, report: null))
                continue;

            if (!ValidateProtocolType(serializer, report: null))
                continue;

            if (!ValidateFormatters(serializer, report: null))
                continue;

            if (!ValidateClosedGenericRegistrations(serializer, report: null))
                continue;

            if (!ValidateGenericDefinitions(serializer, genericDefinitions, report: null))
                continue;

            ValidateProtocolCoverage(context, serializer, compilation);
        }
    }

    private static ImmutableDictionary<int, string> ComputeDuplicateSerializerIds(ImmutableArray<SerializerInfo?> serializers)
    {
        return serializers
            .Where(s => s != null)
            .Cast<SerializerInfo>()
            .Where(s => s.SerializerId > 0)
            .GroupBy(s => s.SerializerId)
            .Where(group => group.Count() > 1)
            .ToImmutableDictionary(group => group.Key, group => string.Join(", ", group.Select(s => s.ClassName)));
    }

    private static ImmutableDictionary<string, string> ComputeDuplicateProtocolBindings(ImmutableArray<SerializerInfo?> serializers)
    {
        return serializers
            .Where(s => s != null)
            .Cast<SerializerInfo>()
            .Where(s => !string.IsNullOrEmpty(s.ProtocolTypeFullName))
            .GroupBy(s => s.ProtocolTypeFullName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToImmutableDictionary(group => group.Key, group => string.Join(", ", group.Select(s => s.ClassName)), StringComparer.Ordinal);
    }

    private static SerializerInfo? ExtractSerializer(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        // Reading only the first attribute is safe: AkkaSerializerAttribute<TProtocol> declares
        // AllowMultiple = false, and empirically (see AkkaSerializerGeneratorDiagnosticsSpec)
        // the C# compiler enforces that against the OPEN generic attribute definition, not each
        // closed construction -- [AkkaSerializer<IA>][AkkaSerializer<IB>] on the same class is
        // rejected with CS0579 ("Duplicate 'AkkaSerializer<>' attribute") even though IA and IB
        // differ, so at most one [AkkaSerializer<T>] ever reaches this method. No AKKASG030 is
        // needed for this case.
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        string? name = null;
        var serializerId = 0;

        // AkkaSerializerAttribute<TProtocol>(string name, int serializerId): both arguments are
        // mandatory POSITIONAL constructor arguments now -- the attribute has no settable
        // properties left, so `[AkkaSerializer<T>(Name = "x", SerializerId = 1)]` named-property
        // syntax cannot compile and NamedArguments can never carry either value. A length other
        // than 2 cannot occur for a successfully-compiled use of this attribute.
        if (attribute.ConstructorArguments.Length == 2)
        {
            name = attribute.ConstructorArguments[0].Value as string;
            if (attribute.ConstructorArguments[1].Value is int id)
                serializerId = id;
        }

        // The protocol type symbol is consumed HERE and only here: everything the pipeline needs
        // downstream is its fully-qualified name (dispatch/grouping keys) and whether it is an
        // interface (AKKASG033). Retaining the INamedTypeSymbol in the cached model would defeat
        // incremental caching outright -- symbols never compare equal across compilations.
        var protocolType = attribute.AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        var protocolTypeFullName = protocolType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
        var protocolTypeIsInterface = protocolType?.TypeKind == TypeKind.Interface;

        var formatterAttributeType = compilation.GetTypeByMetadataName(FormatterAttributeFullName);
        var extendedActorSystemType = compilation.GetTypeByMetadataName(ExtendedActorSystemFullName);
        var formatters = ExtractFormatters(symbol, formatterAttributeType, extendedActorSystemType);
        var closedGenericRegistrations = ExtractClosedGenericRegistrations(symbol, compilation);

        return new SerializerInfo(
            GetNamespace(symbol),
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            name ?? string.Empty,
            serializerId,
            protocolTypeFullName,
            protocolTypeIsInterface,
            symbol.DeclaredAccessibility,
            formatters,
            closedGenericRegistrations,
            IsPartial(symbol),
            symbol.IsGenericType,
            DerivesFromAkkaSerializerBase(symbol, compilation));
    }

    /// <summary>
    /// Whether EVERY syntax declaration of <paramref name="symbol"/> carries the 'partial'
    /// modifier -- a class with a single non-partial declaration, or with any one part missing
    /// 'partial', is already a compile error (CS0260) once the generator emits its own partial
    /// declaration of the same class; AKKASG032 replaces that cryptic error with a direct one.
    /// A declaring reference that is not a <see cref="ClassDeclarationSyntax"/> cannot occur here
    /// in practice (the extraction pipeline only targets <see cref="ClassDeclarationSyntax"/>
    /// nodes, and every partial part of one class shares the same declaration kind) and is
    /// conservatively treated as satisfying the check rather than asserted against.
    /// </summary>
    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is ClassDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> derives (directly or transitively) from
    /// <see cref="AkkaSerializerBaseTypeFullName"/> -- the generated overrides (<c>Identifier</c>,
    /// <c>Manifest</c>, <c>Serialize</c>, <c>Deserialize</c>, <c>SizeHint</c>) require it as a base,
    /// or they fail to compile as overrides (CS0115) against whatever base the class actually has.
    /// </summary>
    private static bool DerivesFromAkkaSerializerBase(INamedTypeSymbol symbol, Compilation compilation)
    {
        var akkaSerializerBaseType = compilation.GetTypeByMetadataName(AkkaSerializerBaseTypeFullName);
        if (akkaSerializerBaseType == null)
            return false;

        for (var baseType = symbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, akkaSerializerBaseType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts <c>[AkkaSerializable&lt;T&gt;]</c> registrations from the serializer class. A
    /// valid target is a CLOSED generic construction (no unbound generics, no type parameters
    /// anywhere in its arguments) whose definition is annotated <c>[AkkaSerializable]</c>; its
    /// <see cref="MessageInfo"/> is built from the constructed symbol, so all field types arrive
    /// already substituted. Invalid targets are recorded with a null message so AKKASG020 fires.
    /// </summary>
    private static ImmutableArray<ClosedGenericRegistrationInfo> ExtractClosedGenericRegistrations(INamedTypeSymbol symbol, Compilation compilation)
    {
        var genericSerializableAttribute = compilation.GetTypeByMetadataName(GenericSerializableAttributeFullName);
        if (genericSerializableAttribute == null)
            return ImmutableArray<ClosedGenericRegistrationInfo>.Empty;

        var attributes = symbol.GetAttributes()
            .Where(attr => attr.AttributeClass is { IsGenericType: true } ac && SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, genericSerializableAttribute))
            .ToImmutableArray();
        if (attributes.IsEmpty)
            return ImmutableArray<ClosedGenericRegistrationInfo>.Empty;

        var knownTypes = KnownTypes.From(compilation);
        var builder = ImmutableArray.CreateBuilder<ClosedGenericRegistrationInfo>(attributes.Length);
        foreach (var attribute in attributes)
        {
            var manifest = string.Empty;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Manifest" && argument.Value.Value is string value)
                    manifest = value;
            }

            var target = attribute.AttributeClass!.TypeArguments[0] as INamedTypeSymbol;
            var serializableAttribute = target?.OriginalDefinition.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
            var isValidTarget = target is { IsGenericType: true, IsUnboundGenericType: false }
                && IsFullyClosed(target)
                && serializableAttribute != null;

            if (!isValidTarget)
            {
                var displayName = attribute.AttributeClass!.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Add(new ClosedGenericRegistrationInfo(displayName, message: null));
                continue;
            }

            // AllowEmpty travels with the definition's [AkkaSerializable]; the manifest is
            // per-construction (each closed form needs its own identity) and comes from the
            // registration attribute.
            var allowEmpty = serializableAttribute!.NamedArguments
                .Any(argument => argument.Key == "AllowEmpty" && argument.Value.Value is true);
            var message = ExtractMessageCore(
                target!,
                GetMessageDictionaryKey(target!),
                manifest,
                allowEmpty,
                knownTypes,
                compilation,
                definitionFullName: GetFullyQualifiedTypeName(target!.OriginalDefinition));
            builder.Add(new ClosedGenericRegistrationInfo(message.FullyQualifiedName, message));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Whether every type argument of a construction (recursively) is a concrete type -- i.e. no
    /// type parameter appears anywhere. <c>Wrapper&lt;Foo&gt;</c> and
    /// <c>Wrapper&lt;Pair&lt;Foo, Bar&gt;&gt;</c> qualify; <c>Wrapper&lt;T&gt;</c> inside another
    /// generic declaration does not.
    /// </summary>
    private static bool IsFullyClosed(INamedTypeSymbol type)
    {
        foreach (var argument in type.TypeArguments)
        {
            switch (argument)
            {
                case ITypeParameterSymbol:
                    return false;
                case INamedTypeSymbol { IsGenericType: true } nested when !IsFullyClosed(nested):
                    return false;
            }
        }

        return true;
    }

    private static ImmutableArray<FormatterInfo> ExtractFormatters(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? formatterAttributeType,
        INamedTypeSymbol? extendedActorSystemType)
    {
        if (formatterAttributeType == null)
            return ImmutableArray<FormatterInfo>.Empty;

        // AkkaSerializerFormatterAttribute<TTarget, TFormatter> where TFormatter :
        // IAkkaMessagePackFormatter<TTarget> -- a constructed generic attribute, so matching
        // requires comparing against OriginalDefinition (the same pattern used for
        // AkkaSerializableAttribute<TMessage> in ExtractClosedGenericRegistrations).
        var formatterAttributes = symbol.GetAttributes()
            .Where(attr => attr.AttributeClass is { IsGenericType: true } ac && SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, formatterAttributeType))
            .ToImmutableArray();

        if (formatterAttributes.IsEmpty)
            return ImmutableArray<FormatterInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<FormatterInfo>(formatterAttributes.Length);
        foreach (var attribute in formatterAttributes)
        {
            // TTarget and TFormatter come from the constructed attribute's own type arguments, not
            // ConstructorArguments -- there is no constructor argument carrying either type
            // anymore. Both slots are always present for a successfully-compiled two-arity
            // construction: neither a null target nor an unbound generic target/formatter can be
            // written here (the compiler rejects both at the attribute usage site), so unlike the
            // former Type-typed constructor arguments, these can never be "missing" or "not a
            // type" -- AKKASG011's former null-target check is unreachable and has been removed.
            var targetTypeSymbol = attribute.AttributeClass!.TypeArguments[0];
            var formatterTypeSymbol = attribute.AttributeClass!.TypeArguments[1];

            // Formatter targets must still be plain named types: arrays are not INamedTypeSymbol,
            // and CLOSED generic targets (e.g. List<int>) -- now directly expressible as TTarget --
            // would still collide on the arity-less fully-qualified name used for field matching.
            // Both remain recorded with IsTargetSupported = false so AKKASG011 fires.
            var targetNamedType = targetTypeSymbol as INamedTypeSymbol;
            var isTargetSupported = targetNamedType is { IsGenericType: false };
            var targetTypeFullName = isTargetSupported
                ? GetFullyQualifiedTypeName(targetNamedType!)
                : targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var formatterNamedType = formatterTypeSymbol as INamedTypeSymbol;
            var formatterTypeFullName = formatterNamedType != null
                ? GetFullyQualifiedTypeName(formatterNamedType)
                : formatterTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // The `where TFormatter : IAkkaMessagePackFormatter<TTarget>` constraint is enforced by
            // the compiler at the attribute usage site, so AKKASG008's former interface-conformance
            // check can never fire here and has been removed. A generic constraint cannot express
            // "and not abstract", though: an abstract TFormatter still satisfies the constraint
            // (it has no `new()` clause to rule that out either -- formatters with an
            // ExtendedActorSystem-only constructor are legitimate), so AKKASG008 now guards
            // abstractness alone.
            var isAbstract = formatterNamedType?.IsAbstract ?? false;

            var ctorKind = formatterNamedType != null
                ? GetFormatterCtorKind(formatterNamedType, extendedActorSystemType)
                : FormatterCtorKind.None;

            builder.Add(new FormatterInfo(
                targetTypeFullName,
                targetTypeSymbol.IsValueType,
                formatterTypeFullName,
                isAbstract,
                ctorKind,
                isTargetSupported));
        }

        return builder.ToImmutable();
    }

    private static FormatterCtorKind GetFormatterCtorKind(INamedTypeSymbol formatterType, INamedTypeSymbol? extendedActorSystemType)
    {
        var hasParameterlessCtor = false;
        var hasSystemCtor = false;
        foreach (var ctor in formatterType.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (ctor.Parameters.Length == 0)
                hasParameterlessCtor = true;
            else if (ctor.Parameters.Length == 1 && extendedActorSystemType != null &&
                     SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, extendedActorSystemType))
                hasSystemCtor = true;
        }

        // Prefer the ExtendedActorSystem constructor when both are present: the generated
        // serializer always has the system in hand, and system context (transport addresses,
        // provider state) is why a formatter declares that constructor in the first place.
        if (hasSystemCtor)
            return FormatterCtorKind.System;

        return hasParameterlessCtor ? FormatterCtorKind.Parameterless : FormatterCtorKind.None;
    }

    /// <summary>
    /// Fires AKKASG032 for each way the [AkkaSerializer] class declaration itself is unusable as
    /// a codegen target: not partial, not derived from the AkkaSerializer base class, or generic.
    /// Today each of these produces a wall of raw CS errors (CS0260/CS0759/CS0115/CS0264) pointing
    /// at the GENERATED file instead of the user's declaration; this replaces that with one direct
    /// diagnostic per violated rule, still on the user's class.
    /// A null <paramref name="report"/> evaluates the check silently -- the coverage output uses
    /// that to replicate the emission stage's gating without duplicating its diagnostics.
    /// </summary>
    private static bool ValidateSerializerShape(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        var isValid = true;

        if (!serializer.IsPartial)
        {
            report?.Invoke(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "must be declared 'partial': the generator emits a second declaration of this class"));
            isValid = false;
        }

        if (!serializer.DerivesFromAkkaSerializerBase)
        {
            report?.Invoke(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "must derive from Akka.Serialization.V2.AkkaSerializer: the generated members (Identifier, Manifest, Serialize, Deserialize, SizeHint) are declared as overrides of that base"));
            isValid = false;
        }

        if (serializer.IsGeneric)
        {
            report?.Invoke(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "cannot be a generic type: the generator emits one concrete, closed partial class per [AkkaSerializer] declaration"));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Fires AKKASG033 when <c>TProtocol</c> in <c>[AkkaSerializer&lt;TProtocol&gt;]</c> is not an
    /// interface. Top-level dispatch matches a message via <c>message.Protocols</c>, which is
    /// populated from <c>INamedTypeSymbol.AllInterfaces</c> (see <see cref="ExtractMessageCore"/>)
    /// -- a class or struct can never appear there, so a non-interface protocol type silently
    /// produces a serializer whose Manifest/Serialize/Deserialize switches have no cases at all.
    /// An empty <see cref="SerializerInfo.ProtocolTypeFullName"/> means the attribute's type
    /// argument was not a named type at all (the extraction stored no name for it) and is exempt,
    /// exactly as the former null-symbol check was.
    /// </summary>
    private static bool ValidateProtocolType(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        if (serializer.ProtocolTypeFullName.Length == 0 || serializer.ProtocolTypeIsInterface)
            return true;

        report?.Invoke(Diagnostic.Create(ProtocolTypeMustBeInterface, Location.None, serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
        return false;
    }

    /// <summary>
    /// Fires AKKASG029 when a named type declared IN THIS COMPILATION implements the serializer's
    /// protocol interface but is not [AkkaSerializable]. Exempt: interfaces and abstract classes
    /// (never concrete runtime message types -- their concrete subtypes are checked individually),
    /// and [AkkaSerializable]-marked open generic definitions (governed entirely by AKKASG022's
    /// registration machinery). An unmarked OPEN GENERIC definition that implements the protocol is
    /// still an error here: with no [AkkaSerializable] on the definition, none of its closed
    /// constructions could ever be registered with [AkkaSerializable&lt;T&gt;] in the first place.
    /// A type this flags is invisible to the generated Manifest/Serialize/Deserialize switches
    /// today and only fails at runtime, the first time it is sent.
    /// The protocol interface is matched by fully-qualified name against each candidate's
    /// <see cref="ITypeSymbol.AllInterfaces"/>: the cached <see cref="SerializerInfo"/> is
    /// deliberately symbol-free, and within one compilation a fully-qualified name identifies
    /// exactly one type, so the string comparison is equivalent to the former
    /// <see cref="SymbolEqualityComparer.Default"/> lookup.
    /// </summary>
    private static void ValidateProtocolCoverage(SourceProductionContext context, SerializerInfo serializer, Compilation compilation)
    {
        if (serializer.ProtocolTypeFullName.Length == 0)
            return;

        var knownTypes = KnownTypes.From(compilation);
        if (knownTypes.SerializableAttribute == null)
            return;

        foreach (var candidate in GetSourceDeclaredTypes(compilation))
        {
            // This whole-compilation walk re-runs on every edit (its output combines the
            // CompilationProvider by necessity); honor IDE cancellation between candidates.
            context.CancellationToken.ThrowIfCancellationRequested();

            if (candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                continue;

            if (candidate.IsAbstract)
                continue;

            if (!ImplementsProtocol(candidate, serializer.ProtocolTypeFullName))
                continue;

            var isMarked = candidate.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
            if (isMarked)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(ProtocolMessageNotSerializable, Location.None,
                ToDisplayName(GetFullyQualifiedTypeName(candidate)), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
        }
    }

    private static bool ImplementsProtocol(INamedTypeSymbol candidate, string protocolTypeFullName)
    {
        foreach (var implemented in candidate.AllInterfaces)
        {
            if (string.Equals(implemented.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), protocolTypeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every named type declared in <paramref name="compilation"/>'s OWN source (never a referenced
    /// assembly: <see cref="Compilation.Assembly"/> is the assembly being compiled), recursively
    /// including nested types. Used only by <see cref="ValidateProtocolCoverage"/>, transiently,
    /// inside the diagnostics-only coverage callback -- never stored in a cached provider.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GetSourceDeclaredTypes(Compilation compilation)
    {
        return GetSourceDeclaredTypes(compilation.Assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceDeclaredTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var nested in GetSourceDeclaredTypesIncludingSelf(type))
                yield return nested;
        }

        foreach (var nestedNamespace in ns.GetNamespaceMembers())
        {
            foreach (var type in GetSourceDeclaredTypes(nestedNamespace))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceDeclaredTypesIncludingSelf(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var descendant in GetSourceDeclaredTypesIncludingSelf(nested))
                yield return descendant;
        }
    }

    private static bool ValidateFormatters(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        if (serializer.Formatters.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var formatter in serializer.Formatters)
        {
            if (!formatter.IsTargetSupported)
            {
                report?.Invoke(Diagnostic.Create(FormatterTargetNotSupported, Location.None, ToDisplayName(formatter.TargetTypeFullName), serializer.ClassName));
                isValid = false;
                continue;
            }

            if (formatter.IsAbstract)
            {
                report?.Invoke(Diagnostic.Create(InvalidFormatterType, Location.None, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName, ToDisplayName(formatter.TargetTypeFullName)));
                isValid = false;
                continue;
            }

            if (formatter.CtorKind == FormatterCtorKind.None)
            {
                report?.Invoke(Diagnostic.Create(FormatterConstructorNotUsable, Location.None, ToDisplayName(formatter.FormatterTypeFullName), serializer.ClassName));
                isValid = false;
            }
        }

        foreach (var duplicate in serializer.Formatters
                     .Where(formatter => formatter.IsTargetSupported)
                     .GroupBy(formatter => formatter.TargetTypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            report?.Invoke(Diagnostic.Create(DuplicateFormatterRegistration, Location.None, serializer.ClassName, ToDisplayName(duplicate.Key)));
            isValid = false;
        }

        return isValid;
    }

    private static bool ValidateClosedGenericRegistrations(SerializerInfo serializer, Action<Diagnostic>? report)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations.Where(registration => registration.Message == null))
        {
            report?.Invoke(Diagnostic.Create(InvalidClosedGenericRegistration, Location.None, ToDisplayName(registration.TargetDisplayName), serializer.ClassName));
            isValid = false;
        }

        foreach (var duplicate in serializer.ClosedGenericRegistrations
                     .Where(registration => registration.Message != null)
                     .GroupBy(registration => registration.TargetDisplayName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            report?.Invoke(Diagnostic.Create(DuplicateClosedGenericRegistration, Location.None, serializer.ClassName, ToDisplayName(duplicate.Key)));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Fires AKKASG022 when a generic <c>[AkkaSerializable]</c> definition implements this
    /// serializer's protocol interface but no closed construction of it is registered: without a
    /// registration the type would silently never serialize, which is exactly the confusing
    /// broken-codegen failure mode this diagnostic replaces.
    /// </summary>
    private static bool ValidateGenericDefinitions(SerializerInfo serializer, ImmutableArray<MessageInfo> genericDefinitions, Action<Diagnostic>? report)
    {
        if (genericDefinitions.IsDefaultOrEmpty || serializer.ProtocolTypeFullName.Length == 0)
            return true;

        var isValid = true;
        foreach (var definition in genericDefinitions)
        {
            if (!definition.Protocols.Contains(serializer.ProtocolTypeFullName))
                continue;

            var hasRegistration = serializer.ClosedGenericRegistrations.Any(registration =>
                registration.Message != null &&
                string.Equals(registration.Message.DefinitionFullName, definition.FullyQualifiedName, StringComparison.Ordinal));
            if (hasRegistration)
                continue;

            report?.Invoke(Diagnostic.Create(GenericSerializableRequiresRegistration, Location.None, ToDisplayName(definition.FullyQualifiedName), ToDisplayName(serializer.ProtocolTypeFullName), serializer.ClassName));
            isValid = false;
        }

        return isValid;
    }

    private static MessageInfo? ExtractMessage(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var knownTypes = KnownTypes.From(compilation);
        var manifest = string.Empty;
        var allowEmpty = false;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Manifest" && argument.Value.Value is string value)
                manifest = value;
            else if (argument.Key == "AllowEmpty" && argument.Value.Value is bool allowEmptyValue)
                allowEmpty = allowEmptyValue;
        }

        // A generic [AkkaSerializable] DEFINITION is never a message itself -- a source generator
        // cannot reify an open generic. Only its registered closed constructions serialize
        // ([AkkaSerializable<T>], AKKASG020/022). Extract it as a flagged placeholder
        // carrying its protocols (for the AKKASG022 check) but no fields: a T-typed field would map
        // as Unsupported and produce a misleading AKKASG003 against the definition.
        if (symbol.IsGenericType)
        {
            return new MessageInfo(
                symbol.Name,
                GetFullyQualifiedTypeName(symbol),
                manifest,
                ImmutableArray<FieldInfo>.Empty,
                GetProtocolNames(symbol),
                allowEmpty: true,
                isGenericDefinition: true,
                definitionFullName: GetFullyQualifiedTypeName(symbol),
                invalidFields: ImmutableArray<InvalidFieldInfo>.Empty,
                constructionPlan: ConstructionPlan.Empty);
        }

        return ExtractMessageCore(symbol, GetFullyQualifiedTypeName(symbol), manifest, allowEmpty, knownTypes, compilation, definitionFullName: string.Empty);
    }

    /// <summary>
    /// Builds the <see cref="MessageInfo"/> for a concrete serializable type: either an ordinary
    /// non-generic <c>[AkkaSerializable]</c> declaration or a registered closed generic
    /// construction. For a closed construction, <see cref="INamedTypeSymbol.GetMembers"/> returns
    /// SUBSTITUTED members -- a property declared as <c>T Payload</c> surfaces here with its
    /// concrete type argument -- so ordinary field inference applies with no type-parameter
    /// special-casing.
    /// </summary>
    private static MessageInfo ExtractMessageCore(
        INamedTypeSymbol symbol,
        string fullyQualifiedName,
        string manifest,
        bool allowEmpty,
        KnownTypes knownTypes,
        Compilation compilation,
        string definitionFullName)
    {
        var fields = new List<FieldInfo>();
        var fieldSymbols = new List<IPropertySymbol>();
        var invalidFields = ImmutableArray.CreateBuilder<InvalidFieldInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var fieldAttribute = member.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute));
            if (fieldAttribute == null || fieldAttribute.ConstructorArguments.Length != 1)
                continue;

            // A static or getter-inaccessible [AkkaField] property can never be read by the
            // generated Write path (`message.Property`) -- record it as invalid (AKKASG028) instead
            // of emitting uncompilable code, and exclude it from both ordinary field extraction and
            // constructor selection below.
            if (member.IsStatic)
            {
                invalidFields.Add(new InvalidFieldInfo(member.Name, "is static; [AkkaField] requires an instance property"));
                continue;
            }

            if (member.GetMethod == null || !IsAccessibleFromGeneratedCode(member.GetMethod.DeclaredAccessibility))
            {
                invalidFields.Add(new InvalidFieldInfo(member.Name, "has no accessible getter"));
                continue;
            }

            var index = (int)fieldAttribute.ConstructorArguments[0].Value!;
            var isNullable = member.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValueType(member.Type);
            var isEnvelopePayload = member.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.EnvelopePayloadAttribute));
            var unionMembers = ExtractUnionMembers(member, knownTypes, compilation, out var hasUnionAttribute, out var unionDeclaredOnField);

            // Precedence: [AkkaEnvelopePayload] always wins (matching its documented precedence over
            // formatter registrations), then [AkkaUnion], then ordinary inference. When the envelope
            // suppresses a FIELD-LEVEL [AkkaUnion] the conflict is remembered for the AKKASG035
            // advisory: both attributes on one property are conflicting author intent. A TYPE-LEVEL
            // [AkkaUnion] on the field's static type is deliberately exempt -- it serves that
            // interface's other, non-envelope fields, so its presence here is incidental.
            var mapping = isEnvelopePayload ? new TypeMapping(FieldKind.EnvelopePayload)
                : hasUnionAttribute ? new TypeMapping(FieldKind.Union)
                : MapType(member.Type, knownTypes);
            fields.Add(new FieldInfo(
                index,
                member.Name,
                member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                mapping,
                isNullable,
                unionMembers: isEnvelopePayload ? default : unionMembers,
                unionSuppressedByEnvelope: isEnvelopePayload && unionDeclaredOnField));
            fieldSymbols.Add(member);
        }

        var constructionPlan = SelectConstructor(symbol, fields, fieldSymbols, compilation);

        return new MessageInfo(
            symbol.Name,
            fullyQualifiedName,
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            GetProtocolNames(symbol),
            allowEmpty,
            isGenericDefinition: false,
            definitionFullName: definitionFullName,
            invalidFields: invalidFields.ToImmutable(),
            constructionPlan: constructionPlan);
    }

    /// <summary>
    /// Whether a member with this accessibility, declared on the message type, can be referenced
    /// from the generated serializer partial class. The generated class is never nested inside the
    /// message type and never derives from it, so only Public/Internal/ProtectedOrInternal (the
    /// internal-or-protected union, satisfied by same-assembly access) are reachable -- Protected and
    /// PrivateProtected both require a subtype relationship the generated code does not have.
    /// </summary>
    private static bool IsAccessibleFromGeneratedCode(Accessibility accessibility)
    {
        return accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    /// <summary>
    /// Selects the constructor used to reconstruct <paramref name="symbol"/> on deserialize and
    /// plans how each valid [AkkaField] property is supplied: as a NAMED constructor argument (when
    /// it maps to a parameter of the chosen constructor) or as an object-initializer assignment
    /// (when it does not, provided it has an accessible 'set'/'init' accessor). See
    /// <see cref="ConstructionPlan"/>. <paramref name="fields"/>/<paramref name="fieldSymbols"/> are
    /// parallel: <c>fieldSymbols[i]</c> is the property backing <c>fields[i]</c>.
    /// </summary>
    private static ConstructionPlan SelectConstructor(
        INamedTypeSymbol symbol,
        IReadOnlyList<FieldInfo> fields,
        IReadOnlyList<IPropertySymbol> fieldSymbols,
        Compilation compilation)
    {
        var candidates = new List<(IMethodSymbol Ctor, ImmutableArray<ConstructorArgumentPlan> Arguments, ImmutableArray<string> UncoveredDefaulted, int DeclarationOrder)>();
        var declarationOrder = 0;
        foreach (var ctor in symbol.InstanceConstructors)
        {
            var order = declarationOrder++;
            if (!IsAccessibleFromGeneratedCode(ctor.DeclaredAccessibility))
                continue;

            var argumentsBuilder = ImmutableArray.CreateBuilder<ConstructorArgumentPlan>();
            var uncoveredDefaultedBuilder = ImmutableArray.CreateBuilder<string>();
            var eligible = true;
            foreach (var parameter in ctor.Parameters)
            {
                var matchIndex = MatchParameterToFieldIndex(parameter, fields, fieldSymbols, compilation);
                if (matchIndex >= 0)
                {
                    argumentsBuilder.Add(new ConstructorArgumentPlan(parameter.Name, fields[matchIndex].Name));
                    continue;
                }

                // A parameter without a default value MUST map to a field, or the constructor
                // cannot reconstruct the type at all -- not eligible. A defaulted, unmapped
                // parameter keeps the constructor eligible but is remembered for AKKASG027: its
                // value silently resets to the default on every deserialize.
                if (!parameter.HasExplicitDefaultValue)
                {
                    eligible = false;
                    break;
                }

                uncoveredDefaultedBuilder.Add(parameter.Name);
            }

            if (!eligible)
                continue;

            candidates.Add((ctor, argumentsBuilder.ToImmutable(), uncoveredDefaultedBuilder.ToImmutable(), order));
        }

        if (candidates.Count == 0)
        {
            return new ConstructionPlan(
                ImmutableArray<ConstructorArgumentPlan>.Empty,
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create("no accessible constructor maps every non-default parameter to an [AkkaField] property by name with an assignable type"));
        }

        // Most field-mapped parameters wins (fewer leftover properties needing initializer
        // assignment); ties break on fewest total parameters, then declaration order, for a
        // deterministic choice across identical-shaped candidates.
        var chosen = candidates
            .OrderByDescending(candidate => candidate.Arguments.Length)
            .ThenBy(candidate => candidate.Ctor.Parameters.Length)
            .ThenBy(candidate => candidate.DeclarationOrder)
            .First();

        var mappedFieldNames = new HashSet<string>(chosen.Arguments.Select(argument => argument.FieldName), StringComparer.Ordinal);
        var initializerFieldNames = ImmutableArray.CreateBuilder<string>();
        var errors = ImmutableArray.CreateBuilder<string>();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (mappedFieldNames.Contains(field.Name))
                continue;

            var property = fieldSymbols[i];
            var hasAccessibleSetter = property.SetMethod != null && IsAccessibleFromGeneratedCode(property.SetMethod.DeclaredAccessibility);
            if (!hasAccessibleSetter)
            {
                errors.Add($"property '{field.Name}' is not covered by the selected constructor and has no accessible 'set' or 'init' accessor");
                continue;
            }

            initializerFieldNames.Add(field.Name);
        }

        return new ConstructionPlan(chosen.Arguments, initializerFieldNames.ToImmutable(), chosen.UncoveredDefaulted, errors.ToImmutable());
    }

    /// <summary>
    /// Matches a constructor parameter to a field by name -- ordinal (case-sensitive) first, then a
    /// UNIQUE case-insensitive match; an ambiguous case-insensitive match (more than one field name
    /// differs only by case) counts as no match at all rather than guessing. A name match still
    /// requires the property's type to be implicitly convertible to the parameter's type. Returns
    /// the matched field's index into <paramref name="fields"/>, or -1 for no match.
    /// </summary>
    private static int MatchParameterToFieldIndex(
        IParameterSymbol parameter,
        IReadOnlyList<FieldInfo> fields,
        IReadOnlyList<IPropertySymbol> fieldSymbols,
        Compilation compilation)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, parameter.Name, StringComparison.Ordinal))
                return compilation.HasImplicitConversion(fieldSymbols[i].Type, parameter.Type) ? i : -1;
        }

        var matchIndex = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (matchIndex >= 0)
                return -1;

            matchIndex = i;
        }

        if (matchIndex < 0)
            return -1;

        return compilation.HasImplicitConversion(fieldSymbols[matchIndex].Type, parameter.Type) ? matchIndex : -1;
    }

    /// <summary>
    /// Extracts the declared member set of a union field. The member set comes from the field's
    /// own <c>[AkkaUnion]</c> when present (a per-field override), otherwise from an
    /// <c>[AkkaUnion]</c> on the field's STATIC TYPE -- the natural declaration site, where the
    /// union is stated once for every field of that interface/abstract base. For a field declared
    /// as a type parameter inside a registered closed construction, the type-level lookup runs
    /// against the SUBSTITUTED type argument, so <c>T Body</c> with <c>T := IOrderEvent</c> picks
    /// up the union declared on <c>IOrderEvent</c>.
    /// Symbol-dependent facts (assignability to the field's static type, unbound-generic detection)
    /// are captured here; facts that need the whole message set (serializability, manifests) are
    /// validated later in <see cref="ValidateMessages"/> against the serializer's message
    /// dictionary. Malformed arguments (null, not a type, unbound generic) are recorded as
    /// unsupported entries so a diagnostic fires instead of the member silently vanishing.
    /// </summary>
    private static ImmutableArray<UnionMemberInfo> ExtractUnionMembers(
        IPropertySymbol member,
        KnownTypes knownTypes,
        Compilation compilation,
        out bool hasUnionAttribute,
        out bool unionDeclaredOnField)
    {
        hasUnionAttribute = false;
        unionDeclaredOnField = false;
        if (knownTypes.UnionAttribute == null)
            return ImmutableArray<UnionMemberInfo>.Empty;

        // Field-level override wins; otherwise inherit the type-level declaration from the field's
        // static type. OriginalDefinition covers a generic union base, where the attribute lives on
        // the definition. Whether the declaration sits on the FIELD itself is reported separately
        // (unionDeclaredOnField) for the AKKASG035 envelope-conflict advisory.
        var unionAttribute = member.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
        unionDeclaredOnField = unionAttribute != null;
        if (unionAttribute == null && member.Type is INamedTypeSymbol fieldType)
        {
            unionAttribute = fieldType.OriginalDefinition.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
        }

        // AkkaUnionAttribute(Type first, params Type[] rest): TWO constructor arguments now, not
        // one -- [0] is the mandatory `first` member, [1] is the `params` array holding the rest.
        // The full declared member set is the concatenation of both; reading only
        // ConstructorArguments[0].Values (the pre-Seed-2 shape, when the whole set arrived as a
        // single `params Type[] memberTypes` array) would silently see only `first` and drop every
        // other declared member.
        if (unionAttribute == null || unionAttribute.ConstructorArguments.Length != 2)
            return ImmutableArray<UnionMemberInfo>.Empty;

        hasUnionAttribute = true;
        var restArguments = unionAttribute.ConstructorArguments[1].Values;
        var arguments = ImmutableArray.CreateBuilder<TypedConstant>(1 + restArguments.Length);
        arguments.Add(unionAttribute.ConstructorArguments[0]);
        arguments.AddRange(restArguments);

        var builder = ImmutableArray.CreateBuilder<UnionMemberInfo>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (argument.Value is not INamedTypeSymbol memberType || memberType.IsUnboundGenericType)
            {
                var displayName = argument.Value is ITypeSymbol typeSymbol
                    ? typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : "<null>";
                builder.Add(new UnionMemberInfo(displayName, isValueType: false, isAssignable: false, isSupported: false, isSealed: false, isAbstract: false));
                continue;
            }

            builder.Add(new UnionMemberInfo(
                GetMessageDictionaryKey(memberType),
                memberType.IsValueType,
                compilation.HasImplicitConversion(memberType, member.Type),
                isSupported: true,
                isSealed: memberType.IsSealed || memberType.IsValueType,
                isAbstract: memberType.IsAbstract,
                foreignAssemblyName: GetForeignAssemblyName(memberType, knownTypes)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The key a type is looked up under in the serializer's message dictionary. Non-generic types
    /// use the arity-less <see cref="GetFullyQualifiedTypeName"/> (the existing key for every
    /// <c>[AkkaSerializable]</c> message); closed generic constructions use the full,
    /// fully-qualified display string (e.g. <c>Ns.Wrapper&lt;Ns.Foo&gt;</c>) so distinct
    /// constructions stay distinct.
    /// </summary>
    private static string GetMessageDictionaryKey(INamedTypeSymbol type)
    {
        return type.IsGenericType
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : GetFullyQualifiedTypeName(type);
    }

    private static ImmutableDictionary<string, MessageInfo> ResolveMessages(
        ImmutableDictionary<string, MessageInfo> allMessagesByType,
        ImmutableArray<FormatterInfo> formatters)
    {
        if (formatters.IsDefaultOrEmpty)
            return allMessagesByType;

        var formattersByTarget = new Dictionary<string, FormatterInfo>(StringComparer.Ordinal);
        foreach (var formatter in formatters)
            formattersByTarget[formatter.TargetTypeFullName] = formatter;

        var builder = ImmutableDictionary.CreateBuilder<string, MessageInfo>();
        foreach (var pair in allMessagesByType)
        {
            var message = pair.Value;
            var resolvedFields = ImmutableArray.CreateBuilder<FieldInfo>(message.Fields.Length);
            var changed = false;

            foreach (var field in message.Fields)
            {
                if (field.Mapping.Kind != FieldKind.EnvelopePayload &&
                    field.Mapping.TypeFullName.Length > 0 &&
                    formattersByTarget.TryGetValue(field.Mapping.TypeFullName, out var formatter))
                {
                    resolvedFields.Add(field.WithFormatter(new TypeMapping(FieldKind.Formatted, field.Mapping.TypeFullName), formatter));
                    changed = true;
                }
                else
                {
                    resolvedFields.Add(field);
                }
            }

            builder[pair.Key] = changed ? message.WithFields(resolvedFields.ToImmutable()) : message;
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<MessageInfo> CollectReachableMessages(
        ImmutableArray<MessageInfo> topLevelMessages,
        ImmutableDictionary<string, MessageInfo> allMessagesByType)
    {
        var messages = ImmutableArray.CreateBuilder<MessageInfo>();
        var visited = new HashSet<string>();
        var pending = new Queue<MessageInfo>(topLevelMessages);

        while (pending.Count > 0)
        {
            var message = pending.Dequeue();
            if (!visited.Add(message.FullyQualifiedName))
                continue;

            messages.Add(message);
            var referencedObjectTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in message.Fields)
            {
                foreach (var objectMapping in EnumerateObjectMappings(field.Mapping))
                    referencedObjectTypes.Add(objectMapping.TypeFullName);

                // Union members are reachable exactly like nested Object fields: each member needs
                // its Write/Read/SizeOf methods generated for the union dispatch to call into.
                foreach (var unionMember in field.UnionMembers)
                {
                    if (unionMember.IsSupported)
                        referencedObjectTypes.Add(unionMember.TypeFullName);
                }
            }

            foreach (var typeName in referencedObjectTypes)
            {
                if (allMessagesByType.TryGetValue(typeName, out var nestedMessage))
                    pending.Enqueue(nestedMessage);
            }
        }

        return messages.ToImmutable();
    }

    // Walks a mapping and its collection element/key/value mappings, yielding every Object mapping
    // found. A nested [AkkaSerializable] type used only inside a collection (a List<Reading>
    // element, say) is found this way too. Yields the full mapping, not just its name, so a caller
    // can read its flags; a caller that only needs names can project TypeFullName itself.
    private static IEnumerable<TypeMapping> EnumerateObjectMappings(TypeMapping mapping)
    {
        if (mapping.Kind == FieldKind.Object)
            yield return mapping;

        foreach (var argument in mapping.TypeArguments)
        {
            foreach (var nested in EnumerateObjectMappings(argument))
                yield return nested;
        }
    }

    private static bool ValidateMessages(SourceProductionContext context, SerializerInfo serializer, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var isValid = true;
        foreach (var message in topLevelMessages.Where(message => string.IsNullOrWhiteSpace(message.Manifest)))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingManifest, Location.None, ToDisplayName(message.FullyQualifiedName)));
            isValid = false;
        }

        foreach (var duplicate in topLevelMessages
                     .Where(m => !string.IsNullOrWhiteSpace(m.Manifest))
                     .GroupBy(m => m.Manifest, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var typeNames = string.Join(", ", duplicate.Select(m => ToDisplayName(m.FullyQualifiedName)));
            context.ReportDiagnostic(Diagnostic.Create(DuplicateManifest, Location.None, serializer.ClassName, duplicate.Key, typeNames));
            isValid = false;
        }

        foreach (var message in reachableMessages)
        {
            if (message.Fields.Length == 0 && !message.AllowEmpty)
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingFields, Location.None, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
            }

            foreach (var duplicate in message.Fields.GroupBy(field => field.Index).Where(group => group.Count() > 1))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateFieldIndex, Location.None, ToDisplayName(message.FullyQualifiedName), duplicate.Key));
                isValid = false;
            }

            // Structural [AkkaField] problems found during extraction (static property, or a
            // getter the generated Write path could not call): these properties never made it into
            // message.Fields, so they cannot double-report through any of the checks below.
            foreach (var invalidField in message.InvalidFields)
            {
                context.ReportDiagnostic(Diagnostic.Create(FieldPropertyNotAccessible, Location.None, invalidField.PropertyName, ToDisplayName(message.FullyQualifiedName), invalidField.Reason));
                isValid = false;
            }

            // Read-side reconstruction: either no constructor could be selected, or the selected
            // constructor leaves [AkkaField] properties uncovered with no accessible setter to fall
            // back on -- both make deserialize impossible to generate.
            foreach (var error in message.ConstructionPlan.Errors)
            {
                context.ReportDiagnostic(Diagnostic.Create(NoMatchingConstructor, Location.None, ToDisplayName(message.FullyQualifiedName), error));
                isValid = false;
            }

            // Advisory only: the selected constructor still works (its defaulted parameter is simply
            // never supplied), but the parameter's value silently reverts to its default on every
            // deserialize because no [AkkaField] property feeds it.
            foreach (var parameterName in message.ConstructionPlan.UncoveredDefaultedParameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(ConstructorParameterNotCovered, Location.None, parameterName, ToDisplayName(message.FullyQualifiedName)));
            }

            // Advisory only (AKKASG035): both [AkkaEnvelopePayload] and a field-level [AkkaUnion]
            // were declared on this property; extraction dropped the union member set because
            // envelope payload takes precedence (see ExtractMessageCore).
            foreach (var field in message.Fields.Where(field => field.UnionSuppressedByEnvelope))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionDeclarationIgnoredOnEnvelopePayload, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName)));
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Unsupported))
            {
                context.ReportDiagnostic(field.Mapping.SuggestsEnvelopeOrUnion
                    ? Diagnostic.Create(UnsupportedFieldTypePolymorphic, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName))
                    : Diagnostic.Create(UnsupportedFieldType, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.MissingSerializableDefinition))
            {
                ReportMissingNestedSchema(context, message, field.Name, field.TypeFullName, field.Mapping, serializer.ClassName);
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.UnsupportedEnumUnderlyingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnumUnderlyingType, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.Mapping.TypeFullName), ToDisplayName(field.Mapping.EnumUnderlyingTypeName)));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                if (!ValidateUnionField(context, message, field, messagesByType, serializer.ClassName))
                    isValid = false;
            }

            // An Object mapping that resolves to no known message would generate a call to a
            // nonexistent Write/Read/SizeOf method. ReportMissingNestedSchema tells apart the two
            // ways that happens: a genuine unregistered closed generic construction (AKKASG023), or
            // a non-generic type with no syntax in THIS compilation (AKKASG007, cross-assembly
            // wording when the type's declaring assembly says so).
            foreach (var field in message.Fields)
            {
                var seenTypeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var objectMapping in EnumerateObjectMappings(field.Mapping))
                {
                    if (!seenTypeNames.Add(objectMapping.TypeFullName) || messagesByType.ContainsKey(objectMapping.TypeFullName))
                        continue;

                    ReportMissingNestedSchema(context, message, field.Name, objectMapping.TypeFullName, objectMapping, serializer.ClassName);
                    isValid = false;
                }
            }
        }

        // Flattening generic constructions into generated member names is collision-prone in
        // principle (mirrors System.Text.Json's DuplicateTypeName handling): detect and fail
        // instead of silently emitting duplicate members.
        foreach (var collision in reachableMessages
                     .GroupBy(GetMessageMethodName, StringComparer.Ordinal)
                     .Where(group => group.Select(m => m.FullyQualifiedName).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            var typeNames = string.Join(", ", collision.Select(m => ToDisplayName(m.FullyQualifiedName)));
            context.ReportDiagnostic(Diagnostic.Create(DuplicateGeneratedName, Location.None, serializer.ClassName, collision.Key, typeNames));
            isValid = false;
        }

        return isValid;
    }

    // Decision table for a nested type this generator cannot serialize today: a closed generic
    // construction reports AKKASG023 (register it with [AkkaSerializable<T>]); a type declared in a
    // referenced assembly reports the AKKASG007 cross-assembly wording; anything else reports the
    // plain AKKASG007 message.
    private static void ReportMissingNestedSchema(
        SourceProductionContext context,
        MessageInfo message,
        string fieldName,
        string typeFullName,
        TypeMapping mapping,
        string serializerClassName)
    {
        if (mapping.IsGenericConstruction)
        {
            context.ReportDiagnostic(Diagnostic.Create(UnregisteredClosedGenericField, Location.None, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), serializerClassName));
            return;
        }

        context.ReportDiagnostic(mapping.ForeignAssemblyName.Length > 0
            ? Diagnostic.Create(MissingNestedSerializableDefinitionCrossAssembly, Location.None, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName), mapping.ForeignAssemblyName, serializerClassName)
            : Diagnostic.Create(MissingNestedSerializableDefinition, Location.None, fieldName, ToDisplayName(message.FullyQualifiedName), ToDisplayName(typeFullName)));
    }

    /// <summary>
    /// Fires AKKASG034 when a valid <c>[AkkaSerializable&lt;T&gt;]</c> registration's construction
    /// neither implements the serializer's protocol (so it can never become a top-level message)
    /// nor is referenced by any [AkkaField] property of a message reachable from a top-level
    /// message (so it can never be emitted as a nested Object field either, AKKASG023's mechanism).
    /// Such a registration compiles clean today and simply does nothing: <see cref="CollectReachableMessages"/>
    /// never reaches it, so it gets no generated Write/Read/SizeOf methods at all. A construction
    /// registered ONLY for nested-field use (legitimate; it need not implement the protocol) is
    /// exempt as long as it is actually reachable.
    /// </summary>
    private static bool ValidateClosedGenericProtocolCoverage(SourceProductionContext context, SerializerInfo serializer, ImmutableArray<MessageInfo> reachableMessages)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty || serializer.ProtocolTypeFullName.Length == 0)
            return true;

        var reachableNames = new HashSet<string>(reachableMessages.Select(message => message.FullyQualifiedName), StringComparer.Ordinal);
        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations)
        {
            if (registration.Message == null)
                continue;

            if (registration.Message.Protocols.Contains(serializer.ProtocolTypeFullName))
                continue;

            if (reachableNames.Contains(registration.Message.FullyQualifiedName))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(ClosedGenericRegistrationNotInProtocol, Location.None,
                ToDisplayName(registration.TargetDisplayName), serializer.ClassName, ToDisplayName(serializer.ProtocolTypeFullName)));
            isValid = false;
        }

        return isValid;
    }

    // Reports AKKASG015 for a union member with no known message. A referenced-assembly member
    // gets the cross-assembly wording; a same-assembly member gets the plain message.
    private static void ReportUnionMemberNotSerializable(SourceProductionContext context, MessageInfo message, string fieldName, UnionMemberInfo member, string serializerClassName)
    {
        context.ReportDiagnostic(member.ForeignAssemblyName.Length > 0
            ? Diagnostic.Create(UnionMemberNotSerializableCrossAssembly, Location.None, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName), member.ForeignAssemblyName, serializerClassName)
            : Diagnostic.Create(UnionMemberNotSerializable, Location.None, ToDisplayName(member.TypeFullName), fieldName, ToDisplayName(message.FullyQualifiedName)));
    }

    private static bool ValidateUnionField(
        SourceProductionContext context,
        MessageInfo message,
        FieldInfo field,
        ImmutableDictionary<string, MessageInfo> messagesByType,
        string serializerClassName)
    {
        var isValid = true;

        // AkkaUnionAttribute(Type first, params Type[] rest) makes an empty member set
        // unrepresentable: `first` is a mandatory constructor argument, so [AkkaUnion()] does not
        // compile and field.UnionMembers can never be empty here. The "at least one member type is
        // required" half of AKKASG019 that used to guard this is gone along with it.
        foreach (var duplicate in field.UnionMembers
                     .GroupBy(member => member.TypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidUnionMemberSet, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), $"member type '{ToDisplayName(duplicate.Key)}' is declared more than once"));
            isValid = false;
        }

        var manifests = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in field.UnionMembers)
        {
            if (!member.IsSupported || !messagesByType.TryGetValue(member.TypeFullName, out var memberMessage))
            {
                ReportUnionMemberNotSerializable(context, message, field.Name, member, serializerClassName);
                isValid = false;
                continue;
            }

            if (!member.IsAssignable)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotAssignable, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName), ToDisplayName(field.TypeFullName)));
                isValid = false;
            }

            // Advisory tier for member types whose exact-runtime-type dispatch is compromised:
            //  - abstract member (AKKASG036, Warning): dispatch can NEVER select it -- an abstract
            //    type is never a runtime type, so its branch is dead code;
            //  - merely unsealed member (AKKASG025, Info): works, but an undeclared subtype of it
            //    fails at write time -- worth surfacing, not worth failing.
            // An abstract member fires AKKASG036 ONLY: it is definitionally unsealed, and stacking
            // the weaker AKKASG025 on top of it would be noise.
            if (member.IsAbstract)
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberAbstract, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
            else if (!member.IsSealed)
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotSealed, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));

            if (string.IsNullOrWhiteSpace(memberMessage.Manifest))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberMissingManifest, Location.None, ToDisplayName(member.TypeFullName), field.Name, ToDisplayName(message.FullyQualifiedName)));
                isValid = false;
                continue;
            }

            if (!manifests.TryGetValue(memberMessage.Manifest, out var typesWithManifest))
            {
                typesWithManifest = new List<string>();
                manifests[memberMessage.Manifest] = typesWithManifest;
            }

            typesWithManifest.Add(member.TypeFullName);
        }

        foreach (var collision in manifests.Where(pair => pair.Value.Distinct(StringComparer.Ordinal).Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnionMemberManifestCollision, Location.None, field.Name, ToDisplayName(message.FullyQualifiedName), collision.Key, string.Join(", ", collision.Value.Select(ToDisplayName))));
            isValid = false;
        }

        return isValid;
    }

    private static string Generate(SerializerInfo serializer, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var usedFormatters = CollectUsedFormatters(reachableMessages);

        var sb = new StringBuilder();
        var w = new CodeWriter(sb);
        w.Line("// <auto-generated />");
        w.Line("#nullable enable");
        w.Line("using System;");
        w.Line("using System.Buffers;");
        w.BlankLine();

        if (!string.IsNullOrEmpty(serializer.Namespace))
        {
            // The namespace is a dotted name chain, not a single identifier, so it takes the
            // deliberate raw path rather than CodeWriter.Identifier (which escapes one identifier).
            w.Raw("namespace ").Raw(serializer.Namespace).Line(";");
            w.BlankLine();
        }

        w.Raw(GetAccessibilityKeyword(serializer.DeclaredAccessibility)).Raw(" sealed partial class ").Identifier(serializer.ClassName).NewLine();
        using (w.Block())
        {
            GenerateFormatterFields(w, usedFormatters);
            w.Raw("public ").Identifier(serializer.ClassName).Line("(global::Akka.Actor.ExtendedActorSystem system) : base(system)");
            using (w.Block())
            {
                foreach (var formatter in usedFormatters)
                {
                    w.Identifier(GetFormatterFieldName(formatter)).Raw(" = new ").Type(TypeName.Global(formatter.FormatterTypeFullName)).Raw("(");
                    if (formatter.CtorKind == FormatterCtorKind.System)
                        w.Raw("system");
                    w.Line(");");
                }
            }

            w.BlankLine();
            w.Raw("public override int Identifier => ").Number(serializer.SerializerId).Line(";");
            w.BlankLine();
            GenerateRegistration(w, serializer);
            GenerateManifest(w, topLevelMessages);
            GenerateSerialize(w);
            GenerateSerializeDirect(w, topLevelMessages);
            GenerateDeserialize(w, topLevelMessages);
            GenerateSizeHint(w, topLevelMessages);
            GenerateCountingBufferWriter(w);

            var unionHelpers = PlanUnionHelpers(reachableMessages);
            foreach (var message in reachableMessages)
            {
                GenerateSizeMessage(w, message, unionHelpers);
                GenerateWriteMessage(w, message, unionHelpers);
                GenerateReadMessage(w, message, unionHelpers);
            }

            GenerateUnionHelpers(w, unionHelpers, messagesByType);
        }

        return sb.ToString();
    }

    private static ImmutableArray<FormatterInfo> CollectUsedFormatters(ImmutableArray<MessageInfo> reachableMessages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var used = ImmutableArray.CreateBuilder<FormatterInfo>();
        foreach (var message in reachableMessages)
        {
            foreach (var field in message.Fields)
            {
                if (field.Mapping.Kind == FieldKind.Formatted && field.Formatter is { } formatter && seen.Add(formatter.TargetTypeFullName))
                    used.Add(formatter);
            }
        }

        if (used.Count == 0)
            return ImmutableArray<FormatterInfo>.Empty;

        return used.ToImmutable().Sort((a, b) => string.CompareOrdinal(a.TargetTypeFullName, b.TargetTypeFullName));
    }

    private static void GenerateFormatterFields(CodeWriter w, ImmutableArray<FormatterInfo> usedFormatters)
    {
        if (usedFormatters.Length == 0)
            return;

        foreach (var formatter in usedFormatters)
            w.Raw("private readonly ").Type(TypeName.Global(formatter.FormatterTypeFullName)).Raw(" ").Identifier(GetFormatterFieldName(formatter)).Line(";");

        w.BlankLine();
    }

    private static void GenerateRegistration(CodeWriter w, SerializerInfo serializer)
    {
        w.Line("public static partial global::Akka.Serialization.V2.SerializerRegistration CreateRegistration()");
        using (w.Block())
        {
            w.Raw("return global::Akka.Serialization.V2.SerializerRegistration.Create(").StringLiteral(serializer.Name).Line(",");
            using (w.Indented())
            {
                w.Raw("system => new ").Identifier(serializer.ClassName).Line("(system),");
                w.Line("global::System.Collections.Immutable.ImmutableHashSet.Create<global::System.Type>(");
                using (w.Indented())
                {
                    w.Raw("typeof(");
                    // An empty ProtocolTypeFullName (the [AkkaSerializer<T>] type argument was not a
                    // named type) is exempt from AKKASG033 and still reaches emission; it emits an
                    // empty typeof() exactly as the pre-CodeWriter emitter did -- broken generated
                    // code the USER build reports, never a generator crash.
                    if (serializer.ProtocolTypeFullName.Length > 0)
                        w.Type(TypeName.Global(serializer.ProtocolTypeFullName));
                    w.Line("))); ");
                }
            }
        }

        w.BlankLine();
    }

    private static void GenerateManifest(CodeWriter w, ImmutableArray<MessageInfo> messages)
    {
        w.Line("public override string Manifest(object obj)");
        using (w.Block())
        {
            w.Line("return obj switch");
            using (w.ExpressionBlock())
            {
                foreach (var message in messages)
                    w.Type(TypeName.Global(message.FullyQualifiedName)).Raw(" => ").StringLiteral(message.Manifest).Line(",");
                w.Line("_ => throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj))");
            }
        }

        w.BlankLine();
    }

    private static void GenerateSerialize(CodeWriter w)
    {
        w.Line("public override int Serialize(object obj, IBufferWriter<byte> writer)");
        using (w.Block())
        {
            w.Line("var countingWriter = new AkkaGeneratedCountingBufferWriter(writer);");
            w.Line("var messagePackWriter = new global::MessagePack.MessagePackWriter(countingWriter);");
            w.Line("SerializeMessagePack(obj, ref messagePackWriter);");
            w.Line("messagePackWriter.Flush();");
            w.Line("return checked((int)countingWriter.BytesWritten);");
        }

        w.BlankLine();
    }

    private static void GenerateSerializeDirect(CodeWriter w, ImmutableArray<MessageInfo> messages)
    {
        w.Line("private void SerializeMessagePack(object obj, ref global::MessagePack.MessagePackWriter writer)");
        using (w.Block())
        {
            w.Line("switch (obj)");
            using (var sw = w.Switch())
            {
                foreach (var message in messages)
                {
                    using (sw.CaseTypePattern(TypeName.Global(message.FullyQualifiedName), "message"))
                    {
                        w.Raw("Write").Identifier(GetMessageMethodName(message)).Line("(ref writer, message);");
                        w.Line("break;");
                    }
                }

                using (sw.Default())
                    w.Line("throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj));");
            }
        }

        w.BlankLine();
    }

    private static void GenerateDeserialize(CodeWriter w, ImmutableArray<MessageInfo> messages)
    {
        w.Line("public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)");
        using (w.Block())
        {
            w.Line("var reader = new global::MessagePack.MessagePackReader(bytes);");
            w.Line("return manifest switch");
            using (w.ExpressionBlock())
            {
                foreach (var message in messages)
                    w.StringLiteral(message.Manifest).Raw(" => Read").Identifier(GetMessageMethodName(message)).Line("(ref reader),");
                w.Line("_ => throw new global::System.Runtime.Serialization.SerializationException($\"Unknown generated serializer manifest [{manifest}] for serializer [{GetType()}].\")");
            }
        }

        w.BlankLine();
    }

    private static void GenerateSizeHint(CodeWriter w, ImmutableArray<MessageInfo> messages)
    {
        w.Line("public override int SizeHint(object obj)");
        using (w.Block())
        {
            w.Line("return obj switch");
            using (w.ExpressionBlock())
            {
                foreach (var message in messages)
                    w.Type(TypeName.Global(message.FullyQualifiedName)).Raw(" message => SizeOf").Identifier(GetMessageMethodName(message)).Line("(message),");
                w.Line("_ => global::Akka.Serialization.SerializerV2.UnknownSize");
            }
        }

        w.BlankLine();
    }

    private static void GenerateCountingBufferWriter(CodeWriter w)
    {
        w.Line("private sealed class AkkaGeneratedCountingBufferWriter : global::System.Buffers.IBufferWriter<byte>");
        using (w.Block())
        {
            w.Line("private readonly global::System.Buffers.IBufferWriter<byte> _inner;");
            w.BlankLine();
            w.Line("public AkkaGeneratedCountingBufferWriter(global::System.Buffers.IBufferWriter<byte> inner)");
            using (w.Block())
                w.Line("_inner = inner;");
            w.BlankLine();
            w.Raw("public long BytesWritten");
            using (w.InlineBraces())
                w.Raw("get; private set;");
            w.NewLine();
            w.BlankLine();
            w.Line("public void Advance(int count)");
            using (w.Block())
            {
                w.Line("_inner.Advance(count);");
                w.Line("BytesWritten += count;");
            }

            w.BlankLine();
            w.Line("public global::System.Memory<byte> GetMemory(int sizeHint = 0)");
            using (w.Block())
                w.Line("return _inner.GetMemory(sizeHint);");
            w.BlankLine();
            w.Line("public global::System.Span<byte> GetSpan(int sizeHint = 0)");
            using (w.Block())
                w.Line("return _inner.GetSpan(sizeHint);");
        }

        w.BlankLine();
    }

    private static void GenerateSizeMessage(CodeWriter w, MessageInfo message, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers)
    {
        w.Raw("private int SizeOf").Identifier(GetMessageMethodName(message))
            .Raw("(").Type(TypeName.Global(message.FullyQualifiedName)).Line(" message)");
        using (w.Block())
        {
            w.Line("checked");
            using (w.Block())
            {
                w.Raw("var size = SizeOfMapHeader(").Number(message.Fields.Length).Line(");");
                var alloc = new NameAlloc();
                foreach (var field in message.Fields)
                    GenerateSizeField(w, unionHelpers, field, alloc);
                w.Line("return size;");
            }
        }

        w.BlankLine();
    }

    private static void GenerateSizeField(CodeWriter w, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var value = ValueExpr.GeneratorOwned("message").Member(field.Name);
        var localName = Local.ForField(field.Name).WithSuffix("Size");
        w.Raw("size += SizeOfInt32(").Number(field.Index).Line(");");
        if (IsCollectionKind(field.Mapping.Kind))
        {
            var fieldSize = alloc.Next("size");

            // Only reachable for a Nullable<T>-wrapped VALUE-typed collection field (today: only
            // ImmutableArray<T>? -- every other collection kind is a reference type, so its
            // "nullable" comes from reference-nullability, not Nullable<T>, and IsNullableValueField
            // is always false for it). EmitSizeCollectionBody itself accesses members like .IsDefault
            // and .Length on `value` directly, which do not exist on the Nullable<T> WRAPPER -- so the
            // Nullable<T> layer must be peeled off (mirroring GenerateWriteField's identical
            // "if (value is null) ... else ...value.Value..." unwrap) before EmitSizeCollectionBody
            // ever sees the value.
            if (IsNullableValueField(field))
            {
                var unwrappedSize = alloc.Next("size");
                w.Raw("int ").Local(fieldSize).Line(";");
                w.Raw("if (").Value(value).Line(" is null)");
                using (w.Block())
                    w.Local(fieldSize).Line(" = SizeOfNil();");
                w.Line("else");
                using (w.Block())
                {
                    EmitSizeCollectionBody(w, field.Mapping, value.Member("Value"), unwrappedSize, alloc);
                    w.Local(fieldSize).Raw(" = ").Local(unwrappedSize).Line(";");
                }
            }
            else
            {
                EmitSizeCollectionBody(w, field.Mapping, value, fieldSize, alloc);
            }

            w.Raw("size += ").Local(fieldSize).Line(";");
            return;
        }

        if (TryEmitInlineSizeStatement(w, field, value))
            return;

        w.Raw("var ").Local(localName).Raw(" = ");
        GenerateSizeExpression(w, unionHelpers, field, value);
        w.Line(";");
        w.Raw("if (").Local(localName).Line(" < 0)");
        using (w.Indented())
            w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
        w.Raw("size += ").Local(localName).Line(";");
    }

    private static bool TryEmitInlineSizeStatement(CodeWriter w, FieldInfo field, ValueExpr value)
    {
        // Object, EnvelopePayload, and Union always route through the general
        // GenerateSizeExpression path below (they call a generated SizeOfXxx/SizeOfEnvelopePayload/
        // SizeOfUnion method, not a scalar MessagePackSizes helper) -- including when the field is a
        // nullable [AkkaSerializable] struct, which would otherwise match IsNullableValueField below
        // and get an inline scalar expression that EmitScalarSizeExpression cannot produce for
        // FieldKind.Object. Union sizes can also be UnknownSize and need the < 0 guard.
        if (field.Mapping.Kind is FieldKind.Formatted or FieldKind.Object or FieldKind.EnvelopePayload or FieldKind.Union)
            return false;

        if (IsNullableValueField(field))
        {
            w.Raw("size += ").Value(value).Raw(" is null ? SizeOfNil() : ");
            EmitScalarSizeExpression(w, field.Mapping, value.Member("Value"));
            w.Line(";");
            return true;
        }

        w.Raw("size += ");
        EmitScalarSizeExpression(w, field.Mapping, value);
        w.Line(";");
        return true;
    }

    private static void GenerateSizeExpression(CodeWriter w, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, ValueExpr value)
    {
        switch (field.Mapping.Kind)
        {
            case FieldKind.EnvelopePayload:
                w.Raw("SizeOfEnvelopePayload(").Value(value).Raw(")");
                break;
            case FieldKind.Union when field.IsNullable:
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(unionHelpers[BuildUnionSignature(field)].HelperName).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Union:
                w.Raw("SizeOf").Identifier(unionHelpers[BuildUnionSignature(field)].HelperName).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Object when IsNullableValueField(field):
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(GetObjectMethodName(field.Mapping)).Raw("(").Value(value).Raw(".Value)");
                break;
            case FieldKind.Object when field.IsNullable:
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(GetObjectMethodName(field.Mapping)).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Object:
                w.Raw("SizeOf").Identifier(GetObjectMethodName(field.Mapping)).Raw("(").Value(value).Raw(")");
                break;
            case FieldKind.Formatted when IsNullableValueField(field):
                w.Value(value).Raw(" is null ? SizeOfNil() : ").Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".SizeOf(").Value(value).Raw(".Value)");
                break;
            case FieldKind.Formatted when field.IsNullable:
                w.Value(value).Raw(" is null ? SizeOfNil() : ").Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".SizeOf(").Value(value).Raw(")");
                break;
            case FieldKind.Formatted:
                w.Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".SizeOf(").Value(value).Raw(")");
                break;
            default:
                EmitScalarSizeExpression(w, field.Mapping, value);
                break;
        }
    }

    private static void EmitScalarSizeExpression(CodeWriter w, TypeMapping mapping, ValueExpr value)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
                w.Raw("SizeOfString(").Value(value).Raw(")");
                break;
            case FieldKind.ByteArray:
                w.Raw("SizeOfBytes(").Value(value).Raw(")");
                break;
            case FieldKind.Int32:
                w.Raw("SizeOfInt32(").Value(value).Raw(")");
                break;
            case FieldKind.Int64:
                w.Raw("SizeOfInt64(").Value(value).Raw(")");
                break;
            case FieldKind.Boolean:
                w.Raw("SizeOfBoolean(").Value(value).Raw(")");
                break;
            case FieldKind.Double:
                w.Raw("SizeOfDouble(").Value(value).Raw(")");
                break;
            case FieldKind.Decimal:
                w.Raw("SizeOfDecimal(").Value(value).Raw(")");
                break;
            case FieldKind.Guid:
                w.Raw("SizeOfGuid(").Value(value).Raw(")");
                break;
            case FieldKind.DateTime:
                w.Raw("SizeOfDateTime(").Value(value).Raw(")");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("SizeOfDateTimeOffset(").Value(value).Raw(")");
                break;
            case FieldKind.ActorRef:
                w.Raw("SizeOfActorRef(").Value(value).Raw(")");
                break;
            case FieldKind.Enum:
                w.Raw("SizeOfEnum((int)").Value(value).Raw(")");
                break;
            default:
                w.Raw("global::Akka.Serialization.SerializerV2.UnknownSize");
                break;
        }
    }

    private static void GenerateWriteMessage(CodeWriter w, MessageInfo message, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers)
    {
        w.Raw("private void Write").Identifier(GetMessageMethodName(message))
            .Raw("(ref global::MessagePack.MessagePackWriter writer, ").Type(TypeName.Global(message.FullyQualifiedName)).Line(" message)");
        using (w.Block())
        {
            w.Raw("writer.WriteMapHeader(").Number(message.Fields.Length).Line(");");
            var alloc = new NameAlloc();
            foreach (var field in message.Fields)
                GenerateWriteField(w, unionHelpers, field, alloc);
        }

        w.BlankLine();
    }

    private static void GenerateReadMessage(CodeWriter w, MessageInfo message, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers)
    {
        w.Raw("private ").Type(TypeName.Global(message.FullyQualifiedName)).Raw(" Read").Identifier(GetMessageMethodName(message))
            .Line("(ref global::MessagePack.MessagePackReader reader)");
        using (w.Block())
        {
            // Generator-owned locals are prefixed "__" so they cannot collide with a per-field local
            // (Local.ForField(field.Name)/GetHasLocal below), no matter what the [AkkaField] property
            // is named -- including adversarial names like "FieldCount" or "EntryIndex" that would
            // otherwise camel-case straight into these identifiers (CS0128/CS0136).
            w.Line("var __fieldCount = reader.ReadMapHeader();");
            var alloc = new NameAlloc();
            foreach (var field in message.Fields)
            {
                w.Type(TypeName.Global(field.TypeFullName));
                if (IsReferenceLike(field))
                    w.Raw("?");
                w.Raw(" ").Local(Local.ForField(field.Name)).Raw(" = ").Raw(DefaultValue(field)).Line(";");
                if (IsRequired(field))
                    w.Raw("var ").Local(GetHasLocal(field)).Line(" = false;");
            }

            w.Line("for (var __entryIndex = 0; __entryIndex < __fieldCount; __entryIndex++)");
            using (w.Block())
            {
                w.Line("var __fieldId = reader.ReadInt32();");
                w.Line("switch (__fieldId)");
                using (var sw = w.Switch())
                {
                    foreach (var field in message.Fields)
                    {
                        using (sw.CaseNumber(field.Index))
                        {
                            GenerateReadField(w, unionHelpers, field, alloc);
                            if (IsRequired(field))
                                w.Local(GetHasLocal(field)).Line(" = true;");
                            w.Line("break;");
                        }
                    }

                    using (sw.Default())
                    {
                        w.Line("reader.Skip();");
                        w.Line("break;");
                    }
                }
            }

            w.BlankLine();

            foreach (var field in message.Fields.Where(IsRequired))
            {
                var target = Local.ForField(field.Name);
                w.Raw("if (!").Local(GetHasLocal(field));
                if (IsReferenceLike(field))
                    w.Raw(" || ").Local(target).Raw(" is null");
                w.Line(")");
                using (w.Indented())
                {
                    w.Raw("throw new global::System.Runtime.Serialization.SerializationException(\"Missing required field [")
                        .LiteralText(field.Name).Raw("] with index [").Number(field.Index).Raw("] while deserializing [")
                        .LiteralText(message.FullyQualifiedName).Line("].\");");
                }
            }

            GenerateReadMessageConstruction(w, message);
        }

        w.BlankLine();
    }

    /// <summary>
    /// Emits the final <c>return new T(...)</c> of a read method from <see cref="MessageInfo.ConstructionPlan"/>:
    /// NAMED arguments (escaped where the parameter name is a C# keyword, e.g. <c>@event:</c>, via
    /// the writer's <see cref="CodeWriter.Identifier"/> path) for every constructor-mapped
    /// [AkkaField] property, followed by an object initializer for whatever is left over. The plan
    /// stores field NAMES rather than <see cref="FieldInfo"/> references so it stays correct across
    /// <see cref="MessageInfo.WithFields"/> (formatter resolution can replace a field's mapping
    /// without touching its name).
    /// </summary>
    private static void GenerateReadMessageConstruction(CodeWriter w, MessageInfo message)
    {
        var fieldsByName = message.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var plan = message.ConstructionPlan;

        w.Raw("return new ").Type(TypeName.Global(message.FullyQualifiedName)).Raw("(");
        var firstArgument = true;
        foreach (var argument in plan.Arguments)
        {
            if (!firstArgument)
                w.Raw(", ");
            firstArgument = false;
            w.Identifier(argument.ParameterName).Raw(": ").Value(GetFieldValueExpression(fieldsByName[argument.FieldName]));
        }

        w.Raw(")");

        if (plan.InitializerFieldNames.Length > 0)
        {
            using (w.InlineBraces())
            {
                var firstInitializer = true;
                foreach (var name in plan.InitializerFieldNames)
                {
                    if (!firstInitializer)
                        w.Raw(", ");
                    firstInitializer = false;
                    var field = fieldsByName[name];
                    w.Identifier(field.Name).Raw(" = ").Value(GetFieldValueExpression(field));
                }
            }
        }

        w.Line(";");
    }

    // ---------------------------------------------------------------------------------------------
    // Union emission ([AkkaUnion] fields).
    //
    // A union value encodes as a 2-entry int-keyed map: { 1: <member manifest string>, 2: <the
    // member's ordinary inline field map> }. The manifest is the discriminator -- the same
    // serializer-owned manifest the member would carry as a top-level message -- so a value reads
    // identically whether it arrived through union dispatch or ordinary manifest dispatch. Contrast
    // with [AkkaEnvelopePayload]'s { 1: serializerId, 2: manifest, 3: opaque bytes }: the union
    // omits the serializer id (every member is owned by this serializer) and inlines the member's
    // fields directly instead of double-buffering them into a length-prefixed blob.
    //
    // Write dispatch matches the runtime type EXACTLY (value.GetType() == typeof(Member)) rather
    // than pattern matching, so an undeclared subtype of a declared member fails serialization
    // instead of silently truncating to its base -- the same default System.Text.Json applies to
    // [JsonDerivedType] sets (UnknownDerivedTypeHandling.FailSerialization). The size path returns
    // UnknownSize for an undeclared type instead of throwing; the write path throws.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The dedup identity of a union dispatch helper: the field's static type plus the ordered
    /// member set. Two fields (in the same or different messages) with the same static type and
    /// member set -- the common case under type-level [AkkaUnion] declarations -- share one
    /// generated Write/Read/SizeOf helper trio instead of emitting duplicates per field.
    /// </summary>
    private static string BuildUnionSignature(FieldInfo field)
    {
        return field.TypeFullName + "|" + string.Join("|", field.UnionMembers.Select(member => member.TypeFullName));
    }

    /// <summary>
    /// Plans one helper per distinct union signature across all reachable messages. Helpers are
    /// named after the union's folded static type ("Union_IOrderEvent"); when several distinct
    /// member sets share a static type (field-level overrides), later ones -- ordered by signature
    /// for determinism -- get a numeric suffix.
    /// </summary>
    private static ImmutableDictionary<string, (string HelperName, FieldInfo Field)> PlanUnionHelpers(ImmutableArray<MessageInfo> reachableMessages)
    {
        var representatives = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        foreach (var message in reachableMessages)
        {
            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                var signature = BuildUnionSignature(field);
                if (!representatives.ContainsKey(signature))
                    representatives[signature] = field;
            }
        }

        var builder = ImmutableDictionary.CreateBuilder<string, (string, FieldInfo)>(StringComparer.Ordinal);
        foreach (var group in representatives.GroupBy(pair => FoldTypeName(pair.Value.TypeFullName), StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var helperName = i == 0 ? "Union_" + group.Key : "Union_" + group.Key + "_" + (i + 1);
                builder[ordered[i].Key] = (helperName, ordered[i].Value);
            }
        }

        return builder.ToImmutable();
    }

    private static void GenerateUnionHelpers(
        CodeWriter w,
        ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers,
        ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        foreach (var plan in unionHelpers.Values.OrderBy(plan => plan.HelperName, StringComparer.Ordinal))
        {
            var field = plan.Field;
            var members = field.UnionMembers
                .Where(member => member.IsSupported && messagesByType.ContainsKey(member.TypeFullName))
                .Select(member => (Member: member, Message: messagesByType[member.TypeFullName]))
                .ToImmutableArray();

            GenerateUnionWrite(w, field, plan.HelperName, members);
            GenerateUnionRead(w, field, plan.HelperName, members);
            GenerateUnionSize(w, field, plan.HelperName, members);
        }
    }

    private static void GenerateUnionWrite(CodeWriter w, FieldInfo field, string helperName, ImmutableArray<(UnionMemberInfo Member, MessageInfo Message)> members)
    {
        w.Raw("private void Write").Identifier(helperName)
            .Raw("(ref global::MessagePack.MessagePackWriter writer, ").Type(TypeName.Global(field.TypeFullName)).Line(" value)");
        using (w.Block())
        {
            w.Line("var runtimeType = value.GetType();");
            foreach (var (member, memberMessage) in members)
            {
                w.Raw("if (runtimeType == typeof(").Type(TypeName.Global(member.TypeFullName)).Line("))");
                using (w.Block())
                {
                    w.Line("writer.WriteMapHeader(2);");
                    w.Line("writer.Write(1);");
                    w.Raw("writer.Write(").StringLiteral(memberMessage.Manifest).Line(");");
                    w.Line("writer.Write(2);");
                    w.Raw("Write").Identifier(GetMessageMethodName(memberMessage)).Raw("(ref writer, (").Type(TypeName.Global(member.TypeFullName)).Line(")value);");
                    w.Line("return;");
                }

                w.BlankLine();
            }

            w.Raw("throw new global::System.Runtime.Serialization.SerializationException($\"Type [{runtimeType}] is not a declared union member for union [")
                .LiteralText(field.TypeFullName).Line("].\");");
        }

        w.BlankLine();
    }

    private static void GenerateUnionRead(CodeWriter w, FieldInfo field, string helperName, ImmutableArray<(UnionMemberInfo Member, MessageInfo Message)> members)
    {
        w.Raw("private ").Type(TypeName.Global(field.TypeFullName)).Raw(" Read").Identifier(helperName)
            .Line("(ref global::MessagePack.MessagePackReader reader)");
        using (w.Block())
        {
            w.Line("var fieldCount = reader.ReadMapHeader();");
            w.Line("string? manifest = null;");
            w.Type(TypeName.Global(field.TypeFullName)).Line("? result = default;");
            w.Line("var hasPayload = false;");
            w.Line("for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)");
            using (w.Block())
            {
                w.Line("var fieldId = reader.ReadInt32();");
                w.Line("switch (fieldId)");
                using (var sw = w.Switch())
                {
                    using (sw.CaseNumber(1))
                    {
                        w.Line("manifest = reader.ReadString();");
                        w.Line("break;");
                    }

                    using (sw.CaseNumber(2))
                    {
                        w.Line("switch (manifest)");
                        using (var manifestSwitch = w.Switch())
                        {
                            foreach (var (_, memberMessage) in members)
                            {
                                using (manifestSwitch.CaseStringLiteral(memberMessage.Manifest))
                                {
                                    w.Raw("result = Read").Identifier(GetMessageMethodName(memberMessage)).Line("(ref reader);");
                                    w.Line("break;");
                                }
                            }

                            using (manifestSwitch.CaseNull())
                            {
                                w.Raw("throw new global::System.Runtime.Serialization.SerializationException(\"Union manifest must precede the payload for union [")
                                    .LiteralText(field.TypeFullName).Line("].\");");
                            }

                            using (manifestSwitch.Default())
                            {
                                w.Raw("throw new global::System.Runtime.Serialization.SerializationException($\"Unknown union manifest [{manifest}] for union [")
                                    .LiteralText(field.TypeFullName).Line("].\");");
                            }
                        }

                        w.BlankLine();
                        w.Line("hasPayload = true;");
                        w.Line("break;");
                    }

                    using (sw.Default())
                    {
                        w.Line("reader.Skip();");
                        w.Line("break;");
                    }
                }
            }

            w.BlankLine();
            w.Line("if (!hasPayload || result is null)");
            using (w.Indented())
            {
                w.Raw("throw new global::System.Runtime.Serialization.SerializationException(\"Missing union payload for union [")
                    .LiteralText(field.TypeFullName).Line("].\");");
            }

            w.Line("return result;");
        }

        w.BlankLine();
    }

    private static void GenerateUnionSize(CodeWriter w, FieldInfo field, string helperName, ImmutableArray<(UnionMemberInfo Member, MessageInfo Message)> members)
    {
        w.Raw("private int SizeOf").Identifier(helperName)
            .Raw("(").Type(TypeName.Global(field.TypeFullName)).Line(" value)");
        using (w.Block())
        {
            w.Line("var runtimeType = value.GetType();");
            foreach (var (member, memberMessage) in members)
            {
                w.Raw("if (runtimeType == typeof(").Type(TypeName.Global(member.TypeFullName)).Line("))");
                using (w.Block())
                {
                    w.Raw("var payloadSize = SizeOf").Identifier(GetMessageMethodName(memberMessage)).Raw("((").Type(TypeName.Global(member.TypeFullName)).Line(")value);");
                    w.Line("if (payloadSize < 0)");
                    using (w.Indented())
                        w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
                    w.Raw("return checked(SizeOfMapHeader(2) + SizeOfInt32(1) + SizeOfString(").StringLiteral(memberMessage.Manifest)
                        .Line(") + SizeOfInt32(2) + payloadSize);");
                }

                w.BlankLine();
            }

            w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
        }

        w.BlankLine();
    }

    private static void GenerateWriteField(CodeWriter w, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var value = ValueExpr.GeneratorOwned("message").Member(field.Name);
        w.Raw("writer.Write(").Number(field.Index).Line(");");
        if (IsNullableValueField(field))
        {
            w.Raw("if (").Value(value).Line(" is null)");
            using (w.Indented())
                w.Line("writer.WriteNil();");
            w.Line("else");
            using (w.Indented())
                GenerateWriteFieldValue(w, unionHelpers, field, value.Member("Value"), alloc);
            return;
        }

        GenerateWriteFieldValue(w, unionHelpers, field, value, alloc);
    }

    private static void GenerateWriteFieldValue(CodeWriter w, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, ValueExpr value, NameAlloc alloc)
    {
        if (IsCollectionKind(field.Mapping.Kind))
        {
            EmitWriteCollectionBody(w, field.Mapping, value, alloc);
            return;
        }

        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
            case FieldKind.ByteArray:
            case FieldKind.Int32:
            case FieldKind.Int64:
            case FieldKind.Boolean:
            case FieldKind.Double:
                w.Raw("writer.Write(").Value(value).Line(");");
                break;
            case FieldKind.Decimal:
                w.Raw("WriteDecimal(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Guid:
                w.Raw("WriteGuid(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTime:
                w.Raw("WriteDateTime(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("WriteDateTimeOffset(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.ActorRef:
                w.Raw("WriteActorRef(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.EnvelopePayload:
                w.Raw("WriteEnvelopePayload(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Enum:
                w.Raw("writer.Write((int)").Value(value).Line(");");
                break;
            case FieldKind.Object:
                // Mirrors FieldKind.Formatted below: when the nested type is a value type, a
                // nullable field was already unwrapped to its non-nullable .Value by the caller
                // (GenerateWriteField's IsNullableValueField branch), so no further null-check is
                // possible (or needed) here -- only a genuinely nullable REFERENCE nested type
                // needs the runtime "is null" guard.
                if (field.IsNullable && IsReferenceLike(field))
                {
                    w.Raw("if (").Value(value).Line(" is null)");
                    using (w.Indented())
                        w.Line("writer.WriteNil();");
                    w.Line("else");
                    using (w.Indented())
                        w.Raw("Write").Identifier(GetObjectMethodName(field.Mapping)).Raw("(ref writer, ").Value(value).Line(");");
                }
                else
                {
                    w.Raw("Write").Identifier(GetObjectMethodName(field.Mapping)).Raw("(ref writer, ").Value(value).Line(");");
                }
                break;
            case FieldKind.Formatted:
                if (field.IsNullable && IsReferenceLike(field))
                {
                    w.Raw("if (").Value(value).Line(" is null)");
                    using (w.Indented())
                        w.Line("writer.WriteNil();");
                    w.Line("else");
                    using (w.Indented())
                        w.Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".Write(ref writer, ").Value(value).Line(");");
                }
                else
                {
                    w.Identifier(GetFormatterFieldName(field.Formatter!)).Raw(".Write(ref writer, ").Value(value).Line(");");
                }
                break;
            case FieldKind.Union:
                // Union fields are always reference-like (the static type is an interface or
                // abstract base), so only the nullable-reference guard is needed here.
                if (field.IsNullable)
                {
                    w.Raw("if (").Value(value).Line(" is null)");
                    using (w.Indented())
                        w.Line("writer.WriteNil();");
                    w.Line("else");
                    using (w.Indented())
                        w.Raw("Write").Identifier(unionHelpers[BuildUnionSignature(field)].HelperName).Raw("(ref writer, ").Value(value).Line(");");
                }
                else
                {
                    w.Raw("Write").Identifier(unionHelpers[BuildUnionSignature(field)].HelperName).Raw("(ref writer, ").Value(value).Line(");");
                }
                break;
        }
    }

    private static void GenerateReadField(CodeWriter w, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var target = Local.ForField(field.Name);

        // Collection fields own their MessagePack nil handling end-to-end (EmitReadCollectionBody
        // does its own TryReadNil), so they are read directly regardless of the field's nullability:
        // a nil-on-the-wire assigns null, and the post-loop required-field guard rejects a null in a
        // non-nullable collection slot exactly as it does for any other non-nullable reference field.
        if (IsCollectionKind(field.Mapping.Kind))
        {
            GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
            return;
        }

        if (IsNullableValueField(field))
        {
            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(target).Line(" = null;");
            w.Line("else");
            using (w.Indented())
                GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
            return;
        }

        var isNullableReferenceLikeSlot = field.Mapping.Kind == FieldKind.EnvelopePayload
            || field.Mapping.Kind == FieldKind.Union
            || (field.Mapping.Kind == FieldKind.Object && IsReferenceLike(field))
            || (field.Mapping.Kind == FieldKind.Formatted && IsReferenceLike(field));

        if (isNullableReferenceLikeSlot && field.IsNullable)
        {
            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(target).Line(" = null;");
            w.Line("else");
            using (w.Indented())
                GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
            return;
        }

        GenerateReadFieldValue(w, unionHelpers, field, target, alloc);
    }

    private static void GenerateReadFieldValue(CodeWriter w, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, Local target, NameAlloc alloc)
    {
        if (IsCollectionKind(field.Mapping.Kind))
        {
            EmitReadCollectionBody(w, field.Mapping, target, alloc);
            return;
        }

        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                w.Local(target).Line(" = reader.ReadString();");
                break;
            case FieldKind.ByteArray:
                w.Raw("var ").Local(target.WithSuffix("Bytes")).Line(" = reader.ReadBytes();");
                w.Local(target).Raw(" = ").Local(target.WithSuffix("Bytes")).Line("?.ToArray();");
                break;
            case FieldKind.Int32:
                w.Local(target).Line(" = reader.ReadInt32();");
                break;
            case FieldKind.Int64:
                w.Local(target).Line(" = reader.ReadInt64();");
                break;
            case FieldKind.Boolean:
                w.Local(target).Line(" = reader.ReadBoolean();");
                break;
            case FieldKind.Double:
                w.Local(target).Line(" = reader.ReadDouble();");
                break;
            case FieldKind.Decimal:
                w.Local(target).Line(" = ReadDecimal(ref reader);");
                break;
            case FieldKind.Guid:
                w.Local(target).Line(" = ReadGuid(ref reader);");
                break;
            case FieldKind.DateTime:
                w.Local(target).Line(" = ReadDateTime(ref reader);");
                break;
            case FieldKind.DateTimeOffset:
                w.Local(target).Line(" = ReadDateTimeOffset(ref reader);");
                break;
            case FieldKind.ActorRef:
                w.Local(target).Line(" = ReadActorRef(ref reader);");
                break;
            case FieldKind.EnvelopePayload:
                w.Local(target).Raw(" = ReadEnvelopePayload<").Type(TypeName.Global(field.TypeFullName)).Line(">(ref reader);");
                break;
            case FieldKind.Enum:
                w.Local(target).Raw(" = (").Type(TypeName.Global(field.Mapping.TypeFullName)).Line(")reader.ReadInt32();");
                break;
            case FieldKind.Object:
                w.Local(target).Raw(" = Read").Identifier(GetObjectMethodName(field.Mapping)).Line("(ref reader);");
                break;
            case FieldKind.Formatted:
                w.Local(target).Raw(" = ").Identifier(GetFormatterFieldName(field.Formatter!)).Line(".Read(ref reader);");
                break;
            case FieldKind.Union:
                w.Local(target).Raw(" = Read").Identifier(unionHelpers[BuildUnionSignature(field)].HelperName).Line("(ref reader);");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Native collection emission: T[], List<T>, IReadOnlyList<T>, IReadOnlyCollection<T>,
    // Dictionary<TKey,TValue>, IReadOnlyDictionary<TKey,TValue>, ImmutableArray<T>, ImmutableList<T>,
    // ImmutableHashSet<T>, ImmutableDictionary<TKey,TValue>.
    //
    // Collections encode as MessagePack array/map framing wrapped around per-element encodings that
    // reuse the same scalar/object primitives as ordinary fields, and compose recursively so nested
    // collections (List<List<int>>, Dictionary<string, List<Reading>>, ImmutableDictionary<string,
    // List<int>>) work with no special cases. Every collection kind shares this SAME wire framing --
    // an ImmutableList<int> field is byte-identical on the wire to the same data in a List<int> field
    // (see CollectionFieldSpec.cs); only the in-memory construction on read differs per kind:
    //   - T[]                                -> T[] (indexer-set, pre-sized)
    //   - List<T>, IReadOnlyList<T>,
    //     IReadOnlyCollection<T>             -> List<T> (Add, pre-sized capacity)
    //   - Dictionary<K,V>,
    //     IReadOnlyDictionary<K,V>           -> Dictionary<K,V> (indexer-set, pre-sized capacity)
    //   - ImmutableArray<T>                  -> ImmutableArray.CreateBuilder<T>(capacity), then
    //                                            Builder.MoveToImmutable() (zero-copy handoff; the
    //                                            builder is pre-sized to the wire's element count, so
    //                                            Count always equals Capacity when the loop finishes)
    //   - ImmutableList<T>, ImmutableHashSet<T>,
    //     ImmutableDictionary<K,V>           -> the type's own Builder (no capacity parameter -- these
    //                                            are tree/trie-backed, not array-backed), then
    //                                            Builder.ToImmutable()
    // A duplicate key written into a Dictionary/IReadOnlyDictionary/ImmutableDictionary on read is
    // last-write-wins (indexer-set), matching Dictionary<K,V>'s own semantics. A duplicate element
    // written into an ImmutableHashSet on read is silently deduplicated (Builder.Add returns false
    // for an already-present value; the return is ignored), matching a normal set's Add semantics.
    // Set/map ITERATION ORDER on write is whatever GetEnumerator() yields for that runtime type --
    // NOT guaranteed stable for ImmutableHashSet<T>/ImmutableDictionary<K,V> across instances built
    // from the same logical content in a different order, so tests must not byte-compare a written
    // multi-element set/dictionary; only its round-tripped VALUES are guaranteed (see
    // CollectionFieldSpec.cs's ImmutableHashSet/ImmutableDictionary tests, which sort before
    // comparing).
    //
    // ImmutableArray<T> is the one VALUE-typed (struct) collection kind (IsStructCollectionKind), so
    // it cannot use "value is null" (CS0037: not convertible from a non-nullable value type) -- its
    // null-ish state is default(ImmutableArray<T>).IsDefault, which is DISTINCT from
    // ImmutableArray<T>.Empty (Length 0, IsDefault false). This generator maps that distinction onto
    // the SAME nil-vs-empty wire framing every other collection kind already uses:
    //   - write: value.IsDefault  -> MessagePack nil   (mirrors "value is null" for every other kind)
    //   - write: value.Empty (or any non-default, zero-length array) -> array header 0
    //   - read:  nil  -> default(ImmutableArray<T>)    (mirrors "target = null" for every other kind)
    //   - read:  array header 0 (non-nil) -> a Builder(0).MoveToImmutable(), which is
    //            ImmutableArray<T>.Empty (Length 0, IsDefault FALSE) -- distinct from the nil case
    // This makes default(ImmutableArray<T>) round-trip losslessly as itself, exactly as null already
    // round-trips for every reference collection kind, while a genuinely empty array stays
    // distinguishable from both on the wire and after deserialization. Accessing .Length or
    // enumerating a default ImmutableArray<T> throws NullReferenceException at runtime (verified
    // against the in-box System.Collections.Immutable on net10.0), so EVERY code path touching an
    // ImmutableArray<T> value (write, size) MUST check .IsDefault first -- never assume "not null"
    // is enough, the way it is for a reference collection.
    // null encodes as MessagePack nil; empty encodes as a zero-length array/map header. The two are
    // distinct on the wire and round-trip as distinct values. This framing is permanent wire format --
    // see the encoding matrix in the PR body for the full table.
    // ---------------------------------------------------------------------------------------------

    // ImmutableArray<T> has no public Count (it is an explicit ICollection<T>/IReadOnlyCollection<T>
    // implementation, inaccessible on the struct type directly) -- only Length, exactly like T[].
    private static string CollectionCountMember(FieldKind kind) => kind is FieldKind.Array or FieldKind.ImmutableArray ? "Length" : "Count";

    /// <summary>
    /// Whether a value of this element mapping is stored as a reference in its strongly-typed collection
    /// slot. Reference elements are declared as nullable read temporaries and stored with the
    /// null-forgiving operator (a runtime no-op) so the generated code stays warning-clean under
    /// <c>#nullable enable</c> while still round-tripping a genuine null element.
    /// <see cref="FieldKind.ImmutableArray"/> is deliberately excluded even though it is a collection
    /// kind: it is a VALUE type element (see <see cref="IsStructCollectionKind"/>), so wrapping its read
    /// temporary in <c>Nullable&lt;ImmutableArray&lt;T&gt;&gt;</c> and null-forgiving it back would not
    /// even compile against a <c>List&lt;ImmutableArray&lt;T&gt;&gt;.Add(ImmutableArray&lt;T&gt;)</c> slot.
    /// </summary>
    private static bool ElementIsReference(TypeMapping mapping)
    {
        if (IsStructCollectionKind(mapping.Kind))
            return false;

        if (IsCollectionKind(mapping.Kind))
            return true;

        return mapping.Kind switch
        {
            FieldKind.String or FieldKind.ByteArray or FieldKind.ActorRef => true,
            FieldKind.Object => !mapping.IsValueType,
            _ => false
        };
    }

    private static ValueExpr ElementStore(TypeMapping mapping, ValueExpr valueExpr)
        => ElementIsReference(mapping) ? valueExpr.NullForgiven() : valueExpr;

    private static bool IsScalarValueKind(FieldKind kind)
        => kind is FieldKind.Int32 or FieldKind.Int64 or FieldKind.Boolean or FieldKind.Double
            or FieldKind.Decimal or FieldKind.Guid or FieldKind.DateTime or FieldKind.DateTimeOffset or FieldKind.Enum;

    // ----- WRITE -----

    private static void EmitWriteCollectionBody(CodeWriter w, TypeMapping mapping, ValueExpr value, NameAlloc alloc)
    {
        w.Raw("if (").Value(value).Line(IsStructCollectionKind(mapping.Kind) ? ".IsDefault)" : " is null)");
        using (w.Block())
            w.Line("writer.WriteNil();");
        w.Line("else");
        using (w.Block())
        {
            if (IsMapLikeKind(mapping.Kind))
            {
                var kvp = alloc.Next("kvp");
                w.Raw("writer.WriteMapHeader(").Value(value).Line(".Count);");
                w.Raw("foreach (var ").Local(kvp).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                {
                    EmitWriteElement(w, mapping.TypeArguments[0], ((ValueExpr)kvp).Member("Key"), alloc);
                    EmitWriteElement(w, mapping.TypeArguments[1], ((ValueExpr)kvp).Member("Value"), alloc);
                }
            }
            else
            {
                var item = alloc.Next("item");
                w.Raw("writer.WriteArrayHeader(").Value(value.Member(CollectionCountMember(mapping.Kind))).Line(");");
                w.Raw("foreach (var ").Local(item).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                    EmitWriteElement(w, mapping.TypeArguments[0], item, alloc);
            }
        }
    }

    private static void EmitWriteElement(CodeWriter w, TypeMapping mapping, ValueExpr value, NameAlloc alloc)
    {
        if (IsCollectionKind(mapping.Kind))
        {
            EmitWriteCollectionBody(w, mapping, value, alloc);
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                w.Raw("Write").Identifier(GetObjectMethodName(mapping)).Raw("(ref writer, ").Value(value).Line(");");
                return;
            }

            var writeValue = mapping.IsValueType ? value.Member("Value") : value;
            w.Raw("if (").Value(value).Line(" is null)");
            using (w.Indented())
                w.Line("writer.WriteNil();");
            w.Line("else");
            using (w.Indented())
                w.Raw("Write").Identifier(GetObjectMethodName(mapping)).Raw("(ref writer, ").Value(writeValue).Line(");");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            w.Raw("if (").Value(value).Line(" is null)");
            using (w.Indented())
                w.Line("writer.WriteNil();");
            w.Line("else");
            using (w.Indented())
                EmitScalarWrite(w, mapping, value.Member("Value"));
            return;
        }

        EmitScalarWrite(w, mapping, value);
    }

    private static void EmitScalarWrite(CodeWriter w, TypeMapping mapping, ValueExpr value)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
            case FieldKind.ByteArray:
            case FieldKind.Int32:
            case FieldKind.Int64:
            case FieldKind.Boolean:
            case FieldKind.Double:
                w.Raw("writer.Write(").Value(value).Line(");");
                break;
            case FieldKind.Decimal:
                w.Raw("WriteDecimal(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Guid:
                w.Raw("WriteGuid(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTime:
                w.Raw("WriteDateTime(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("WriteDateTimeOffset(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.ActorRef:
                w.Raw("WriteActorRef(ref writer, ").Value(value).Line(");");
                break;
            case FieldKind.Enum:
                w.Raw("writer.Write((int)").Value(value).Line(");");
                break;
        }
    }

    // ----- READ -----

    private static void EmitReadCollectionBody(CodeWriter w, TypeMapping mapping, Local target, NameAlloc alloc)
    {
        w.Line("if (reader.TryReadNil())");
        using (w.Block())
            w.Local(target).Raw(" = ").Raw(NilCollectionValue(mapping.Kind)).Line(";");
        w.Line("else");
        using (w.Block())
        {
            var length = alloc.Next("len");
            var collection = alloc.Next("col");
            var index = alloc.Next("i");

            if (IsMapLikeKind(mapping.Kind))
            {
                var key = mapping.TypeArguments[0];
                var val = mapping.TypeArguments[1];
                var keyVar = alloc.Next("key");
                var valVar = alloc.Next("val");
                w.Raw("var ").Local(length).Line(" = reader.ReadMapHeader();");
                EmitMapLikeAllocation(w, mapping.Kind, key.DeclaredTypeName, val.DeclaredTypeName, collection, length);
                w.Raw("for (var ").Local(index).Raw(" = 0; ").Local(index).Raw(" < ").Local(length).Raw("; ").Local(index).Line("++)");
                using (w.Block())
                {
                    EmitReadElement(w, key, keyVar, alloc);
                    EmitReadElement(w, val, valVar, alloc);
                    // Last-write-wins on a duplicate key, matching Dictionary<K,V>'s own indexer semantics
                    // -- true for the plain Dictionary allocation above AND for ImmutableDictionary.Builder's
                    // indexer (verified: Builder[key] = value overwrites an existing entry, same as Dictionary).
                    w.Local(collection).Raw("[").Value(ElementStore(key, keyVar)).Raw("] = ").Value(ElementStore(val, valVar)).Line(";");
                }
            }
            else
            {
                var element = mapping.TypeArguments[0];
                var itemVar = alloc.Next("item");
                w.Raw("var ").Local(length).Line(" = reader.ReadArrayHeader();");
                EmitListLikeAllocation(w, mapping.Kind, element.DeclaredTypeName, collection, length);
                w.Raw("for (var ").Local(index).Raw(" = 0; ").Local(index).Raw(" < ").Local(length).Raw("; ").Local(index).Line("++)");
                using (w.Block())
                {
                    EmitReadElement(w, element, itemVar, alloc);
                    if (mapping.Kind == FieldKind.Array)
                        w.Local(collection).Raw("[").Local(index).Raw("] = ").Value(ElementStore(element, itemVar)).Line(";");
                    else
                        // Add() on every non-array kind: List<T>'s own Add, ImmutableArray<T>.Builder.Add
                        // (array-backed, pre-sized -- see EmitListLikeAllocation), ImmutableList<T>.Builder.Add,
                        // or ImmutableHashSet<T>.Builder.Add (silently ignores an already-present duplicate,
                        // matching a normal set's Add semantics -- its bool return is intentionally discarded).
                        w.Local(collection).Raw(".Add(").Value(ElementStore(element, itemVar)).Line(");");
                }
            }

            w.Local(target).Raw(" = ");
            EmitFinalizeCollectionExpression(w, mapping.Kind, collection);
            w.Line(";");
        }
    }

    /// <summary>
    /// The value assigned to the read target when the wire holds MessagePack nil. Every reference
    /// collection kind mirrors ordinary reference-field nil handling ("target = null"). The one
    /// VALUE-typed kind, <see cref="FieldKind.ImmutableArray"/>, cannot be assigned "null" (its local
    /// is declared as the plain non-nullable struct type -- see <see cref="IsReferenceLike"/> /
    /// <see cref="ElementIsReference"/>), so nil instead decodes to "default", i.e.
    /// <c>default(ImmutableArray&lt;T&gt;)</c>, whose <c>IsDefault</c> is true -- the read-side mirror
    /// of the write-side <c>value.IsDefault</c> check in <see cref="EmitWriteCollectionBody"/>.
    /// </summary>
    private static string NilCollectionValue(FieldKind kind) => IsStructCollectionKind(kind) ? "default" : "null";

    /// <summary>
    /// Emits the "var col = ...;" allocation that a list-like collection read builds into before the
    /// element loop. <see cref="FieldKind.Array"/> allocates the exact target array (jagged-aware, via
    /// <see cref="EmitArrayAllocationExpression"/>); <see cref="FieldKind.ImmutableArray"/> allocates its
    /// array-backed <c>Builder</c> pre-sized to <paramref name="lengthVar"/> so the loop's <c>Add</c>
    /// calls never resize and the final <c>MoveToImmutable()</c> (see
    /// <see cref="EmitFinalizeCollectionExpression"/>) is a zero-copy handoff instead of a defensive copy;
    /// every other list-like kind (<see cref="FieldKind.List"/>, <see cref="FieldKind.ReadOnlyList"/>,
    /// <see cref="FieldKind.ReadOnlyCollection"/>) materializes a pre-sized <c>List&lt;T&gt;</c>, and
    /// <see cref="FieldKind.ImmutableList"/>/<see cref="FieldKind.ImmutableHashSet"/> allocate their own
    /// tree/trie-backed <c>Builder</c> (no capacity parameter exists for either -- there is nothing
    /// array-like to pre-size).
    /// </summary>
    private static void EmitListLikeAllocation(CodeWriter w, FieldKind kind, string elementTypeName, Local collectionVar, Local lengthVar)
    {
        switch (kind)
        {
            case FieldKind.Array:
                w.Raw("var ").Local(collectionVar).Raw(" = ");
                EmitArrayAllocationExpression(w, elementTypeName, lengthVar);
                w.Line(";");
                break;
            case FieldKind.ImmutableArray:
                w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableArray.CreateBuilder<").Type(TypeName.Global(elementTypeName)).Raw(">(").Local(lengthVar).Line(");");
                break;
            case FieldKind.ImmutableList:
                w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableList.CreateBuilder<").Type(TypeName.Global(elementTypeName)).Line(">();");
                break;
            case FieldKind.ImmutableHashSet:
                w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableHashSet.CreateBuilder<").Type(TypeName.Global(elementTypeName)).Line(">();");
                break;
            default:
                w.Raw("var ").Local(collectionVar).Raw(" = new global::System.Collections.Generic.List<").Type(TypeName.Global(elementTypeName)).Raw(">(").Local(lengthVar).Line(");");
                break;
        }
    }

    /// <summary>
    /// Emits the "var col = ...;" allocation that a map-like collection read builds into before the
    /// entry loop. <see cref="FieldKind.ImmutableDictionary"/> allocates its own trie-backed
    /// <c>Builder</c> (no capacity parameter -- there is nothing array-like to pre-size); every other
    /// map-like kind (<see cref="FieldKind.Dictionary"/>, <see cref="FieldKind.ReadOnlyDictionary"/>)
    /// materializes a pre-sized <c>Dictionary&lt;TKey,TValue&gt;</c>.
    /// </summary>
    private static void EmitMapLikeAllocation(CodeWriter w, FieldKind kind, string keyTypeName, string valueTypeName, Local collectionVar, Local lengthVar)
    {
        if (kind == FieldKind.ImmutableDictionary)
        {
            w.Raw("var ").Local(collectionVar).Raw(" = global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<")
                .Type(TypeName.Global(keyTypeName)).Raw(", ").Type(TypeName.Global(valueTypeName)).Line(">();");
            return;
        }

        w.Raw("var ").Local(collectionVar).Raw(" = new global::System.Collections.Generic.Dictionary<")
            .Type(TypeName.Global(keyTypeName)).Raw(", ").Type(TypeName.Global(valueTypeName)).Raw(">(").Local(lengthVar).Line(");");
    }

    /// <summary>
    /// Emits the expression assigned to the read target once the element/entry loop finishes. Every
    /// kind that allocated its FINAL storage directly (<see cref="FieldKind.Array"/>, <see cref="FieldKind.List"/>
    /// and the read-only interfaces backed by it, <see cref="FieldKind.Dictionary"/> and
    /// <see cref="FieldKind.ReadOnlyDictionary"/>) assigns the collection variable as-is. Every kind
    /// that allocated a <c>Builder</c> instead (see <see cref="EmitListLikeAllocation"/> /
    /// <see cref="EmitMapLikeAllocation"/>) finalizes it here: <see cref="FieldKind.ImmutableArray"/>'s
    /// array-backed builder via <c>MoveToImmutable()</c> (zero-copy -- valid because the builder was
    /// pre-sized to exactly the element count the loop adds), every other <c>Immutable*</c> kind via
    /// <c>ToImmutable()</c>.
    /// </summary>
    private static void EmitFinalizeCollectionExpression(CodeWriter w, FieldKind kind, Local collectionVar)
    {
        switch (kind)
        {
            case FieldKind.ImmutableArray:
                w.Local(collectionVar).Raw(".MoveToImmutable()");
                break;
            case FieldKind.ImmutableList:
            case FieldKind.ImmutableHashSet:
            case FieldKind.ImmutableDictionary:
                w.Local(collectionVar).Raw(".ToImmutable()");
                break;
            default:
                w.Local(collectionVar);
                break;
        }
    }

    /// <summary>
    /// Emits the C# allocation expression for a single-dimension array of
    /// <paramref name="elementTypeName"/>. For a jagged array the length belongs in the FIRST bracket
    /// pair with the element's own bracket pairs appended after it: element <c>int[]</c> allocates as
    /// <c>new int[len][]</c> (not the invalid <c>new int[][len]</c>), element <c>int[][]</c> as
    /// <c>new int[len][][]</c>. Bracket pairs only ever appear as an array suffix in the
    /// fully-qualified display name (generics use angle brackets), so peeling trailing <c>[]</c> pairs
    /// off the element type name recovers the correct structure.
    /// </summary>
    private static void EmitArrayAllocationExpression(CodeWriter w, string elementTypeName, Local lengthVar)
    {
        var core = elementTypeName;
        var suffix = string.Empty;
        while (core.EndsWith("[]", StringComparison.Ordinal))
        {
            core = core.Substring(0, core.Length - 2);
            suffix += "[]";
        }

        w.Raw("new ").Type(TypeName.Global(core)).Raw("[").Local(lengthVar).Raw("]").Raw(suffix);
    }

    private static void EmitReadElement(CodeWriter w, TypeMapping mapping, Local resultVar, NameAlloc alloc)
    {
        // The read temporary's declared type: reference elements get the nullable form and are
        // stored with the null-forgiving operator (see ElementIsReference/ElementStore).
        w.Type(TypeName.Global(mapping.DeclaredTypeName));
        if (ElementIsReference(mapping))
            w.Raw("?");
        w.Raw(" ").Local(resultVar).Line(";");

        if (IsCollectionKind(mapping.Kind))
        {
            EmitReadCollectionBody(w, mapping, resultVar, alloc);
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                w.Local(resultVar).Raw(" = Read").Identifier(GetObjectMethodName(mapping)).Line("(ref reader);");
                return;
            }

            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(resultVar).Line(" = null;");
            w.Line("else");
            using (w.Indented())
                w.Local(resultVar).Raw(" = Read").Identifier(GetObjectMethodName(mapping)).Line("(ref reader);");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            w.Line("if (reader.TryReadNil())");
            using (w.Indented())
                w.Local(resultVar).Line(" = null;");
            w.Line("else");
            using (w.Indented())
            {
                w.Local(resultVar).Raw(" = ");
                EmitScalarReadExpression(w, mapping);
                w.Line(";");
            }

            return;
        }

        w.Local(resultVar).Raw(" = ");
        EmitScalarReadExpression(w, mapping);
        w.Line(";");
    }

    private static void EmitScalarReadExpression(CodeWriter w, TypeMapping mapping)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
                w.Raw("reader.ReadString()");
                break;
            case FieldKind.ByteArray:
                w.Raw("reader.ReadBytes()?.ToArray()");
                break;
            case FieldKind.Int32:
                w.Raw("reader.ReadInt32()");
                break;
            case FieldKind.Int64:
                w.Raw("reader.ReadInt64()");
                break;
            case FieldKind.Boolean:
                w.Raw("reader.ReadBoolean()");
                break;
            case FieldKind.Double:
                w.Raw("reader.ReadDouble()");
                break;
            case FieldKind.Decimal:
                w.Raw("ReadDecimal(ref reader)");
                break;
            case FieldKind.Guid:
                w.Raw("ReadGuid(ref reader)");
                break;
            case FieldKind.DateTime:
                w.Raw("ReadDateTime(ref reader)");
                break;
            case FieldKind.DateTimeOffset:
                w.Raw("ReadDateTimeOffset(ref reader)");
                break;
            case FieldKind.ActorRef:
                w.Raw("ReadActorRef(ref reader)");
                break;
            case FieldKind.Enum:
                w.Raw("(").Type(TypeName.Global(mapping.TypeFullName)).Raw(")reader.ReadInt32()");
                break;
            default:
                w.Raw("default");
                break;
        }
    }

    // ----- SIZE -----

    private static void EmitSizeCollectionBody(CodeWriter w, TypeMapping mapping, ValueExpr value, Local sizeVar, NameAlloc alloc)
    {
        w.Raw("int ").Local(sizeVar).Line(";");
        w.Raw("if (").Value(value).Line(IsStructCollectionKind(mapping.Kind) ? ".IsDefault)" : " is null)");
        using (w.Block())
            w.Local(sizeVar).Line(" = SizeOfNil();");
        w.Line("else");
        using (w.Block())
        {
            if (IsMapLikeKind(mapping.Kind))
            {
                var kvp = alloc.Next("kvp");
                w.Local(sizeVar).Raw(" = SizeOfMapHeader(").Value(value).Line(".Count);");
                w.Raw("foreach (var ").Local(kvp).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                {
                    EmitSizeElement(w, mapping.TypeArguments[0], ((ValueExpr)kvp).Member("Key"), sizeVar, alloc);
                    EmitSizeElement(w, mapping.TypeArguments[1], ((ValueExpr)kvp).Member("Value"), sizeVar, alloc);
                }
            }
            else
            {
                var item = alloc.Next("item");
                w.Local(sizeVar).Raw(" = SizeOfArrayHeader(").Value(value.Member(CollectionCountMember(mapping.Kind))).Line(");");
                w.Raw("foreach (var ").Local(item).Raw(" in ").Value(value).Line(")");
                using (w.Block())
                    EmitSizeElement(w, mapping.TypeArguments[0], item, sizeVar, alloc);
            }
        }
    }

    private static void EmitSizeElement(CodeWriter w, TypeMapping mapping, ValueExpr value, Local sizeVar, NameAlloc alloc)
    {
        if (IsCollectionKind(mapping.Kind))
        {
            var innerSize = alloc.Next("size");
            EmitSizeCollectionBody(w, mapping, value, innerSize, alloc);
            w.Local(sizeVar).Raw(" += ").Local(innerSize).Line(";");
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            var elementSize = alloc.Next("size");
            w.Raw("var ").Local(elementSize).Raw(" = ");
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                w.Raw("SizeOf").Identifier(GetObjectMethodName(mapping)).Raw("(").Value(value).Raw(")");
            }
            else
            {
                var sizedValue = mapping.IsValueType ? value.Member("Value") : value;
                w.Value(value).Raw(" is null ? SizeOfNil() : SizeOf").Identifier(GetObjectMethodName(mapping)).Raw("(").Value(sizedValue).Raw(")");
            }

            w.Line(";");
            w.Raw("if (").Local(elementSize).Line(" < 0)");
            using (w.Indented())
                w.Line("return global::Akka.Serialization.SerializerV2.UnknownSize;");
            w.Local(sizeVar).Raw(" += ").Local(elementSize).Line(";");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            w.Local(sizeVar).Raw(" += ").Value(value).Raw(" is null ? SizeOfNil() : ");
            EmitScalarSizeExpression(w, mapping, value.Member("Value"));
            w.Line(";");
            return;
        }

        w.Local(sizeVar).Raw(" += ");
        EmitScalarSizeExpression(w, mapping, value);
        w.Line(";");
    }

    private static TypeMapping MapType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (TryGetNullableValueType(type, out var underlyingType))
            return MapType(underlyingType, knownTypes);

        // Only attach the fallback underlying-type name for NON-GENERIC named types:
        // GetFullyQualifiedTypeName is arity-less, so stamping it onto a generic field type
        // (e.g. Result<int>) would let it match a formatter registered for a same-named
        // non-generic type (Result) and emit ill-typed code. Generic field types keep an empty
        // mapping name, can never match a formatter, and still fail with AKKASG003.
        var mapping = MapTypeCore(type, knownTypes);
        if (mapping.TypeFullName.Length == 0 && type is INamedTypeSymbol { IsGenericType: false } namedType)
            return mapping.WithTypeFullName(GetFullyQualifiedTypeName(namedType));

        return mapping;
    }

    private static TypeMapping MapTypeCore(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (type is INamedTypeSymbol enumType && type.TypeKind == TypeKind.Enum)
        {
            // Enums encode as int32 on the wire ("writer.Write((int)value)" / "(E)reader.ReadInt32()"),
            // so an underlying type whose values are not all int32-representable (uint, long, ulong)
            // would silently truncate. Reject at compile time (AKKASG014) instead.
            var underlyingType = enumType.EnumUnderlyingType;
            if (underlyingType != null && !IsEnumUnderlyingTypeSupported(underlyingType.SpecialType))
            {
                return new TypeMapping(
                    FieldKind.UnsupportedEnumUnderlyingType,
                    GetFullyQualifiedTypeName(enumType),
                    enumUnderlyingTypeName: underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            return new TypeMapping(FieldKind.Enum, GetFullyQualifiedTypeName(enumType));
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return new TypeMapping(FieldKind.ByteArray);

        // OriginalDefinition covers both shapes: for a non-generic type it is the type itself; for a
        // closed generic construction (Wrapper<Foo>) the [AkkaSerializable] attribute lives on the
        // definition. The mapping name is arity-aware (GetMessageDictionaryKey) so a closed
        // construction resolves to its registered [AkkaSerializable<T>] message -- or, if
        // unregistered, fails AKKASG023 instead of silently dropping its type arguments.
        if (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute)))
            return new TypeMapping(
                FieldKind.Object,
                GetMessageDictionaryKey(namedType),
                namedType.IsValueType,
                foreignAssemblyName: GetForeignAssemblyName(namedType, knownTypes),
                isGenericConstruction: namedType.IsGenericType);

        var mapping = type.SpecialType switch
        {
            SpecialType.System_String => new TypeMapping(FieldKind.String),
            SpecialType.System_Int32 => new TypeMapping(FieldKind.Int32),
            SpecialType.System_Int64 => new TypeMapping(FieldKind.Int64),
            SpecialType.System_Boolean => new TypeMapping(FieldKind.Boolean),
            SpecialType.System_Double => new TypeMapping(FieldKind.Double),
            SpecialType.System_Decimal => new TypeMapping(FieldKind.Decimal),
            SpecialType.System_DateTime => new TypeMapping(FieldKind.DateTime),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.Guid) => new TypeMapping(FieldKind.Guid),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.DateTimeOffset) => new TypeMapping(FieldKind.DateTimeOffset),
            _ when SymbolEqualityComparer.Default.Equals(type, knownTypes.ActorRef) => new TypeMapping(FieldKind.ActorRef),
            _ => new TypeMapping(FieldKind.Unsupported)
        };

        if (mapping.Kind != FieldKind.Unsupported)
            return mapping;

        if (TryMapCollection(type, knownTypes, out var collectionMapping))
            return collectionMapping;

        if (type is INamedTypeSymbol { IsGenericType: false, TypeKind: TypeKind.Class or TypeKind.Struct } missingNestedType)
            return new TypeMapping(FieldKind.MissingSerializableDefinition, GetFullyQualifiedTypeName(missingNestedType), foreignAssemblyName: GetForeignAssemblyName(missingNestedType, knownTypes));

        // AKKASG003 on an interface, an abstract class, or a type parameter is usually a forgotten
        // [AkkaEnvelopePayload]/[AkkaUnion] declaration rather than a genuinely unrepresentable
        // type -- flag it so ValidateMessages can point authors at both fixes instead of leaving
        // them to guess.
        var suggestsEnvelopeOrUnion = type.TypeKind == TypeKind.TypeParameter
            || (type is INamedTypeSymbol { IsAbstract: true } && type.TypeKind is TypeKind.Interface or TypeKind.Class);
        return suggestsEnvelopeOrUnion ? new TypeMapping(FieldKind.Unsupported, suggestsEnvelopeOrUnion: true) : mapping;
    }

    private static string GetForeignAssemblyName(ISymbol symbol, KnownTypes knownTypes)
    {
        var assembly = symbol.ContainingAssembly;
        return assembly != null && !SymbolEqualityComparer.Default.Equals(assembly, knownTypes.CompilationAssembly)
            ? assembly.Name
            : string.Empty;
    }

    /// <summary>
    /// Maps the ten natively-supported collection shapes to their collection <see cref="FieldKind"/>,
    /// recursively mapping element/key/value types so collections compose. Single-type-argument shapes
    /// (<c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>,
    /// <c>ImmutableArray&lt;T&gt;</c>, <c>ImmutableList&lt;T&gt;</c>, <c>ImmutableHashSet&lt;T&gt;</c>) and
    /// key/value shapes (<c>Dictionary&lt;TKey,TValue&gt;</c>, <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>,
    /// <c>ImmutableDictionary&lt;TKey,TValue&gt;</c>) are matched by <see cref="TryMatchSingleArgumentKind"/>
    /// and <see cref="TryMatchKeyValueKind"/> against the field's OWN declared generic type definition (not
    /// an "is-assignable" relationship, so e.g. a field declared <c>IReadOnlyList&lt;T&gt;</c> never matches
    /// the <c>IReadOnlyCollection&lt;T&gt;</c> shape even though the former extends the latter).
    /// A collection whose element/key/value is itself unsupported collapses to
    /// <see cref="FieldKind.Unsupported"/> so AKKASG003 fires with the full field type -- except an
    /// enum element with an unsupported underlying type, which propagates as
    /// <see cref="FieldKind.UnsupportedEnumUnderlyingType"/> so AKKASG014 fires naming the enum.
    /// <c>byte[]</c> is never seen here (it is intercepted earlier as <see cref="FieldKind.ByteArray"/>).
    /// </summary>
    private static bool TryMapCollection(ITypeSymbol type, KnownTypes knownTypes, out TypeMapping mapping)
    {
        mapping = default;

        if (type is IArrayTypeSymbol { Rank: 1 } arrayType && arrayType.ElementType.SpecialType != SpecialType.System_Byte)
        {
            var element = MapCollectionElement(arrayType.ElementType, knownTypes);
            mapping = TryCollapseBadElement(element, out var collapsed)
                ? collapsed
                : new TypeMapping(FieldKind.Array, typeArguments: ImmutableArray.Create(element));
            return true;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } namedType)
            return false;

        var definition = namedType.OriginalDefinition;

        if (TryMatchSingleArgumentKind(definition, knownTypes, out var singleArgumentKind))
        {
            mapping = MapSingleArgumentCollection(singleArgumentKind, namedType, knownTypes);
            return true;
        }

        if (TryMatchKeyValueKind(definition, knownTypes, out var keyValueKind))
        {
            mapping = MapKeyValueCollection(keyValueKind, namedType, knownTypes);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Matches <paramref name="definition"/> against every single-type-argument collection shape's OWN
    /// generic type definition symbol. Order is irrelevant: each shape's definition symbol is distinct
    /// (interface inheritance, e.g. <c>IReadOnlyList&lt;T&gt; : IReadOnlyCollection&lt;T&gt;</c>, does not
    /// make the two definitions equal), so at most one branch can ever match.
    /// </summary>
    private static bool TryMatchSingleArgumentKind(INamedTypeSymbol definition, KnownTypes knownTypes, out FieldKind kind)
    {
        if (knownTypes.ListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ListOfT))
        {
            kind = FieldKind.List;
            return true;
        }

        if (knownTypes.ReadOnlyListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyListOfT))
        {
            kind = FieldKind.ReadOnlyList;
            return true;
        }

        if (knownTypes.ReadOnlyCollectionOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyCollectionOfT))
        {
            kind = FieldKind.ReadOnlyCollection;
            return true;
        }

        if (knownTypes.ImmutableArrayOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableArrayOfT))
        {
            kind = FieldKind.ImmutableArray;
            return true;
        }

        if (knownTypes.ImmutableListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableListOfT))
        {
            kind = FieldKind.ImmutableList;
            return true;
        }

        if (knownTypes.ImmutableHashSetOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableHashSetOfT))
        {
            kind = FieldKind.ImmutableHashSet;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Matches <paramref name="definition"/> against every key/value collection shape's OWN generic type definition symbol. See <see cref="TryMatchSingleArgumentKind"/> for why match order does not matter.</summary>
    private static bool TryMatchKeyValueKind(INamedTypeSymbol definition, KnownTypes knownTypes, out FieldKind kind)
    {
        if (knownTypes.DictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.DictionaryOfKeyValue))
        {
            kind = FieldKind.Dictionary;
            return true;
        }

        if (knownTypes.ReadOnlyDictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyDictionaryOfKeyValue))
        {
            kind = FieldKind.ReadOnlyDictionary;
            return true;
        }

        if (knownTypes.ImmutableDictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ImmutableDictionaryOfKeyValue))
        {
            kind = FieldKind.ImmutableDictionary;
            return true;
        }

        kind = default;
        return false;
    }

    private static TypeMapping MapSingleArgumentCollection(FieldKind kind, INamedTypeSymbol namedType, KnownTypes knownTypes)
    {
        var element = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
        return TryCollapseBadElement(element, out var collapsed)
            ? collapsed
            : new TypeMapping(kind, typeArguments: ImmutableArray.Create(element));
    }

    private static TypeMapping MapKeyValueCollection(FieldKind kind, INamedTypeSymbol namedType, KnownTypes knownTypes)
    {
        var key = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
        var value = MapCollectionElement(namedType.TypeArguments[1], knownTypes);
        return TryCollapseBadElement(key, out var collapsedKey) ? collapsedKey
            : TryCollapseBadElement(value, out var collapsedValue) ? collapsedValue
            : new TypeMapping(kind, typeArguments: ImmutableArray.Create(key, value));
    }

    /// <summary>
    /// Collapses a bad collection element/key/value mapping into the mapping the containing field
    /// should carry. An enum with an unsupported underlying type keeps its identity (enum name plus
    /// backing type) so AKKASG014 can name it even through arbitrarily deep nesting; every other bad
    /// element collapses to plain <see cref="FieldKind.Unsupported"/> for AKKASG003.
    /// </summary>
    private static bool TryCollapseBadElement(TypeMapping element, out TypeMapping collapsed)
    {
        if (element.Kind == FieldKind.UnsupportedEnumUnderlyingType)
        {
            collapsed = new TypeMapping(
                FieldKind.UnsupportedEnumUnderlyingType,
                element.TypeFullName,
                enumUnderlyingTypeName: element.EnumUnderlyingTypeName);
            return true;
        }

        if (element.Kind is FieldKind.Unsupported or FieldKind.MissingSerializableDefinition)
        {
            collapsed = new TypeMapping(FieldKind.Unsupported);
            return true;
        }

        collapsed = default;
        return false;
    }

    /// <summary>
    /// Whether every value of an enum with this underlying type is exactly representable as an int32
    /// (the wire encoding for <see cref="FieldKind.Enum"/>). uint, long, and ulong are rejected: their
    /// out-of-int32-range values would silently truncate through the <c>(int)</c> cast.
    /// </summary>
    private static bool IsEnumUnderlyingTypeSupported(SpecialType underlyingType)
    {
        return underlyingType is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32;
    }

    private static TypeMapping MapCollectionElement(ITypeSymbol type, KnownTypes knownTypes)
    {
        var declaredTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (TryGetNullableValueType(type, out var underlyingType))
            return MapTypeCore(underlyingType, knownTypes).AsCollectionElement(declaredTypeName, isNullable: true);

        var isNullable = type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
        return MapTypeCore(type, knownTypes).AsCollectionElement(declaredTypeName, isNullable);
    }

    private static bool IsCollectionKind(FieldKind kind)
        => kind is FieldKind.Array or FieldKind.List or FieldKind.ReadOnlyList or FieldKind.Dictionary
            or FieldKind.ReadOnlyCollection or FieldKind.ReadOnlyDictionary
            or FieldKind.ImmutableArray or FieldKind.ImmutableList or FieldKind.ImmutableHashSet or FieldKind.ImmutableDictionary;

    /// <summary>Whether a collection kind encodes as a MessagePack MAP (key/value pairs) rather than an ARRAY.</summary>
    private static bool IsMapLikeKind(FieldKind kind)
        => kind is FieldKind.Dictionary or FieldKind.ReadOnlyDictionary or FieldKind.ImmutableDictionary;

    /// <summary>
    /// Whether a collection kind is a VALUE type (struct) rather than a reference type. Only
    /// <see cref="FieldKind.ImmutableArray"/> qualifies today: every other collection kind (arrays,
    /// <c>List&lt;T&gt;</c>, the read-only interfaces, <c>ImmutableList/HashSet/Dictionary</c>) is a
    /// reference type, so a null-ish value is a genuine CLR <c>null</c> and "<c>value is null</c>"
    /// compiles. <c>ImmutableArray&lt;T&gt;</c> cannot be compared to <c>null</c> at all (CS0037) --
    /// its null-ish state is <c>default(ImmutableArray&lt;T&gt;).IsDefault</c>, a distinct state from
    /// <c>ImmutableArray&lt;T&gt;.Empty</c> (which is NOT default). See the design note above
    /// <see cref="EmitWriteCollectionBody"/> for the write/read/size handling this drives.
    /// </summary>
    private static bool IsStructCollectionKind(FieldKind kind)
        => kind == FieldKind.ImmutableArray;

    private static string DefaultValue(FieldInfo field)
    {
        if (field.IsNullable)
            return "null";

        return field.Mapping.Kind switch
        {
            FieldKind.String => "null",
            FieldKind.ByteArray => "null",
            FieldKind.Int32 => "0",
            FieldKind.Int64 => "0L",
            FieldKind.Boolean => "false",
            FieldKind.Double => "0.0",
            FieldKind.Decimal => "0m",
            FieldKind.ActorRef => "global::Akka.Actor.ActorRefs.NoSender",
            FieldKind.EnvelopePayload => "null",
            FieldKind.Union => "null",
            // A required (non-nullable) [AkkaSerializable] struct nested field gets a non-nullable
            // local (see GenerateReadMessage's local declaration/IsReferenceLike): "null" would not
            // compile for it, so fall back to "default" the same way every other non-reference-like
            // kind does below.
            FieldKind.Object => IsReferenceLike(field) ? "null" : "default",
            _ => "default"
        };
    }

    private static bool IsRequired(FieldInfo field)
    {
        return !field.IsNullable;
    }

    private static bool IsReferenceLike(FieldInfo field)
    {
        // ImmutableArray<T> is a collection kind but a VALUE type (struct): a required field is
        // handled like any other non-nullable struct kind (Guid, DateTime, ...) below -- only the
        // "has this field index been seen" guard applies, never a "target is null" check (which
        // would not compile for a struct). See IsStructCollectionKind and the design note above
        // EmitWriteCollectionBody for the full default/IsDefault-vs-null story.
        if (IsStructCollectionKind(field.Mapping.Kind))
            return false;

        if (IsCollectionKind(field.Mapping.Kind))
            return true;

        if (field.Mapping.Kind == FieldKind.Formatted)
            return field.Formatter is { IsTargetValueType: false };

        // Mirrors the Formatted case above: an [AkkaSerializable] nested type used as a required
        // field can be a value type (a readonly record struct), in which case it behaves like a
        // scalar (non-nullable local/constructor argument, no null-check) rather than a reference.
        if (field.Mapping.Kind == FieldKind.Object)
            return !field.Mapping.IsValueType;

        // Union fields are always reference-like: the static type is an interface or abstract base
        // (a struct cannot be the static type of a multi-member union).
        return field.Mapping.Kind is FieldKind.String or FieldKind.ByteArray or FieldKind.ActorRef or FieldKind.EnvelopePayload or FieldKind.Union;
    }

    private static bool IsNullableValueField(FieldInfo field)
    {
        return field.IsNullable && !IsReferenceLike(field);
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return TryGetNullableValueType(type, out _);
    }

    private static bool TryGetNullableValueType(ITypeSymbol type, out ITypeSymbol underlyingType)
    {
        if (type is INamedTypeSymbol namedType && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            underlyingType = namedType.TypeArguments[0];
            return true;
        }

        underlyingType = type;
        return false;
    }

    /// <summary>
    /// "__has" prefix (not the field's own camelCase local, which lacks it): guarantees no collision
    /// with an unrelated property's OWN value local under the pigeon-hole pairing this field name
    /// with another property's name, for example fields "Foo" and "HasFoo" -- "Foo"'s has-guard is
    /// "__hasFoo", distinct from "HasFoo"'s value local "hasFoo".
    /// </summary>
    private static Local GetHasLocal(FieldInfo field)
    {
        return Local.Reserved("__has" + field.Name);
    }

    private static ValueExpr GetFieldValueExpression(FieldInfo field)
    {
        ValueExpr name = Local.ForField(field.Name);
        return IsRequired(field) && IsReferenceLike(field) ? name.NullForgiven() : name;
    }

    private static string GetObjectMethodName(TypeMapping mapping)
    {
        return FoldTypeName(mapping.TypeFullName);
    }

    /// <summary>
    /// Folds a fully-qualified type name into a compact generated-member identifier the way
    /// System.Text.Json's <c>GetTypeInfoPropertyName</c> does: namespaces are dropped, each type
    /// identifier keeps only its simple name, and generic type arguments are concatenated --
    /// <c>Ns.Wrapper&lt;Ns.OrderRequest&gt;</c> becomes <c>WrapperOrderRequest</c>.
    /// These names appear in stack traces (WriteWrapperOrderRequest), so compactness matters.
    /// Flattening is collision-prone by construction (same simple name in two namespaces, marker
    /// ambiguity); AKKASG024 detects collisions among generated members and fails compilation
    /// instead of silently emitting duplicates -- the same trade System.Text.Json makes with its
    /// DuplicateTypeName diagnostic.
    /// </summary>
    private static string FoldTypeName(string typeFullName)
    {
        var sb = new StringBuilder(typeFullName.Length);
        var segment = new StringBuilder();

        void FlushSegment()
        {
            if (segment.Length == 0)
                return;

            sb.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
                sb.Append(segment.ToString(1, segment.Length - 1));
            segment.Clear();
        }

        var source = typeFullName.Replace("global::", string.Empty);
        foreach (var ch in source)
        {
            switch (ch)
            {
                case '.':
                case '+':
                    // Keep only the last identifier of a dotted/nested chain: the segment
                    // accumulated so far was a namespace or containing type.
                    segment.Clear();
                    break;
                case '<':
                case '>':
                case ',':
                case ' ':
                    FlushSegment();
                    break;
                case '[':
                    FlushSegment();
                    break;
                case ']':
                    sb.Append("Array");
                    break;
                case '?':
                    FlushSegment();
                    sb.Append("Nullable");
                    break;
                default:
                    segment.Append(ch);
                    break;
            }
        }

        FlushSegment();
        return sb.ToString();
    }

    private static string GetFormatterFieldName(FormatterInfo formatter)
    {
        return "_akkaFormatter_" + SanitizeTypeName(formatter.TargetTypeFullName);
    }

    private static string SanitizeTypeName(string typeFullName)
    {
        // Used only for formatter field names, whose target types are validated non-generic
        // (AKKASG011) -- generated METHOD names go through FoldTypeName instead. Escape literal
        // underscores FIRST so sanitization is collision-free: 'My.Ns.Foo_Bar' -> 'My_Ns_Foo__Bar'
        // and 'My.Ns.Foo.Bar' -> 'My_Ns_Foo_Bar' stay distinct instead of both collapsing to
        // 'My_Ns_Foo_Bar' (duplicate generated members).
        return typeFullName
            .Replace("global::", string.Empty)
            .Replace("_", "__")
            .Replace(".", "_")
            .Replace("+", "_");
    }

    private static string GetMessageMethodName(MessageInfo message)
    {
        return FoldTypeName(message.FullyQualifiedName);
    }

    /// <summary>
    /// Strips a leading <c>global::</c> prefix from a fully-qualified type name string for display
    /// in a <see cref="Diagnostic"/> message ONLY. Every internal use of a fully-qualified name --
    /// dictionary keys, equality/grouping comparisons, and text appended into emitted source --
    /// keeps the raw <c>global::</c>-qualified form produced by <see cref="GetFullyQualifiedTypeName"/>
    /// and <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>; this helper must be applied only
    /// to the arguments passed to <c>Diagnostic.Create(...)</c> at each reporting call site.
    /// </summary>
    private static string ToDisplayName(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;
    }

    /// <summary>
    /// The fully-qualified display names of every interface (direct and transitive) implemented by
    /// <paramref name="symbol"/>, in <see cref="ITypeSymbol.AllInterfaces"/> order. These are the
    /// cached, symbol-free stand-in for the former <c>ImmutableArray&lt;INamedTypeSymbol&gt;</c>
    /// protocol list: within one compilation a fully-qualified name identifies exactly one type, so
    /// ordinal comparison against <see cref="SerializerInfo.ProtocolTypeFullName"/> (produced by the
    /// same <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>) is equivalent to the former
    /// <see cref="SymbolEqualityComparer.Default"/> matching.
    /// </summary>
    private static ImmutableArray<string> GetProtocolNames(INamedTypeSymbol symbol)
    {
        var interfaces = symbol.AllInterfaces;
        if (interfaces.IsDefaultOrEmpty)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>(interfaces.Length);
        foreach (var implemented in interfaces)
            builder.Add(implemented.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return builder.MoveToImmutable();
    }

    private static string GetFullyQualifiedTypeName(INamedTypeSymbol symbol)
    {
        var parts = new Stack<string>();
        ISymbol? current = symbol;
        while (current is INamedTypeSymbol named)
        {
            parts.Push(named.Name);
            current = named.ContainingType;
        }

        var ns = GetNamespace(symbol);
        return string.IsNullOrEmpty(ns) ? "global::" + string.Join(".", parts) : "global::" + ns + "." + string.Join(".", parts);
    }

    private static string GetNamespace(INamedTypeSymbol symbol)
    {
        var parts = new Stack<string>();
        var ns = symbol.ContainingNamespace;
        while (ns != null && !ns.IsGlobalNamespace)
        {
            parts.Push(ns.Name);
            ns = ns.ContainingNamespace;
        }

        return string.Join(".", parts);
    }

    private static string GetAccessibilityKeyword(Accessibility accessibility)
    {
        return accessibility == Accessibility.Internal ? "internal" : "public";
    }

    // Text-shaping helpers (keyword escaping, camel-casing, string-literal escaping) live on
    // CodeWriter: every emission site reaches them through the writer's typed appends
    // (Identifier/StringLiteral/LiteralText) or the Local/ValueExpr factories, so they cannot be
    // skipped at an emission site.

    // ---------------------------------------------------------------------------------------------
    // Cached pipeline models.
    //
    // Every type below flows through cached incremental nodes (the ForAttributeWithMetadataName
    // transforms and their Collect()ed results), so each one must be (a) SYMBOL-FREE -- an ISymbol
    // never compares equal across compilations, so retaining one silently defeats caching for the
    // whole downstream pipeline -- and (b) VALUE-EQUATABLE, because the incremental engine decides
    // "unchanged" via EqualityComparer<T>.Default. ImmutableArray<T> fields get explicit sequence
    // comparison in each Equals: the struct's own Equals compares the underlying array REFERENCE.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Structural-equality helpers shared by the cached pipeline models below.
    /// </summary>
    private static class ValueEquality
    {
        public static bool SequenceEquals<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        {
            if (left.IsDefault || right.IsDefault)
                return left.IsDefault && right.IsDefault;

            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                    return false;
            }

            return true;
        }

        public const int Seed = 17;

        public static int Combine(int hash, int value) => unchecked(hash * 31 + value);

        public static int Combine(int hash, bool value) => Combine(hash, value ? 1 : 0);

        public static int Combine(int hash, string? value)
            => Combine(hash, value == null ? 0 : StringComparer.Ordinal.GetHashCode(value));

        public static int Combine<T>(int hash, ImmutableArray<T> values)
        {
            if (values.IsDefault)
                return Combine(hash, -1);

            hash = Combine(hash, values.Length);
            foreach (var value in values)
                hash = Combine(hash, value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(value));

            return hash;
        }
    }

    private sealed class SerializerInfo : IEquatable<SerializerInfo>
    {
        public SerializerInfo(
            string ns,
            string className,
            string fullyQualifiedName,
            string name,
            int serializerId,
            string protocolTypeFullName,
            bool protocolTypeIsInterface,
            Accessibility declaredAccessibility,
            ImmutableArray<FormatterInfo> formatters,
            ImmutableArray<ClosedGenericRegistrationInfo> closedGenericRegistrations,
            bool isPartial,
            bool isGeneric,
            bool derivesFromAkkaSerializerBase)
        {
            Namespace = ns;
            ClassName = className;
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            SerializerId = serializerId;
            ProtocolTypeFullName = protocolTypeFullName;
            ProtocolTypeIsInterface = protocolTypeIsInterface;
            DeclaredAccessibility = declaredAccessibility;
            Formatters = formatters;
            ClosedGenericRegistrations = closedGenericRegistrations;
            IsPartial = isPartial;
            IsGeneric = isGeneric;
            DerivesFromAkkaSerializerBase = derivesFromAkkaSerializerBase;
        }

        public string Namespace { get; }
        public string ClassName { get; }
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public int SerializerId { get; }

        /// <summary>
        /// Fully-qualified display name of the <c>[AkkaSerializer&lt;TProtocol&gt;]</c> type
        /// argument; empty when the type argument was not a named type. All protocol matching runs
        /// on this string (ordinal) -- the symbol itself is never retained.
        /// </summary>
        public string ProtocolTypeFullName { get; }

        /// <summary>Whether the protocol type argument is an interface. See AKKASG033.</summary>
        public bool ProtocolTypeIsInterface { get; }

        public Accessibility DeclaredAccessibility { get; }
        public ImmutableArray<FormatterInfo> Formatters { get; }
        public ImmutableArray<ClosedGenericRegistrationInfo> ClosedGenericRegistrations { get; }

        /// <summary>Whether every syntax declaration of this class carries 'partial'. See AKKASG032.</summary>
        public bool IsPartial { get; }

        /// <summary>Whether the serializer class itself is a generic type definition. See AKKASG032.</summary>
        public bool IsGeneric { get; }

        /// <summary>Whether the class derives (directly or transitively) from <c>Akka.Serialization.V2.AkkaSerializer</c>. See AKKASG032.</summary>
        public bool DerivesFromAkkaSerializerBase { get; }

        public bool Equals(SerializerInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
                && string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && SerializerId == other.SerializerId
                && string.Equals(ProtocolTypeFullName, other.ProtocolTypeFullName, StringComparison.Ordinal)
                && ProtocolTypeIsInterface == other.ProtocolTypeIsInterface
                && DeclaredAccessibility == other.DeclaredAccessibility
                && IsPartial == other.IsPartial
                && IsGeneric == other.IsGeneric
                && DerivesFromAkkaSerializerBase == other.DerivesFromAkkaSerializerBase
                && ValueEquality.SequenceEquals(Formatters, other.Formatters)
                && ValueEquality.SequenceEquals(ClosedGenericRegistrations, other.ClosedGenericRegistrations);
        }

        public override bool Equals(object? obj) => Equals(obj as SerializerInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Namespace);
            hash = ValueEquality.Combine(hash, ClassName);
            hash = ValueEquality.Combine(hash, FullyQualifiedName);
            hash = ValueEquality.Combine(hash, Name);
            hash = ValueEquality.Combine(hash, SerializerId);
            hash = ValueEquality.Combine(hash, ProtocolTypeFullName);
            hash = ValueEquality.Combine(hash, ProtocolTypeIsInterface);
            hash = ValueEquality.Combine(hash, (int)DeclaredAccessibility);
            hash = ValueEquality.Combine(hash, IsPartial);
            hash = ValueEquality.Combine(hash, IsGeneric);
            hash = ValueEquality.Combine(hash, DerivesFromAkkaSerializerBase);
            hash = ValueEquality.Combine(hash, Formatters);
            hash = ValueEquality.Combine(hash, ClosedGenericRegistrations);
            return hash;
        }
    }

    private sealed class KnownTypes
    {
        private KnownTypes(Compilation compilation)
        {
            CompilationAssembly = compilation.Assembly;
            FieldAttribute = compilation.GetTypeByMetadataName(FieldAttributeFullName);
            EnvelopePayloadAttribute = compilation.GetTypeByMetadataName(EnvelopePayloadAttributeFullName);
            UnionAttribute = compilation.GetTypeByMetadataName(UnionAttributeFullName);
            SerializableAttribute = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
            Guid = compilation.GetTypeByMetadataName("System.Guid");
            DateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
            ActorRef = compilation.GetTypeByMetadataName("Akka.Actor.IActorRef");
            ListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            ReadOnlyListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
            ReadOnlyCollectionOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyCollection`1");
            DictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
            ReadOnlyDictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
            ImmutableArrayOfT = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableArray`1");
            ImmutableListOfT = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableList`1");
            ImmutableHashSetOfT = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableHashSet`1");
            ImmutableDictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableDictionary`2");
        }

        /// <summary>
        /// The assembly of the compilation this generator run is producing output for. Used only to
        /// tell apart a type declared locally from one declared in a referenced assembly (the
        /// AKKASG007/AKKASG015 cross-assembly hint) -- never carried into any extracted model, so it
        /// does not affect incremental caching.
        /// </summary>
        public IAssemblySymbol CompilationAssembly { get; }

        public INamedTypeSymbol? FieldAttribute { get; }
        public INamedTypeSymbol? EnvelopePayloadAttribute { get; }
        public INamedTypeSymbol? UnionAttribute { get; }
        public INamedTypeSymbol? SerializableAttribute { get; }
        public INamedTypeSymbol? Guid { get; }
        public INamedTypeSymbol? DateTimeOffset { get; }
        public INamedTypeSymbol? ActorRef { get; }
        public INamedTypeSymbol? ListOfT { get; }
        public INamedTypeSymbol? ReadOnlyListOfT { get; }
        public INamedTypeSymbol? ReadOnlyCollectionOfT { get; }
        public INamedTypeSymbol? DictionaryOfKeyValue { get; }
        public INamedTypeSymbol? ReadOnlyDictionaryOfKeyValue { get; }

        /// <summary>
        /// <c>System.Collections.Immutable.ImmutableArray&lt;T&gt;</c> -- a VALUE type (struct), unlike
        /// every other recognized collection kind. <c>default(ImmutableArray&lt;T&gt;)</c> is a distinct
        /// "uninitialized" state (<c>IsDefault</c>) from <c>ImmutableArray&lt;T&gt;.Empty</c>; see the
        /// write/read handling gated on <see cref="FieldKind.ImmutableArray"/> throughout this file.
        /// </summary>
        public INamedTypeSymbol? ImmutableArrayOfT { get; }
        public INamedTypeSymbol? ImmutableListOfT { get; }
        public INamedTypeSymbol? ImmutableHashSetOfT { get; }
        public INamedTypeSymbol? ImmutableDictionaryOfKeyValue { get; }

        public static KnownTypes From(Compilation compilation)
        {
            return new KnownTypes(compilation);
        }
    }

    private sealed class MessageInfo : IEquatable<MessageInfo>
    {
        public MessageInfo(
            string simpleName,
            string fullyQualifiedName,
            string manifest,
            ImmutableArray<FieldInfo> fields,
            ImmutableArray<string> protocols,
            bool allowEmpty,
            ImmutableArray<InvalidFieldInfo> invalidFields,
            ConstructionPlan constructionPlan,
            bool isGenericDefinition = false,
            string definitionFullName = "")
        {
            SimpleName = simpleName;
            FullyQualifiedName = fullyQualifiedName;
            Manifest = manifest;
            Fields = fields;
            Protocols = protocols;
            AllowEmpty = allowEmpty;
            InvalidFields = invalidFields;
            ConstructionPlan = constructionPlan;
            IsGenericDefinition = isGenericDefinition;
            DefinitionFullName = definitionFullName;
        }

        public string SimpleName { get; }
        public string FullyQualifiedName { get; }
        public string Manifest { get; }
        public ImmutableArray<FieldInfo> Fields { get; }

        /// <summary>
        /// Fully-qualified display names of every implemented interface (see
        /// <see cref="GetProtocolNames"/>) -- the symbol-free protocol list matched ordinally
        /// against <see cref="SerializerInfo.ProtocolTypeFullName"/> for top-level dispatch.
        /// </summary>
        public ImmutableArray<string> Protocols { get; }

        public bool AllowEmpty { get; }

        /// <summary>
        /// [AkkaField] properties excluded from <see cref="Fields"/> because they are structurally
        /// unusable (static, or an inaccessible getter) -- see AKKASG028. Empty for a valid message.
        /// </summary>
        public ImmutableArray<InvalidFieldInfo> InvalidFields { get; }

        /// <summary>
        /// How the read method reconstructs this type on deserialize: the chosen constructor's
        /// NAMED-argument bindings plus any leftover object-initializer assignments, or the reasons
        /// no plan could be built (AKKASG026/027). See <see cref="ConstructionPlan"/>.
        /// </summary>
        public ConstructionPlan ConstructionPlan { get; }

        /// <summary>
        /// True for a generic <c>[AkkaSerializable]</c> DEFINITION (e.g. <c>Wrapper&lt;T&gt;</c>):
        /// a placeholder that is never serialized, never top-level, and never reachable -- it exists
        /// only so AKKASG022 can fire when the definition implements a serializer protocol but has
        /// no registered closed constructions.
        /// </summary>
        public bool IsGenericDefinition { get; }

        /// <summary>
        /// For a registered closed construction: the arity-less fully-qualified name of its generic
        /// definition, linking the registration back to the definition for the AKKASG022 check.
        /// Empty for ordinary non-generic messages.
        /// </summary>
        public string DefinitionFullName { get; }

        /// <summary>
        /// Used by formatter resolution to swap in fields with a resolved <see cref="TypeMapping"/>.
        /// <see cref="ConstructionPlan"/> is keyed by field NAME, not by <see cref="FieldInfo"/>
        /// reference, so it stays valid across this substitution without needing to be rebuilt.
        /// </summary>
        public MessageInfo WithFields(ImmutableArray<FieldInfo> fields)
        {
            return new MessageInfo(SimpleName, FullyQualifiedName, Manifest, fields, Protocols, AllowEmpty, InvalidFields, ConstructionPlan, IsGenericDefinition, DefinitionFullName);
        }

        public bool Equals(MessageInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(SimpleName, other.SimpleName, StringComparison.Ordinal)
                && string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
                && string.Equals(Manifest, other.Manifest, StringComparison.Ordinal)
                && AllowEmpty == other.AllowEmpty
                && IsGenericDefinition == other.IsGenericDefinition
                && string.Equals(DefinitionFullName, other.DefinitionFullName, StringComparison.Ordinal)
                && ConstructionPlan.Equals(other.ConstructionPlan)
                && ValueEquality.SequenceEquals(Fields, other.Fields)
                && ValueEquality.SequenceEquals(Protocols, other.Protocols)
                && ValueEquality.SequenceEquals(InvalidFields, other.InvalidFields);
        }

        public override bool Equals(object? obj) => Equals(obj as MessageInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, SimpleName);
            hash = ValueEquality.Combine(hash, FullyQualifiedName);
            hash = ValueEquality.Combine(hash, Manifest);
            hash = ValueEquality.Combine(hash, AllowEmpty);
            hash = ValueEquality.Combine(hash, IsGenericDefinition);
            hash = ValueEquality.Combine(hash, DefinitionFullName);
            hash = ValueEquality.Combine(hash, ConstructionPlan.GetHashCode());
            hash = ValueEquality.Combine(hash, Fields);
            hash = ValueEquality.Combine(hash, Protocols);
            hash = ValueEquality.Combine(hash, InvalidFields);
            return hash;
        }
    }

    /// <summary>
    /// A single <c>[AkkaField]</c> property found unusable during extraction: static, or its getter
    /// is not accessible to the generated code. See AKKASG028.
    /// </summary>
    private sealed class InvalidFieldInfo : IEquatable<InvalidFieldInfo>
    {
        public InvalidFieldInfo(string propertyName, string reason)
        {
            PropertyName = propertyName;
            Reason = reason;
        }

        public string PropertyName { get; }

        /// <summary>Free-text reason, e.g. "is static; ..." or "has no accessible getter".</summary>
        public string Reason { get; }

        public bool Equals(InvalidFieldInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal)
                && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as InvalidFieldInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, PropertyName);
            hash = ValueEquality.Combine(hash, Reason);
            return hash;
        }
    }

    /// <summary>
    /// How a message's constructor is called on deserialize. <see cref="Arguments"/> supplies NAMED
    /// constructor arguments (parameter name -&gt; field name); <see cref="InitializerFieldNames"/>
    /// lists [AkkaField] properties assigned afterward via object initializer. Both are non-empty only
    /// when <see cref="IsValid"/>; otherwise <see cref="Errors"/> explains what could not be satisfied
    /// (AKKASG026). <see cref="UncoveredDefaultedParameters"/> is advisory (AKKASG027) and can be
    /// non-empty even when <see cref="IsValid"/> is true.
    /// </summary>
    private sealed class ConstructionPlan : IEquatable<ConstructionPlan>
    {
        public static readonly ConstructionPlan Empty = new(
            ImmutableArray<ConstructorArgumentPlan>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty);

        public ConstructionPlan(
            ImmutableArray<ConstructorArgumentPlan> arguments,
            ImmutableArray<string> initializerFieldNames,
            ImmutableArray<string> uncoveredDefaultedParameters,
            ImmutableArray<string> errors)
        {
            Arguments = arguments;
            InitializerFieldNames = initializerFieldNames;
            UncoveredDefaultedParameters = uncoveredDefaultedParameters;
            Errors = errors;
        }

        public ImmutableArray<ConstructorArgumentPlan> Arguments { get; }
        public ImmutableArray<string> InitializerFieldNames { get; }
        public ImmutableArray<string> UncoveredDefaultedParameters { get; }

        /// <summary>Human-readable reasons construction is impossible; empty when valid.</summary>
        public ImmutableArray<string> Errors { get; }

        public bool Equals(ConstructionPlan? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return ValueEquality.SequenceEquals(Arguments, other.Arguments)
                && ValueEquality.SequenceEquals(InitializerFieldNames, other.InitializerFieldNames)
                && ValueEquality.SequenceEquals(UncoveredDefaultedParameters, other.UncoveredDefaultedParameters)
                && ValueEquality.SequenceEquals(Errors, other.Errors);
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructionPlan);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Arguments);
            hash = ValueEquality.Combine(hash, InitializerFieldNames);
            hash = ValueEquality.Combine(hash, UncoveredDefaultedParameters);
            hash = ValueEquality.Combine(hash, Errors);
            return hash;
        }
    }

    /// <summary>A single NAMED constructor argument: <see cref="ParameterName"/> supplied from the field named <see cref="FieldName"/>.</summary>
    private readonly struct ConstructorArgumentPlan : IEquatable<ConstructorArgumentPlan>
    {
        public ConstructorArgumentPlan(string parameterName, string fieldName)
        {
            ParameterName = parameterName;
            FieldName = fieldName;
        }

        public string ParameterName { get; }
        public string FieldName { get; }

        public bool Equals(ConstructorArgumentPlan other)
        {
            return string.Equals(ParameterName, other.ParameterName, StringComparison.Ordinal)
                && string.Equals(FieldName, other.FieldName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is ConstructorArgumentPlan other && Equals(other);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, ParameterName);
            hash = ValueEquality.Combine(hash, FieldName);
            return hash;
        }
    }

    /// <summary>
    /// A single <c>[AkkaSerializable&lt;T&gt;]</c> registration. <see cref="Message"/> is null
    /// when the target was invalid (not a type, non-generic, unbound, or its definition lacks
    /// <c>[AkkaSerializable]</c>) so AKKASG020 fires instead of the registration silently vanishing.
    /// </summary>
    private sealed class ClosedGenericRegistrationInfo : IEquatable<ClosedGenericRegistrationInfo>
    {
        public ClosedGenericRegistrationInfo(string targetDisplayName, MessageInfo? message)
        {
            TargetDisplayName = targetDisplayName;
            Message = message;
        }

        public string TargetDisplayName { get; }
        public MessageInfo? Message { get; }

        public bool Equals(ClosedGenericRegistrationInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(TargetDisplayName, other.TargetDisplayName, StringComparison.Ordinal)
                && Equals(Message, other.Message);
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedGenericRegistrationInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, TargetDisplayName);
            hash = ValueEquality.Combine(hash, Message?.GetHashCode() ?? 0);
            return hash;
        }
    }

    private sealed class FieldInfo : IEquatable<FieldInfo>
    {
        public FieldInfo(int index, string name, string typeFullName, TypeMapping mapping, bool isNullable, FormatterInfo? formatter = null, ImmutableArray<UnionMemberInfo> unionMembers = default, bool unionSuppressedByEnvelope = false)
        {
            Index = index;
            Name = name;
            TypeFullName = typeFullName;
            Mapping = mapping;
            IsNullable = isNullable;
            Formatter = formatter;
            UnionMembers = unionMembers.IsDefault ? ImmutableArray<UnionMemberInfo>.Empty : unionMembers;
            UnionSuppressedByEnvelope = unionSuppressedByEnvelope;
        }

        public int Index { get; }
        public string Name { get; }
        public string TypeFullName { get; }
        public TypeMapping Mapping { get; }
        public bool IsNullable { get; }
        public FormatterInfo? Formatter { get; }

        /// <summary>Declared members for a <see cref="FieldKind.Union"/> field; empty otherwise.</summary>
        public ImmutableArray<UnionMemberInfo> UnionMembers { get; }

        /// <summary>
        /// True when the property carried BOTH [AkkaEnvelopePayload] and a field-level [AkkaUnion]:
        /// extraction dropped the union member set because envelope payload takes precedence.
        /// Advisory AKKASG035 fires on it.
        /// </summary>
        public bool UnionSuppressedByEnvelope { get; }

        public FieldInfo WithFormatter(TypeMapping mapping, FormatterInfo formatter)
        {
            return new FieldInfo(Index, Name, TypeFullName, mapping, IsNullable, formatter, UnionMembers, UnionSuppressedByEnvelope);
        }

        public bool Equals(FieldInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Index == other.Index
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                && Mapping.Equals(other.Mapping)
                && IsNullable == other.IsNullable
                && UnionSuppressedByEnvelope == other.UnionSuppressedByEnvelope
                && Equals(Formatter, other.Formatter)
                && ValueEquality.SequenceEquals(UnionMembers, other.UnionMembers);
        }

        public override bool Equals(object? obj) => Equals(obj as FieldInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, Index);
            hash = ValueEquality.Combine(hash, Name);
            hash = ValueEquality.Combine(hash, TypeFullName);
            hash = ValueEquality.Combine(hash, Mapping.GetHashCode());
            hash = ValueEquality.Combine(hash, IsNullable);
            hash = ValueEquality.Combine(hash, UnionSuppressedByEnvelope);
            hash = ValueEquality.Combine(hash, Formatter?.GetHashCode() ?? 0);
            hash = ValueEquality.Combine(hash, UnionMembers);
            return hash;
        }
    }

    private readonly struct TypeMapping : IEquatable<TypeMapping>
    {
        public TypeMapping(
            FieldKind kind,
            string typeFullName = "",
            bool isValueType = false,
            string declaredTypeName = "",
            bool isNullable = false,
            ImmutableArray<TypeMapping> typeArguments = default,
            string enumUnderlyingTypeName = "",
            string foreignAssemblyName = "",
            bool suggestsEnvelopeOrUnion = false,
            bool isGenericConstruction = false)
        {
            Kind = kind;
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            DeclaredTypeName = declaredTypeName;
            IsNullable = isNullable;
            TypeArguments = typeArguments.IsDefault ? ImmutableArray<TypeMapping>.Empty : typeArguments;
            EnumUnderlyingTypeName = enumUnderlyingTypeName;
            ForeignAssemblyName = foreignAssemblyName;
            SuggestsEnvelopeOrUnion = suggestsEnvelopeOrUnion;
            IsGenericConstruction = isGenericConstruction;
        }

        public FieldKind Kind { get; }
        public string TypeFullName { get; }

        /// <summary>
        /// For <see cref="FieldKind.Object"/>: whether the annotated <c>[AkkaSerializable]</c> nested
        /// type is a value type (for example, a <c>readonly record struct</c>). Mirrors
        /// <see cref="FormatterInfo.IsTargetValueType"/>, which threads the same distinction for
        /// <see cref="FieldKind.Formatted"/> foreign-type formatter targets. Unused for every other kind.
        /// </summary>
        public bool IsValueType { get; }

        /// <summary>
        /// The exact fully-qualified C# type name (from <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>)
        /// used to declare read temporaries and construct collection instances when this mapping is a
        /// collection element/key/value. Populated only for mappings produced by <c>MapCollectionElement</c>.
        /// For a <c>Nullable&lt;T&gt;</c> value element it includes the trailing <c>?</c> (for example
        /// <c>int?</c>); for a reference element it is the non-nullable form.
        /// </summary>
        public string DeclaredTypeName { get; }

        /// <summary>
        /// For a collection element/key/value mapping: whether the element may be MessagePack <c>nil</c>.
        /// True for a <c>Nullable&lt;T&gt;</c> value element or a nullable-annotated reference element.
        /// Reference objects and nested collections are always nil-guarded regardless of this flag; it is
        /// only load-bearing for distinguishing <c>T?</c> from <c>T</c> among value-type elements.
        /// </summary>
        public bool IsNullable { get; }

        /// <summary>
        /// Child mappings for a collection kind: a single element mapping for every single-type-argument
        /// kind (<see cref="FieldKind.Array"/>, <see cref="FieldKind.List"/>, <see cref="FieldKind.ReadOnlyList"/>,
        /// <see cref="FieldKind.ReadOnlyCollection"/>, <see cref="FieldKind.ImmutableArray"/>,
        /// <see cref="FieldKind.ImmutableList"/>, <see cref="FieldKind.ImmutableHashSet"/>), and [key, value]
        /// for every key/value kind (<see cref="FieldKind.Dictionary"/>, <see cref="FieldKind.ReadOnlyDictionary"/>,
        /// <see cref="FieldKind.ImmutableDictionary"/>). Empty for every non-collection kind.
        /// </summary>
        public ImmutableArray<TypeMapping> TypeArguments { get; }

        /// <summary>
        /// For <see cref="FieldKind.UnsupportedEnumUnderlyingType"/>: the display name of the enum's
        /// underlying type (for example <c>long</c>), carried alongside <see cref="TypeFullName"/> (the
        /// enum itself) so AKKASG014 can name both. Empty for every other kind.
        /// </summary>
        public string EnumUnderlyingTypeName { get; }

        // For Object/MissingSerializableDefinition: the type's declaring assembly name, but only
        // when it is not the one this generator is producing output for. Empty otherwise. Drives
        // the AKKASG007 cross-assembly hint.
        public string ForeignAssemblyName { get; }

        // For Unsupported: whether the field's static type is an interface, abstract class, or type
        // parameter, the shapes a forgotten [AkkaEnvelopePayload]/[AkkaUnion] usually produces.
        // Drives the AKKASG003 hint.
        public bool SuggestsEnvelopeOrUnion { get; }

        /// <summary>
        /// For <see cref="FieldKind.Object"/>: whether the type is a closed construction of a generic
        /// <c>[AkkaSerializable]</c> definition (for example <c>Wrapper&lt;int&gt;</c>), from
        /// <c>INamedTypeSymbol.IsGenericType</c>. Picks AKKASG023 versus AKKASG007 when the type is
        /// missing from this compilation's message table. False for every other kind.
        /// </summary>
        public bool IsGenericConstruction { get; }

        public TypeMapping WithTypeFullName(string typeFullName)
            => new(Kind, typeFullName, IsValueType, DeclaredTypeName, IsNullable, TypeArguments, EnumUnderlyingTypeName, ForeignAssemblyName, SuggestsEnvelopeOrUnion, IsGenericConstruction);

        public TypeMapping AsCollectionElement(string declaredTypeName, bool isNullable)
            => new(Kind, TypeFullName, IsValueType, declaredTypeName, isNullable, TypeArguments, EnumUnderlyingTypeName, ForeignAssemblyName, SuggestsEnvelopeOrUnion, IsGenericConstruction);

        // Explicit IEquatable implementation: the compiler-provided struct equality would compare
        // the TypeArguments ImmutableArray by underlying-array REFERENCE, breaking value equality
        // for every collection mapping (and with it, incremental caching of any model carrying one).
        public bool Equals(TypeMapping other)
        {
            return Kind == other.Kind
                && string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                && IsValueType == other.IsValueType
                && string.Equals(DeclaredTypeName, other.DeclaredTypeName, StringComparison.Ordinal)
                && IsNullable == other.IsNullable
                && string.Equals(EnumUnderlyingTypeName, other.EnumUnderlyingTypeName, StringComparison.Ordinal)
                && string.Equals(ForeignAssemblyName, other.ForeignAssemblyName, StringComparison.Ordinal)
                && SuggestsEnvelopeOrUnion == other.SuggestsEnvelopeOrUnion
                && IsGenericConstruction == other.IsGenericConstruction
                && ValueEquality.SequenceEquals(TypeArguments, other.TypeArguments);
        }

        public override bool Equals(object? obj) => obj is TypeMapping other && Equals(other);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, (int)Kind);
            hash = ValueEquality.Combine(hash, TypeFullName);
            hash = ValueEquality.Combine(hash, IsValueType);
            hash = ValueEquality.Combine(hash, DeclaredTypeName);
            hash = ValueEquality.Combine(hash, IsNullable);
            hash = ValueEquality.Combine(hash, EnumUnderlyingTypeName);
            hash = ValueEquality.Combine(hash, ForeignAssemblyName);
            hash = ValueEquality.Combine(hash, SuggestsEnvelopeOrUnion);
            hash = ValueEquality.Combine(hash, IsGenericConstruction);
            hash = ValueEquality.Combine(hash, TypeArguments);
            return hash;
        }
    }

    /// <summary>
    /// A serializer-scoped hand-written formatter registration extracted from
    /// <c>[AkkaSerializerFormatter&lt;TTarget, TFormatter&gt;]</c>. Carries only
    /// strings/bools/enums (no <see cref="ISymbol"/> references) so it stays cheap to hold across
    /// incremental generator passes.
    /// </summary>
    private sealed class FormatterInfo : IEquatable<FormatterInfo>
    {
        public FormatterInfo(string targetTypeFullName, bool isTargetValueType, string formatterTypeFullName, bool isAbstract, FormatterCtorKind ctorKind, bool isTargetSupported)
        {
            TargetTypeFullName = targetTypeFullName;
            IsTargetValueType = isTargetValueType;
            FormatterTypeFullName = formatterTypeFullName;
            IsAbstract = isAbstract;
            CtorKind = ctorKind;
            IsTargetSupported = isTargetSupported;
        }

        public string TargetTypeFullName { get; }
        public bool IsTargetValueType { get; }
        public string FormatterTypeFullName { get; }

        /// <summary>
        /// Whether TFormatter is an abstract type. The `where TFormatter :
        /// IAkkaMessagePackFormatter&lt;TTarget&gt;` constraint does not rule this out (there is no
        /// `new()` clause), so it is checked here instead (AKKASG008).
        /// </summary>
        public bool IsAbstract { get; }
        public FormatterCtorKind CtorKind { get; }
        public bool IsTargetSupported { get; }

        public bool Equals(FormatterInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(TargetTypeFullName, other.TargetTypeFullName, StringComparison.Ordinal)
                && IsTargetValueType == other.IsTargetValueType
                && string.Equals(FormatterTypeFullName, other.FormatterTypeFullName, StringComparison.Ordinal)
                && IsAbstract == other.IsAbstract
                && CtorKind == other.CtorKind
                && IsTargetSupported == other.IsTargetSupported;
        }

        public override bool Equals(object? obj) => Equals(obj as FormatterInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, TargetTypeFullName);
            hash = ValueEquality.Combine(hash, IsTargetValueType);
            hash = ValueEquality.Combine(hash, FormatterTypeFullName);
            hash = ValueEquality.Combine(hash, IsAbstract);
            hash = ValueEquality.Combine(hash, (int)CtorKind);
            hash = ValueEquality.Combine(hash, IsTargetSupported);
            return hash;
        }
    }

    private enum FormatterCtorKind
    {
        None,
        Parameterless,
        System
    }

    private enum FieldKind
    {
        Unsupported,
        String,
        ByteArray,
        Int32,
        Int64,
        Boolean,
        Double,
        Decimal,
        Guid,
        DateTime,
        DateTimeOffset,
        ActorRef,
        EnvelopePayload,
        Enum,
        Object,
        MissingSerializableDefinition,
        Formatted,
        Array,
        List,
        ReadOnlyList,
        Dictionary,
        UnsupportedEnumUnderlyingType,
        Union,
        ReadOnlyCollection,
        ReadOnlyDictionary,
        ImmutableArray,
        ImmutableList,
        ImmutableHashSet,
        ImmutableDictionary
    }

    /// <summary>
    /// A single declared member of an <c>[AkkaUnion]</c> field. Carries only strings/bools (no
    /// <see cref="ISymbol"/> references) so it stays cheap across incremental generator passes.
    /// Facts requiring symbol access (assignability, unbound-generic detection) are captured at
    /// extraction time; facts requiring the whole-compilation message set (serializability,
    /// manifests) are resolved later against the serializer's message dictionary.
    /// </summary>
    private sealed class UnionMemberInfo : IEquatable<UnionMemberInfo>
    {
        public UnionMemberInfo(string typeFullName, bool isValueType, bool isAssignable, bool isSupported, bool isSealed, bool isAbstract, string foreignAssemblyName = "")
        {
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            IsAssignable = isAssignable;
            IsSupported = isSupported;
            IsSealed = isSealed;
            IsAbstract = isAbstract;
            ForeignAssemblyName = foreignAssemblyName;
        }

        /// <summary>Message-dictionary key for the member type (arity-aware for generics).</summary>
        public string TypeFullName { get; }

        public bool IsValueType { get; }

        /// <summary>Whether the member type is implicitly convertible to the field's static type.</summary>
        public bool IsAssignable { get; }

        /// <summary>False when the attribute argument was null, not a type, or an unbound generic.</summary>
        public bool IsSupported { get; }

        /// <summary>Whether undeclared subtypes are impossible (sealed class, struct). Advisory AKKASG025 fires otherwise -- unless the member is abstract, which escalates to AKKASG036.</summary>
        public bool IsSealed { get; }

        /// <summary>
        /// Whether the member type is abstract. Exact-runtime-type write dispatch can never select
        /// an abstract member (an abstract type is never a runtime type), so its dispatch branch is
        /// dead code. Advisory AKKASG036 (Warning) fires on it instead of AKKASG025.
        /// </summary>
        public bool IsAbstract { get; }

        // The member type's declaring assembly name, but only when it is not the compilation this
        // generator is producing output for. Empty otherwise. Drives the AKKASG015 cross-assembly hint.
        public string ForeignAssemblyName { get; }

        public bool Equals(UnionMemberInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                && IsValueType == other.IsValueType
                && IsAssignable == other.IsAssignable
                && IsSupported == other.IsSupported
                && IsSealed == other.IsSealed
                && IsAbstract == other.IsAbstract
                && string.Equals(ForeignAssemblyName, other.ForeignAssemblyName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as UnionMemberInfo);

        public override int GetHashCode()
        {
            var hash = ValueEquality.Seed;
            hash = ValueEquality.Combine(hash, TypeFullName);
            hash = ValueEquality.Combine(hash, IsValueType);
            hash = ValueEquality.Combine(hash, IsAssignable);
            hash = ValueEquality.Combine(hash, IsSupported);
            hash = ValueEquality.Combine(hash, IsSealed);
            hash = ValueEquality.Combine(hash, IsAbstract);
            hash = ValueEquality.Combine(hash, ForeignAssemblyName);
            return hash;
        }
    }
}
