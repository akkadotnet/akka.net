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
    private const string FormatterAttributeFullName = "Akka.Serialization.V2.AkkaSerializerFormatterAttribute";
    private const string FormatterInterfaceFullName = "Akka.Serialization.V2.IAkkaMessagePackFormatter`1";
    private const string ExtendedActorSystemFullName = "Akka.Actor.ExtendedActorSystem";
    private const string AkkaSerializerBaseTypeFullName = "Akka.Serialization.V2.AkkaSerializer";

    private static readonly DiagnosticDescriptor MissingSerializerName = new(
        "AKKASG001",
        "Serializer name is required",
        "[AkkaSerializer] class '{0}' must specify Name for explicit registration",
        "Akka.Serialization.V2",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingSerializerId = new(
        "AKKASG002",
        "Serializer id is required for POC generator",
        "[AkkaSerializer] class '{0}' must specify SerializerId for the POC generator",
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

    private static readonly DiagnosticDescriptor InvalidFormatterType = new(
        "AKKASG008",
        "Formatter type is invalid",
        "Formatter '{0}' on serializer '{1}' must be a non-abstract, non-generic class implementing IAkkaMessagePackFormatter<{2}>",
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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var serializers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializerAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractSerializer(ctx))
            .Where(static info => info != null)
            .Collect();

        var messages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, _) => ExtractMessage(ctx))
            .Where(static info => info != null)
            .Collect();

        // Combined only at this final, terminal stage -- not stored in any cached IncrementalValueProvider
        // node upstream of it -- so AKKASG029's whole-compilation scan (ValidateProtocolCoverage) can see
        // every source-declared type. This necessarily costs incrementality for the WHOLE generator (the
        // Compilation input changes on every edit anywhere in this project), not just that one check: there
        // is no cheaper place to compute "does any type implement this protocol interface without
        // [AkkaSerializable]" than the terminal stage that already has a Compilation in hand.
        context.RegisterSourceOutput(serializers.Combine(messages).Combine(context.CompilationProvider), static (ctx, tuple) =>
        {
            var (pair, compilation) = tuple;

            var duplicateSerializerIds = pair.Left
                .Where(s => s != null)
                .Cast<SerializerInfo>()
                .Where(s => s.SerializerId != 0)
                .GroupBy(s => s.SerializerId)
                .Where(group => group.Count() > 1)
                .ToImmutableDictionary(group => group.Key, group => string.Join(", ", group.Select(s => s.ClassName)));

            foreach (var duplicate in duplicateSerializerIds)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(DuplicateSerializerId, Location.None, duplicate.Key, duplicate.Value));
            }

            // Same computation as duplicateSerializerIds above, grouped on the protocol interface
            // instead of the numeric id: two [AkkaSerializer] classes bound to the same protocol is
            // silent last-wins at runtime registration today (AKKASG031).
            var duplicateProtocolBindings = pair.Left
                .Where(s => s != null)
                .Cast<SerializerInfo>()
                .Where(s => !string.IsNullOrEmpty(s.ProtocolTypeFullName))
                .GroupBy(s => s.ProtocolTypeFullName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToImmutableDictionary(group => group.Key, group => string.Join(", ", group.Select(s => s.ClassName)), StringComparer.Ordinal);

            foreach (var duplicate in duplicateProtocolBindings)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(DuplicateProtocolBinding, Location.None, duplicate.Key, duplicate.Value));
            }

            foreach (var serializer in pair.Left)
            {
                if (serializer == null)
                    continue;

                if (string.IsNullOrWhiteSpace(serializer.Name))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(MissingSerializerName, Location.None, serializer.ClassName));
                    continue;
                }

                if (serializer.SerializerId == 0)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(MissingSerializerId, Location.None, serializer.ClassName));
                    continue;
                }

                if (duplicateSerializerIds.ContainsKey(serializer.SerializerId))
                    continue;

                if (duplicateProtocolBindings.ContainsKey(serializer.ProtocolTypeFullName))
                    continue;

                if (!ValidateSerializerShape(ctx, serializer))
                    continue;

                if (!ValidateProtocolType(ctx, serializer))
                    continue;

                if (!ValidateFormatters(ctx, serializer))
                    continue;

                if (!ValidateClosedGenericRegistrations(ctx, serializer))
                    continue;

                var declaredMessages = pair.Right
                    .Where(message => message != null)
                    .Cast<MessageInfo>()
                    .ToImmutableArray();

                // Generic definitions are placeholders: never serialized, never top-level, never in
                // the message dictionary (their arity-less key could even collide with a same-named
                // non-generic type). They exist only for the AKKASG022 check below.
                var genericDefinitions = declaredMessages
                    .Where(message => message.IsGenericDefinition)
                    .ToImmutableArray();

                if (!ValidateGenericDefinitions(ctx, serializer, genericDefinitions))
                    continue;

                if (!ValidateProtocolCoverage(ctx, serializer, compilation))
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
                    .Where(message => serializer.ProtocolType != null && message.Protocols.Any(protocol => SymbolEqualityComparer.Default.Equals(protocol, serializer.ProtocolType)))
                    .Select(message => resolvedMessagesByType[message.FullyQualifiedName])
                    .ToImmutableArray();
                var reachableMessages = CollectReachableMessages(topLevelMessages, resolvedMessagesByType);

                if (!ValidateMessages(ctx, serializer, topLevelMessages, reachableMessages, resolvedMessagesByType))
                    continue;

                if (!ValidateClosedGenericProtocolCoverage(ctx, serializer, reachableMessages))
                    continue;

                ctx.AddSource(serializer.ClassName + ".AkkaSerialization.g.cs", Generate(serializer, topLevelMessages, reachableMessages, resolvedMessagesByType));
            }
        });
    }

    private static SerializerInfo? ExtractSerializer(GeneratorAttributeSyntaxContext context)
    {
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

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Value is string value)
                name = value;
            else if (argument.Key == "SerializerId" && argument.Value.Value is int id)
                serializerId = id;
        }

        var protocolType = attribute.AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        var protocolTypeFullName = protocolType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;

        var formatterAttributeType = compilation.GetTypeByMetadataName(FormatterAttributeFullName);
        var formatterInterfaceType = compilation.GetTypeByMetadataName(FormatterInterfaceFullName);
        var extendedActorSystemType = compilation.GetTypeByMetadataName(ExtendedActorSystemFullName);
        var formatters = ExtractFormatters(symbol, formatterAttributeType, formatterInterfaceType, extendedActorSystemType);
        var closedGenericRegistrations = ExtractClosedGenericRegistrations(symbol, compilation);

        return new SerializerInfo(
            GetNamespace(symbol),
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            name ?? string.Empty,
            serializerId,
            protocolType,
            protocolTypeFullName,
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
        INamedTypeSymbol? formatterInterfaceType,
        INamedTypeSymbol? extendedActorSystemType)
    {
        if (formatterAttributeType == null)
            return ImmutableArray<FormatterInfo>.Empty;

        var formatterAttributes = symbol.GetAttributes()
            .Where(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, formatterAttributeType))
            .ToImmutableArray();

        if (formatterAttributes.IsEmpty)
            return ImmutableArray<FormatterInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<FormatterInfo>(formatterAttributes.Length);
        foreach (var attribute in formatterAttributes)
        {
            if (attribute.ConstructorArguments.Length != 2)
                continue;

            // Never silently drop a registration: malformed arguments (null, or something that is
            // not a type at all) are recorded as invalid entries so a diagnostic fires instead of
            // the registration silently doing nothing.
            var targetTypeSymbol = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var formatterTypeSymbol = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            // Formatter targets must be plain named types: arrays are not INamedTypeSymbol, and
            // generic targets (open or closed) would collide on the arity-less fully-qualified
            // name used for field matching. Null/non-type targets are equally unsupported.
            // All of these are recorded with IsTargetSupported = false so AKKASG011 fires.
            var targetNamedType = targetTypeSymbol as INamedTypeSymbol;
            var isTargetSupported = targetNamedType is { IsGenericType: false };
            var targetTypeFullName = isTargetSupported
                ? GetFullyQualifiedTypeName(targetNamedType!)
                : targetTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<null>";

            var formatterNamedType = formatterTypeSymbol as INamedTypeSymbol;
            var formatterTypeFullName = formatterNamedType != null
                ? GetFullyQualifiedTypeName(formatterNamedType)
                : formatterTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<null>";

            var implementsInterface = isTargetSupported &&
                formatterInterfaceType != null &&
                formatterNamedType is { TypeKind: TypeKind.Class, IsAbstract: false, IsGenericType: false } &&
                formatterNamedType.AllInterfaces.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, formatterInterfaceType) &&
                    candidate.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], targetTypeSymbol));

            var ctorKind = formatterNamedType != null
                ? GetFormatterCtorKind(formatterNamedType, extendedActorSystemType)
                : FormatterCtorKind.None;

            builder.Add(new FormatterInfo(
                targetTypeFullName,
                targetTypeSymbol?.IsValueType ?? false,
                formatterTypeFullName,
                implementsInterface,
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
    /// </summary>
    private static bool ValidateSerializerShape(SourceProductionContext context, SerializerInfo serializer)
    {
        var isValid = true;

        if (!serializer.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "must be declared 'partial': the generator emits a second declaration of this class"));
            isValid = false;
        }

        if (!serializer.DerivesFromAkkaSerializerBase)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
                "must derive from Akka.Serialization.V2.AkkaSerializer: the generated members (Identifier, Manifest, Serialize, Deserialize, SizeHint) are declared as overrides of that base"));
            isValid = false;
        }

        if (serializer.IsGeneric)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidSerializerShape, Location.None, serializer.ClassName,
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
    /// </summary>
    private static bool ValidateProtocolType(SourceProductionContext context, SerializerInfo serializer)
    {
        if (serializer.ProtocolType == null || serializer.ProtocolType.TypeKind == TypeKind.Interface)
            return true;

        context.ReportDiagnostic(Diagnostic.Create(ProtocolTypeMustBeInterface, Location.None, serializer.ClassName, serializer.ProtocolTypeFullName));
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
    /// </summary>
    private static bool ValidateProtocolCoverage(SourceProductionContext context, SerializerInfo serializer, Compilation compilation)
    {
        if (serializer.ProtocolType == null)
            return true;

        var knownTypes = KnownTypes.From(compilation);
        if (knownTypes.SerializableAttribute == null)
            return true;

        var isValid = true;
        foreach (var candidate in GetSourceDeclaredTypes(compilation))
        {
            if (candidate.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                continue;

            if (candidate.IsAbstract)
                continue;

            if (!candidate.AllInterfaces.Contains(serializer.ProtocolType, SymbolEqualityComparer.Default))
                continue;

            var isMarked = candidate.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute));
            if (isMarked)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(ProtocolMessageNotSerializable, Location.None,
                GetFullyQualifiedTypeName(candidate), serializer.ProtocolTypeFullName, serializer.ClassName));
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Every named type declared in <paramref name="compilation"/>'s OWN source (never a referenced
    /// assembly: <see cref="Compilation.Assembly"/> is the assembly being compiled), recursively
    /// including nested types. Used only by <see cref="ValidateProtocolCoverage"/>, transiently,
    /// inside the terminal source-production callback -- never stored in a cached provider.
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

    private static bool ValidateFormatters(SourceProductionContext context, SerializerInfo serializer)
    {
        if (serializer.Formatters.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var formatter in serializer.Formatters)
        {
            if (!formatter.IsTargetSupported)
            {
                context.ReportDiagnostic(Diagnostic.Create(FormatterTargetNotSupported, Location.None, formatter.TargetTypeFullName, serializer.ClassName));
                isValid = false;
                continue;
            }

            if (!formatter.ImplementsInterface)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidFormatterType, Location.None, formatter.FormatterTypeFullName, serializer.ClassName, formatter.TargetTypeFullName));
                isValid = false;
                continue;
            }

            if (formatter.CtorKind == FormatterCtorKind.None)
            {
                context.ReportDiagnostic(Diagnostic.Create(FormatterConstructorNotUsable, Location.None, formatter.FormatterTypeFullName, serializer.ClassName));
                isValid = false;
            }
        }

        foreach (var duplicate in serializer.Formatters
                     .Where(formatter => formatter.IsTargetSupported)
                     .GroupBy(formatter => formatter.TargetTypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateFormatterRegistration, Location.None, serializer.ClassName, duplicate.Key));
            isValid = false;
        }

        return isValid;
    }

    private static bool ValidateClosedGenericRegistrations(SourceProductionContext context, SerializerInfo serializer)
    {
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty)
            return true;

        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations.Where(registration => registration.Message == null))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidClosedGenericRegistration, Location.None, registration.TargetDisplayName, serializer.ClassName));
            isValid = false;
        }

        foreach (var duplicate in serializer.ClosedGenericRegistrations
                     .Where(registration => registration.Message != null)
                     .GroupBy(registration => registration.TargetDisplayName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateClosedGenericRegistration, Location.None, serializer.ClassName, duplicate.Key));
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
    private static bool ValidateGenericDefinitions(SourceProductionContext context, SerializerInfo serializer, ImmutableArray<MessageInfo> genericDefinitions)
    {
        if (genericDefinitions.IsDefaultOrEmpty || serializer.ProtocolType == null)
            return true;

        var isValid = true;
        foreach (var definition in genericDefinitions)
        {
            if (!definition.Protocols.Any(protocol => SymbolEqualityComparer.Default.Equals(protocol, serializer.ProtocolType)))
                continue;

            var hasRegistration = serializer.ClosedGenericRegistrations.Any(registration =>
                registration.Message != null &&
                string.Equals(registration.Message.DefinitionFullName, definition.FullyQualifiedName, StringComparison.Ordinal));
            if (hasRegistration)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(GenericSerializableRequiresRegistration, Location.None, definition.FullyQualifiedName, serializer.ProtocolTypeFullName, serializer.ClassName));
            isValid = false;
        }

        return isValid;
    }

    private static MessageInfo? ExtractMessage(GeneratorAttributeSyntaxContext context)
    {
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
                symbol.AllInterfaces.ToImmutableArray(),
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
            var unionMembers = ExtractUnionMembers(member, knownTypes, compilation, out var hasUnionAttribute);

            // Precedence: [AkkaEnvelopePayload] always wins (matching its documented precedence over
            // formatter registrations), then [AkkaUnion], then ordinary inference.
            var mapping = isEnvelopePayload ? new TypeMapping(FieldKind.EnvelopePayload)
                : hasUnionAttribute ? new TypeMapping(FieldKind.Union)
                : MapType(member.Type, knownTypes);
            fields.Add(new FieldInfo(
                index,
                member.Name,
                member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                mapping,
                isNullable,
                unionMembers: isEnvelopePayload ? default : unionMembers));
            fieldSymbols.Add(member);
        }

        var constructionPlan = SelectConstructor(symbol, fields, fieldSymbols, compilation);

        return new MessageInfo(
            symbol.Name,
            fullyQualifiedName,
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            symbol.AllInterfaces.ToImmutableArray(),
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
        out bool hasUnionAttribute)
    {
        hasUnionAttribute = false;
        if (knownTypes.UnionAttribute == null)
            return ImmutableArray<UnionMemberInfo>.Empty;

        // Field-level override wins; otherwise inherit the type-level declaration from the field's
        // static type. OriginalDefinition covers a generic union base, where the attribute lives on
        // the definition.
        var unionAttribute = member.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
        if (unionAttribute == null && member.Type is INamedTypeSymbol fieldType)
        {
            unionAttribute = fieldType.OriginalDefinition.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
        }

        if (unionAttribute == null || unionAttribute.ConstructorArguments.Length != 1)
            return ImmutableArray<UnionMemberInfo>.Empty;

        hasUnionAttribute = true;
        var arguments = unionAttribute.ConstructorArguments[0].Values;
        var builder = ImmutableArray.CreateBuilder<UnionMemberInfo>(arguments.Length);
        foreach (var argument in arguments)
        {
            if (argument.Value is not INamedTypeSymbol memberType || memberType.IsUnboundGenericType)
            {
                var displayName = argument.Value is ITypeSymbol typeSymbol
                    ? typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : "<null>";
                builder.Add(new UnionMemberInfo(displayName, isValueType: false, isAssignable: false, isSupported: false, isSealed: false));
                continue;
            }

            builder.Add(new UnionMemberInfo(
                GetMessageDictionaryKey(memberType),
                memberType.IsValueType,
                compilation.HasImplicitConversion(memberType, member.Type),
                isSupported: true,
                isSealed: memberType.IsSealed || memberType.IsValueType));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The key a type is looked up under in the serializer's message dictionary. Non-generic types
    /// use the arity-less <see cref="GetFullyQualifiedTypeName"/> (the existing key for every
    /// <c>[AkkaSerializable]</c> message); closed generic constructions use the full display string
    /// (e.g. <c>global::Ns.Wrapper&lt;global::Ns.Foo&gt;</c>) so distinct constructions stay distinct.
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
                CollectObjectTypeNames(field.Mapping, referencedObjectTypes);

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

    /// <summary>
    /// Collects every <see cref="FieldKind.Object"/> type name reachable through a mapping, descending
    /// into collection element/key/value mappings so that a nested <c>[AkkaSerializable]</c> type used
    /// only inside a collection (for example the element of a <c>List&lt;Reading&gt;</c>) is still
    /// reached and gets its Write/Read/SizeOf methods generated.
    /// </summary>
    private static void CollectObjectTypeNames(TypeMapping mapping, HashSet<string> into)
    {
        if (mapping.Kind == FieldKind.Object)
            into.Add(mapping.TypeFullName);

        foreach (var argument in mapping.TypeArguments)
            CollectObjectTypeNames(argument, into);
    }

    private static bool ValidateMessages(SourceProductionContext context, SerializerInfo serializer, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var isValid = true;
        foreach (var message in topLevelMessages.Where(message => string.IsNullOrWhiteSpace(message.Manifest)))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingManifest, Location.None, message.FullyQualifiedName));
            isValid = false;
        }

        foreach (var duplicate in topLevelMessages
                     .Where(m => !string.IsNullOrWhiteSpace(m.Manifest))
                     .GroupBy(m => m.Manifest, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var typeNames = string.Join(", ", duplicate.Select(m => m.FullyQualifiedName));
            context.ReportDiagnostic(Diagnostic.Create(DuplicateManifest, Location.None, serializer.ClassName, duplicate.Key, typeNames));
            isValid = false;
        }

        foreach (var message in reachableMessages)
        {
            if (message.Fields.Length == 0 && !message.AllowEmpty)
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingFields, Location.None, message.FullyQualifiedName));
                isValid = false;
            }

            foreach (var duplicate in message.Fields.GroupBy(field => field.Index).Where(group => group.Count() > 1))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateFieldIndex, Location.None, message.FullyQualifiedName, duplicate.Key));
                isValid = false;
            }

            // Structural [AkkaField] problems found during extraction (static property, or a
            // getter the generated Write path could not call): these properties never made it into
            // message.Fields, so they cannot double-report through any of the checks below.
            foreach (var invalidField in message.InvalidFields)
            {
                context.ReportDiagnostic(Diagnostic.Create(FieldPropertyNotAccessible, Location.None, invalidField.PropertyName, message.FullyQualifiedName, invalidField.Reason));
                isValid = false;
            }

            // Read-side reconstruction: either no constructor could be selected, or the selected
            // constructor leaves [AkkaField] properties uncovered with no accessible setter to fall
            // back on -- both make deserialize impossible to generate.
            foreach (var error in message.ConstructionPlan.Errors)
            {
                context.ReportDiagnostic(Diagnostic.Create(NoMatchingConstructor, Location.None, message.FullyQualifiedName, error));
                isValid = false;
            }

            // Advisory only: the selected constructor still works (its defaulted parameter is simply
            // never supplied), but the parameter's value silently reverts to its default on every
            // deserialize because no [AkkaField] property feeds it.
            foreach (var parameterName in message.ConstructionPlan.UncoveredDefaultedParameters)
            {
                context.ReportDiagnostic(Diagnostic.Create(ConstructorParameterNotCovered, Location.None, parameterName, message.FullyQualifiedName));
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Unsupported))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedFieldType, Location.None, field.Name, message.FullyQualifiedName, field.TypeFullName));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.MissingSerializableDefinition))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingNestedSerializableDefinition, Location.None, field.Name, message.FullyQualifiedName, field.TypeFullName));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.UnsupportedEnumUnderlyingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnumUnderlyingType, Location.None, field.Name, message.FullyQualifiedName, field.Mapping.TypeFullName, field.Mapping.EnumUnderlyingTypeName));
                isValid = false;
            }

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
            {
                if (!ValidateUnionField(context, message, field, messagesByType))
                    isValid = false;
            }

            // An Object mapping that resolves to no known message would generate a call to a
            // nonexistent Write/Read/SizeOf method. The only way to hit this from within one
            // compilation is a closed generic [AkkaSerializable] field type whose construction was
            // never registered with [AkkaSerializable<T>] (non-generic [AkkaSerializable]
            // types are always extracted). AKKASG023 names the fix.
            foreach (var field in message.Fields)
            {
                var objectTypeNames = new HashSet<string>(StringComparer.Ordinal);
                CollectObjectTypeNames(field.Mapping, objectTypeNames);
                foreach (var objectTypeName in objectTypeNames.Where(typeName => !messagesByType.ContainsKey(typeName)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnregisteredClosedGenericField, Location.None, field.Name, message.FullyQualifiedName, objectTypeName, serializer.ClassName));
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
            var typeNames = string.Join(", ", collision.Select(m => m.FullyQualifiedName));
            context.ReportDiagnostic(Diagnostic.Create(DuplicateGeneratedName, Location.None, serializer.ClassName, collision.Key, typeNames));
            isValid = false;
        }

        return isValid;
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
        if (serializer.ClosedGenericRegistrations.IsDefaultOrEmpty || serializer.ProtocolType == null)
            return true;

        var reachableNames = new HashSet<string>(reachableMessages.Select(message => message.FullyQualifiedName), StringComparer.Ordinal);
        var isValid = true;
        foreach (var registration in serializer.ClosedGenericRegistrations)
        {
            if (registration.Message == null)
                continue;

            if (registration.Message.Protocols.Any(protocol => SymbolEqualityComparer.Default.Equals(protocol, serializer.ProtocolType)))
                continue;

            if (reachableNames.Contains(registration.Message.FullyQualifiedName))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(ClosedGenericRegistrationNotInProtocol, Location.None,
                registration.TargetDisplayName, serializer.ClassName, serializer.ProtocolTypeFullName));
            isValid = false;
        }

        return isValid;
    }

    private static bool ValidateUnionField(
        SourceProductionContext context,
        MessageInfo message,
        FieldInfo field,
        ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var isValid = true;

        if (field.UnionMembers.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidUnionMemberSet, Location.None, field.Name, message.FullyQualifiedName, "at least one member type is required"));
            return false;
        }

        foreach (var duplicate in field.UnionMembers
                     .GroupBy(member => member.TypeFullName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidUnionMemberSet, Location.None, field.Name, message.FullyQualifiedName, $"member type '{duplicate.Key}' is declared more than once"));
            isValid = false;
        }

        var manifests = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in field.UnionMembers)
        {
            if (!member.IsSupported || !messagesByType.TryGetValue(member.TypeFullName, out var memberMessage))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotSerializable, Location.None, member.TypeFullName, field.Name, message.FullyQualifiedName));
                isValid = false;
                continue;
            }

            if (!member.IsAssignable)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotAssignable, Location.None, member.TypeFullName, field.Name, message.FullyQualifiedName, field.TypeFullName));
                isValid = false;
            }

            // Advisory only (Info): an unsealed member works, but an undeclared subtype of it fails
            // at write time under exact-runtime-type dispatch -- worth surfacing, not worth failing.
            if (!member.IsSealed)
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberNotSealed, Location.None, member.TypeFullName, field.Name, message.FullyQualifiedName));

            if (string.IsNullOrWhiteSpace(memberMessage.Manifest))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnionMemberMissingManifest, Location.None, member.TypeFullName, field.Name, message.FullyQualifiedName));
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
            context.ReportDiagnostic(Diagnostic.Create(UnionMemberManifestCollision, Location.None, field.Name, message.FullyQualifiedName, collision.Key, string.Join(", ", collision.Value)));
            isValid = false;
        }

        return isValid;
    }

    private static string Generate(SerializerInfo serializer, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        var usedFormatters = CollectUsedFormatters(reachableMessages);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Buffers;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(serializer.Namespace))
        {
            sb.Append("namespace ").Append(serializer.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(GetAccessibilityKeyword(serializer.DeclaredAccessibility)).Append(" sealed partial class ").Append(serializer.ClassName).AppendLine();
        sb.AppendLine("{");
        GenerateFormatterFields(sb, usedFormatters);
        sb.Append("    public ").Append(serializer.ClassName).AppendLine("(global::Akka.Actor.ExtendedActorSystem system) : base(system)");
        sb.AppendLine("    {");
        foreach (var formatter in usedFormatters)
        {
            sb.Append("        ").Append(GetFormatterFieldName(formatter)).Append(" = new ").Append(formatter.FormatterTypeFullName).Append('(');
            if (formatter.CtorKind == FormatterCtorKind.System)
                sb.Append("system");
            sb.AppendLine(");");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public override int Identifier => ").Append(serializer.SerializerId).AppendLine(";");
        sb.AppendLine();
        GenerateRegistration(sb, serializer);
        GenerateManifest(sb, topLevelMessages);
        GenerateSerialize(sb, topLevelMessages);
        GenerateSerializeDirect(sb, topLevelMessages);
        GenerateDeserialize(sb, topLevelMessages);
        GenerateSizeHint(sb, topLevelMessages);
        GenerateCountingBufferWriter(sb);

        var unionHelpers = PlanUnionHelpers(reachableMessages);
        foreach (var message in reachableMessages)
        {
            GenerateSizeMessage(sb, message, unionHelpers);
            GenerateWriteMessage(sb, message, unionHelpers);
            GenerateReadMessage(sb, message, unionHelpers);
        }

        GenerateUnionHelpers(sb, unionHelpers, messagesByType);

        sb.AppendLine("}");
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

    private static void GenerateFormatterFields(StringBuilder sb, ImmutableArray<FormatterInfo> usedFormatters)
    {
        if (usedFormatters.Length == 0)
            return;

        foreach (var formatter in usedFormatters)
            sb.Append("    private readonly ").Append(formatter.FormatterTypeFullName).Append(' ').Append(GetFormatterFieldName(formatter)).AppendLine(";");

        sb.AppendLine();
    }

    private static void GenerateRegistration(StringBuilder sb, SerializerInfo serializer)
    {
        sb.AppendLine("    public static partial global::Akka.Serialization.V2.SerializerRegistration CreateRegistration()");
        sb.AppendLine("    {");
        sb.Append("        return global::Akka.Serialization.V2.SerializerRegistration.Create(\"")
            .Append(Escape(serializer.Name)).AppendLine("\",");
        sb.Append("            system => new ").Append(serializer.ClassName).AppendLine("(system),");
        sb.AppendLine("            global::System.Collections.Immutable.ImmutableHashSet.Create<global::System.Type>(");
        sb.Append("                typeof(").Append(serializer.ProtocolTypeFullName).AppendLine("))); ");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateManifest(StringBuilder sb, ImmutableArray<MessageInfo> messages)
    {
        sb.AppendLine("    public override string Manifest(object obj)");
        sb.AppendLine("    {");
        sb.AppendLine("        return obj switch");
        sb.AppendLine("        {");
        foreach (var message in messages)
            sb.Append("            ").Append(message.FullyQualifiedName).Append(" => \"").Append(Escape(message.Manifest)).AppendLine("\",");
        sb.AppendLine("            _ => throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj))");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSerialize(StringBuilder sb, ImmutableArray<MessageInfo> messages)
    {
        sb.AppendLine("    public override int Serialize(object obj, IBufferWriter<byte> writer)");
        sb.AppendLine("    {");
        sb.AppendLine("        var countingWriter = new AkkaGeneratedCountingBufferWriter(writer);");
        sb.AppendLine("        var messagePackWriter = new global::MessagePack.MessagePackWriter(countingWriter);");
        sb.AppendLine("        SerializeMessagePack(obj, ref messagePackWriter);");
        sb.AppendLine("        messagePackWriter.Flush();");
        sb.AppendLine("        return checked((int)countingWriter.BytesWritten);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSerializeDirect(StringBuilder sb, ImmutableArray<MessageInfo> messages)
    {
        sb.AppendLine("    private void SerializeMessagePack(object obj, ref global::MessagePack.MessagePackWriter writer)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (obj)");
        sb.AppendLine("        {");
        foreach (var message in messages)
        {
            sb.Append("            case ").Append(message.FullyQualifiedName).AppendLine(" message:");
            sb.Append("                Write").Append(GetMessageMethodName(message)).AppendLine("(ref writer, message);");
            sb.AppendLine("                break;");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateDeserialize(StringBuilder sb, ImmutableArray<MessageInfo> messages)
    {
        sb.AppendLine("    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)");
        sb.AppendLine("    {");
        sb.AppendLine("        var reader = new global::MessagePack.MessagePackReader(bytes);");
        sb.AppendLine("        return manifest switch");
        sb.AppendLine("        {");
        foreach (var message in messages)
            sb.Append("            \"").Append(Escape(message.Manifest)).Append("\" => Read").Append(GetMessageMethodName(message)).AppendLine("(ref reader),");
        sb.AppendLine("            _ => throw new global::System.Runtime.Serialization.SerializationException($\"Unknown generated serializer manifest [{manifest}] for serializer [{GetType()}].\")");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSizeHint(StringBuilder sb, ImmutableArray<MessageInfo> messages)
    {
        sb.AppendLine("    public override int SizeHint(object obj)");
        sb.AppendLine("    {");
        sb.AppendLine("        return obj switch");
        sb.AppendLine("        {");
        foreach (var message in messages)
            sb.Append("            ").Append(message.FullyQualifiedName).Append(" message => SizeOf").Append(GetMessageMethodName(message)).AppendLine("(message),");
        sb.AppendLine("            _ => global::Akka.Serialization.SerializerV2.UnknownSize");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateCountingBufferWriter(StringBuilder sb)
    {
        sb.AppendLine("    private sealed class AkkaGeneratedCountingBufferWriter : global::System.Buffers.IBufferWriter<byte>");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Buffers.IBufferWriter<byte> _inner;");
        sb.AppendLine();
        sb.AppendLine("        public AkkaGeneratedCountingBufferWriter(global::System.Buffers.IBufferWriter<byte> inner)");
        sb.AppendLine("        {");
        sb.AppendLine("            _inner = inner;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public long BytesWritten { get; private set; }");
        sb.AppendLine();
        sb.AppendLine("        public void Advance(int count)");
        sb.AppendLine("        {");
        sb.AppendLine("            _inner.Advance(count);");
        sb.AppendLine("            BytesWritten += count;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::System.Memory<byte> GetMemory(int sizeHint = 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return _inner.GetMemory(sizeHint);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::System.Span<byte> GetSpan(int sizeHint = 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return _inner.GetSpan(sizeHint);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSizeMessage(StringBuilder sb, MessageInfo message, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers)
    {
        sb.Append("    private int SizeOf").Append(GetMessageMethodName(message))
            .Append('(').Append(message.FullyQualifiedName).AppendLine(" message)");
        sb.AppendLine("    {");
        sb.AppendLine("        checked");
        sb.AppendLine("        {");
        sb.Append("            var size = SizeOfMapHeader(").Append(message.Fields.Length).AppendLine(");");
        var alloc = new NameAlloc();
        foreach (var field in message.Fields)
            GenerateSizeField(sb, unionHelpers, field, alloc);
        sb.AppendLine("            return size;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSizeField(StringBuilder sb, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var value = "message." + field.Name;
        var localName = ToCamelCase(field.Name) + "Size";
        sb.Append("            size += SizeOfInt32(").Append(field.Index).AppendLine(");");
        if (IsCollectionKind(field.Mapping.Kind))
        {
            var fieldSize = alloc.Next("size");
            EmitSizeCollectionBody(sb, field.Mapping, value, fieldSize, "            ", alloc);
            sb.Append("            size += ").Append(fieldSize).AppendLine(";");
            return;
        }

        if (TryGetInlineSizeExpression(field, value, out var expression))
        {
            sb.Append("            size += ").Append(expression).AppendLine(";");
            return;
        }

        sb.Append("            var ").Append(localName).Append(" = ");
        GenerateSizeExpression(sb, unionHelpers, field, value);
        sb.AppendLine(";");
        sb.Append("            if (").Append(localName).AppendLine(" < 0)");
        sb.AppendLine("                return global::Akka.Serialization.SerializerV2.UnknownSize;");
        sb.Append("            size += ").Append(localName).AppendLine(";");
    }

    private static bool TryGetInlineSizeExpression(FieldInfo field, string value, out string expression)
    {
        // Object, EnvelopePayload, and Union always route through the general
        // GenerateSizeExpression path below (they call a generated SizeOfXxx/SizeOfEnvelopePayload/
        // SizeOfUnion method, not a scalar MessagePackSizes helper) -- including when the field is a
        // nullable [AkkaSerializable] struct, which would otherwise match IsNullableValueField below
        // and get an inline scalar expression that GetScalarSizeExpression cannot produce for
        // FieldKind.Object. Union sizes can also be UnknownSize and need the < 0 guard.
        if (field.Mapping.Kind is FieldKind.Formatted or FieldKind.Object or FieldKind.EnvelopePayload or FieldKind.Union)
        {
            expression = string.Empty;
            return false;
        }

        if (IsNullableValueField(field))
        {
            expression = value + " is null ? SizeOfNil() : " + GetScalarSizeExpression(field.Mapping, value + ".Value");
            return true;
        }

        expression = GetScalarSizeExpression(field.Mapping, value);
        return true;
    }

    private static void GenerateSizeExpression(StringBuilder sb, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, string value)
    {
        switch (field.Mapping.Kind)
        {
            case FieldKind.EnvelopePayload:
                sb.Append("SizeOfEnvelopePayload(").Append(value).Append(')');
                break;
            case FieldKind.Union when field.IsNullable:
                sb.Append(value).Append(" is null ? SizeOfNil() : SizeOf").Append(unionHelpers[BuildUnionSignature(field)].HelperName).Append('(').Append(value).Append(')');
                break;
            case FieldKind.Union:
                sb.Append("SizeOf").Append(unionHelpers[BuildUnionSignature(field)].HelperName).Append('(').Append(value).Append(')');
                break;
            case FieldKind.Object when IsNullableValueField(field):
                sb.Append(value).Append(" is null ? SizeOfNil() : SizeOf").Append(GetObjectMethodName(field.Mapping)).Append('(').Append(value).Append(".Value)");
                break;
            case FieldKind.Object when field.IsNullable:
                sb.Append(value).Append(" is null ? SizeOfNil() : SizeOf").Append(GetObjectMethodName(field.Mapping)).Append('(').Append(value).Append(')');
                break;
            case FieldKind.Object:
                sb.Append("SizeOf").Append(GetObjectMethodName(field.Mapping)).Append('(').Append(value).Append(')');
                break;
            case FieldKind.Formatted when IsNullableValueField(field):
                sb.Append(value).Append(" is null ? SizeOfNil() : ").Append(GetFormatterFieldName(field.Formatter!)).Append(".SizeOf(").Append(value).Append(".Value)");
                break;
            case FieldKind.Formatted when field.IsNullable:
                sb.Append(value).Append(" is null ? SizeOfNil() : ").Append(GetFormatterFieldName(field.Formatter!)).Append(".SizeOf(").Append(value).Append(')');
                break;
            case FieldKind.Formatted:
                sb.Append(GetFormatterFieldName(field.Formatter!)).Append(".SizeOf(").Append(value).Append(')');
                break;
            default:
                sb.Append(GetScalarSizeExpression(field.Mapping, value));
                break;
        }
    }

    private static string GetScalarSizeExpression(TypeMapping mapping, string value)
    {
        return mapping.Kind switch
        {
            FieldKind.String => "SizeOfString(" + value + ")",
            FieldKind.ByteArray => "SizeOfBytes(" + value + ")",
            FieldKind.Int32 => "SizeOfInt32(" + value + ")",
            FieldKind.Int64 => "SizeOfInt64(" + value + ")",
            FieldKind.Boolean => "SizeOfBoolean(" + value + ")",
            FieldKind.Double => "SizeOfDouble(" + value + ")",
            FieldKind.Decimal => "SizeOfDecimal(" + value + ")",
            FieldKind.Guid => "SizeOfGuid(" + value + ")",
            FieldKind.DateTime => "SizeOfDateTime(" + value + ")",
            FieldKind.DateTimeOffset => "SizeOfDateTimeOffset(" + value + ")",
            FieldKind.ActorRef => "SizeOfActorRef(" + value + ")",
            FieldKind.Enum => "SizeOfEnum((int)" + value + ")",
            _ => "global::Akka.Serialization.SerializerV2.UnknownSize"
        };
    }

    private static void GenerateWriteMessage(StringBuilder sb, MessageInfo message, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers)
    {
        sb.Append("    private void Write").Append(GetMessageMethodName(message))
            .Append("(ref global::MessagePack.MessagePackWriter writer, ").Append(message.FullyQualifiedName).AppendLine(" message)");
        sb.AppendLine("    {");
        sb.Append("        writer.WriteMapHeader(").Append(message.Fields.Length).AppendLine(");");
        var alloc = new NameAlloc();
        foreach (var field in message.Fields)
            GenerateWriteField(sb, unionHelpers, field, alloc);
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateReadMessage(StringBuilder sb, MessageInfo message, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers)
    {
        sb.Append("    private ").Append(message.FullyQualifiedName).Append(" Read").Append(GetMessageMethodName(message))
            .AppendLine("(ref global::MessagePack.MessagePackReader reader)");
        sb.AppendLine("    {");
        // Generator-owned locals are prefixed "__" so they cannot collide with a per-field local
        // (ToCamelCase(field.Name)/GetHasLocalName below), no matter what the [AkkaField] property
        // is named -- including adversarial names like "FieldCount" or "EntryIndex" that would
        // otherwise camel-case straight into these identifiers (CS0128/CS0136).
        sb.AppendLine("        var __fieldCount = reader.ReadMapHeader();");
        var alloc = new NameAlloc();
        foreach (var field in message.Fields)
        {
            sb.Append("        ").Append(GetLocalType(field)).Append(' ').Append(ToCamelCase(field.Name)).Append(" = ")
                .Append(DefaultValue(field)).AppendLine(";");
            if (IsRequired(field))
                sb.Append("        var ").Append(GetHasLocalName(field)).AppendLine(" = false;");
        }

        sb.AppendLine("        for (var __entryIndex = 0; __entryIndex < __fieldCount; __entryIndex++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var __fieldId = reader.ReadInt32();");
        sb.AppendLine("            switch (__fieldId)");
        sb.AppendLine("            {");
        foreach (var field in message.Fields)
        {
            sb.Append("                case ").Append(field.Index).AppendLine(":");
            GenerateReadField(sb, unionHelpers, field, alloc);
            if (IsRequired(field))
                sb.Append("                    ").Append(GetHasLocalName(field)).AppendLine(" = true;");
            sb.AppendLine("                    break;");
        }
        sb.AppendLine("                default:");
        sb.AppendLine("                    reader.Skip();");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        foreach (var field in message.Fields.Where(IsRequired))
        {
            var target = ToCamelCase(field.Name);
            sb.Append("        if (!").Append(GetHasLocalName(field));
            if (IsReferenceLike(field))
                sb.Append(" || ").Append(target).Append(" is null");
            sb.AppendLine(")");
            sb.Append("            throw new global::System.Runtime.Serialization.SerializationException(\"Missing required field [")
                .Append(Escape(field.Name)).Append("] with index [").Append(field.Index).Append("] while deserializing [")
                .Append(Escape(message.FullyQualifiedName)).AppendLine("].\");");
        }

        GenerateReadMessageConstruction(sb, message);
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the final <c>return new T(...)</c> of a read method from <see cref="MessageInfo.ConstructionPlan"/>:
    /// NAMED arguments (escaped where the parameter name is a C# keyword, e.g. <c>@event:</c>) for
    /// every constructor-mapped [AkkaField] property, followed by an object initializer for whatever
    /// is left over. The plan stores field NAMES rather than <see cref="FieldInfo"/> references so it
    /// stays correct across <see cref="MessageInfo.WithFields"/> (formatter resolution can replace a
    /// field's mapping without touching its name).
    /// </summary>
    private static void GenerateReadMessageConstruction(StringBuilder sb, MessageInfo message)
    {
        var fieldsByName = message.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var plan = message.ConstructionPlan;

        sb.Append("        return new ").Append(message.FullyQualifiedName).Append('(');
        sb.Append(string.Join(", ", plan.Arguments.Select(argument =>
            EscapeIfKeyword(argument.ParameterName) + ": " + GetFieldValueExpression(fieldsByName[argument.FieldName]))));
        sb.Append(')');

        if (plan.InitializerFieldNames.Length > 0)
        {
            sb.Append(" { ");
            sb.Append(string.Join(", ", plan.InitializerFieldNames.Select(name =>
            {
                var field = fieldsByName[name];
                return EscapeIfKeyword(field.Name) + " = " + GetFieldValueExpression(field);
            })));
            sb.Append(" }");
        }

        sb.AppendLine(";");
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
        StringBuilder sb,
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

            GenerateUnionWrite(sb, field, plan.HelperName, members);
            GenerateUnionRead(sb, field, plan.HelperName, members);
            GenerateUnionSize(sb, field, plan.HelperName, members);
        }
    }

    private static void GenerateUnionWrite(StringBuilder sb, FieldInfo field, string helperName, ImmutableArray<(UnionMemberInfo Member, MessageInfo Message)> members)
    {
        sb.Append("    private void Write").Append(helperName)
            .Append("(ref global::MessagePack.MessagePackWriter writer, ").Append(field.TypeFullName).AppendLine(" value)");
        sb.AppendLine("    {");
        sb.AppendLine("        var runtimeType = value.GetType();");
        foreach (var (member, memberMessage) in members)
        {
            sb.Append("        if (runtimeType == typeof(").Append(member.TypeFullName).AppendLine("))");
            sb.AppendLine("        {");
            sb.AppendLine("            writer.WriteMapHeader(2);");
            sb.AppendLine("            writer.Write(1);");
            sb.Append("            writer.Write(\"").Append(Escape(memberMessage.Manifest)).AppendLine("\");");
            sb.AppendLine("            writer.Write(2);");
            sb.Append("            Write").Append(GetMessageMethodName(memberMessage)).Append("(ref writer, (").Append(member.TypeFullName).AppendLine(")value);");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.Append("        throw new global::System.Runtime.Serialization.SerializationException($\"Type [{runtimeType}] is not a declared union member for union [")
            .Append(Escape(field.TypeFullName)).AppendLine("].\");");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateUnionRead(StringBuilder sb, FieldInfo field, string helperName, ImmutableArray<(UnionMemberInfo Member, MessageInfo Message)> members)
    {
        sb.Append("    private ").Append(field.TypeFullName).Append(" Read").Append(helperName)
            .AppendLine("(ref global::MessagePack.MessagePackReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fieldCount = reader.ReadMapHeader();");
        sb.AppendLine("        string? manifest = null;");
        sb.Append("        ").Append(field.TypeFullName).AppendLine("? result = default;");
        sb.AppendLine("        var hasPayload = false;");
        sb.AppendLine("        for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var fieldId = reader.ReadInt32();");
        sb.AppendLine("            switch (fieldId)");
        sb.AppendLine("            {");
        sb.AppendLine("                case 1:");
        sb.AppendLine("                    manifest = reader.ReadString();");
        sb.AppendLine("                    break;");
        sb.AppendLine("                case 2:");
        sb.AppendLine("                    switch (manifest)");
        sb.AppendLine("                    {");
        foreach (var (_, memberMessage) in members)
        {
            sb.Append("                        case \"").Append(Escape(memberMessage.Manifest)).AppendLine("\":");
            sb.Append("                            result = Read").Append(GetMessageMethodName(memberMessage)).AppendLine("(ref reader);");
            sb.AppendLine("                            break;");
        }

        sb.AppendLine("                        case null:");
        sb.Append("                            throw new global::System.Runtime.Serialization.SerializationException(\"Union manifest must precede the payload for union [")
            .Append(Escape(field.TypeFullName)).AppendLine("].\");");
        sb.AppendLine("                        default:");
        sb.Append("                            throw new global::System.Runtime.Serialization.SerializationException($\"Unknown union manifest [{manifest}] for union [")
            .Append(Escape(field.TypeFullName)).AppendLine("].\");");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine("                    hasPayload = true;");
        sb.AppendLine("                    break;");
        sb.AppendLine("                default:");
        sb.AppendLine("                    reader.Skip();");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (!hasPayload || result is null)");
        sb.Append("            throw new global::System.Runtime.Serialization.SerializationException(\"Missing union payload for union [")
            .Append(Escape(field.TypeFullName)).AppendLine("].\");");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateUnionSize(StringBuilder sb, FieldInfo field, string helperName, ImmutableArray<(UnionMemberInfo Member, MessageInfo Message)> members)
    {
        sb.Append("    private int SizeOf").Append(helperName)
            .Append('(').Append(field.TypeFullName).AppendLine(" value)");
        sb.AppendLine("    {");
        sb.AppendLine("        var runtimeType = value.GetType();");
        foreach (var (member, memberMessage) in members)
        {
            sb.Append("        if (runtimeType == typeof(").Append(member.TypeFullName).AppendLine("))");
            sb.AppendLine("        {");
            sb.Append("            var payloadSize = SizeOf").Append(GetMessageMethodName(memberMessage)).Append("((").Append(member.TypeFullName).AppendLine(")value);");
            sb.AppendLine("            if (payloadSize < 0)");
            sb.AppendLine("                return global::Akka.Serialization.SerializerV2.UnknownSize;");
            sb.Append("            return checked(SizeOfMapHeader(2) + SizeOfInt32(1) + SizeOfString(\"").Append(Escape(memberMessage.Manifest))
                .AppendLine("\") + SizeOfInt32(2) + payloadSize);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("        return global::Akka.Serialization.SerializerV2.UnknownSize;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateWriteField(StringBuilder sb, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var value = "message." + field.Name;
        sb.Append("        writer.Write(").Append(field.Index).AppendLine(");");
        if (IsNullableValueField(field))
        {
            sb.Append("        if (").Append(value).AppendLine(" is null)");
            sb.AppendLine("            writer.WriteNil();");
            sb.AppendLine("        else");
            GenerateWriteFieldValue(sb, unionHelpers, field, value + ".Value", "            ", alloc);
            return;
        }

        GenerateWriteFieldValue(sb, unionHelpers, field, value, "        ", alloc);
    }

    private static void GenerateWriteFieldValue(StringBuilder sb, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, string value, string indent, NameAlloc alloc)
    {
        if (IsCollectionKind(field.Mapping.Kind))
        {
            EmitWriteCollectionBody(sb, field.Mapping, value, indent, alloc);
            return;
        }

        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.ByteArray:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.Int32:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.Int64:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.Boolean:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.Double:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.Decimal:
                sb.Append(indent).Append("WriteDecimal(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.Guid:
                sb.Append(indent).Append("WriteGuid(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.DateTime:
                sb.Append(indent).Append("WriteDateTime(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.DateTimeOffset:
                sb.Append(indent).Append("WriteDateTimeOffset(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.ActorRef:
                sb.Append(indent).Append("WriteActorRef(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.EnvelopePayload:
                sb.Append(indent).Append("WriteEnvelopePayload(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.Enum:
                sb.Append(indent).Append("writer.Write((int)").Append(value).AppendLine(");");
                break;
            case FieldKind.Object:
                // Mirrors FieldKind.Formatted below: when the nested type is a value type, a
                // nullable field was already unwrapped to its non-nullable .Value by the caller
                // (GenerateWriteField's IsNullableValueField branch), so no further null-check is
                // possible (or needed) here -- only a genuinely nullable REFERENCE nested type
                // needs the runtime "is null" guard.
                if (field.IsNullable && IsReferenceLike(field))
                {
                    sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
                    sb.Append(indent).AppendLine("    writer.WriteNil();");
                    sb.Append(indent).AppendLine("else");
                    sb.Append(indent).Append("    Write").Append(GetObjectMethodName(field.Mapping)).Append("(ref writer, ").Append(value).AppendLine(");");
                }
                else
                {
                    sb.Append(indent).Append("Write").Append(GetObjectMethodName(field.Mapping)).Append("(ref writer, ").Append(value).AppendLine(");");
                }
                break;
            case FieldKind.Formatted:
                if (field.IsNullable && IsReferenceLike(field))
                {
                    sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
                    sb.Append(indent).AppendLine("    writer.WriteNil();");
                    sb.Append(indent).AppendLine("else");
                    sb.Append(indent).Append("    ").Append(GetFormatterFieldName(field.Formatter!)).Append(".Write(ref writer, ").Append(value).AppendLine(");");
                }
                else
                {
                    sb.Append(indent).Append(GetFormatterFieldName(field.Formatter!)).Append(".Write(ref writer, ").Append(value).AppendLine(");");
                }
                break;
            case FieldKind.Union:
                // Union fields are always reference-like (the static type is an interface or
                // abstract base), so only the nullable-reference guard is needed here.
                if (field.IsNullable)
                {
                    sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
                    sb.Append(indent).AppendLine("    writer.WriteNil();");
                    sb.Append(indent).AppendLine("else");
                    sb.Append(indent).Append("    Write").Append(unionHelpers[BuildUnionSignature(field)].HelperName).Append("(ref writer, ").Append(value).AppendLine(");");
                }
                else
                {
                    sb.Append(indent).Append("Write").Append(unionHelpers[BuildUnionSignature(field)].HelperName).Append("(ref writer, ").Append(value).AppendLine(");");
                }
                break;
        }
    }

    private static void GenerateReadField(StringBuilder sb, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, NameAlloc alloc)
    {
        var target = ToCamelCase(field.Name);

        // Collection fields own their MessagePack nil handling end-to-end (EmitReadCollectionBody
        // does its own TryReadNil), so they are read directly regardless of the field's nullability:
        // a nil-on-the-wire assigns null, and the post-loop required-field guard rejects a null in a
        // non-nullable collection slot exactly as it does for any other non-nullable reference field.
        if (IsCollectionKind(field.Mapping.Kind))
        {
            GenerateReadFieldValue(sb, unionHelpers, field, target, "                    ", alloc);
            return;
        }

        if (IsNullableValueField(field))
        {
            sb.AppendLine("                    if (reader.TryReadNil())");
            sb.Append("                        ").Append(target).AppendLine(" = null;");
            sb.AppendLine("                    else");
            GenerateReadFieldValue(sb, unionHelpers, field, target, "                        ", alloc);
            return;
        }

        var isNullableReferenceLikeSlot = field.Mapping.Kind == FieldKind.EnvelopePayload
            || field.Mapping.Kind == FieldKind.Union
            || (field.Mapping.Kind == FieldKind.Object && IsReferenceLike(field))
            || (field.Mapping.Kind == FieldKind.Formatted && IsReferenceLike(field));

        if (isNullableReferenceLikeSlot && field.IsNullable)
        {
            sb.AppendLine("                    if (reader.TryReadNil())");
            sb.Append("                        ").Append(target).AppendLine(" = null;");
            sb.AppendLine("                    else");
            GenerateReadFieldValue(sb, unionHelpers, field, target, "                        ", alloc);
            return;
        }

        GenerateReadFieldValue(sb, unionHelpers, field, target, "                    ", alloc);
    }

    private static void GenerateReadFieldValue(StringBuilder sb, ImmutableDictionary<string, (string HelperName, FieldInfo Field)> unionHelpers, FieldInfo field, string target, string indent, NameAlloc alloc)
    {
        if (IsCollectionKind(field.Mapping.Kind))
        {
            EmitReadCollectionBody(sb, field.Mapping, target, indent, alloc);
            return;
        }

        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                sb.Append(indent).Append(target).AppendLine(" = reader.ReadString();");
                break;
            case FieldKind.ByteArray:
                sb.Append(indent).Append("var ").Append(target).AppendLine("Bytes = reader.ReadBytes();");
                sb.Append(indent).Append(target).Append(" = ").Append(target).AppendLine("Bytes?.ToArray();");
                break;
            case FieldKind.Int32:
                sb.Append(indent).Append(target).AppendLine(" = reader.ReadInt32();");
                break;
            case FieldKind.Int64:
                sb.Append(indent).Append(target).AppendLine(" = reader.ReadInt64();");
                break;
            case FieldKind.Boolean:
                sb.Append(indent).Append(target).AppendLine(" = reader.ReadBoolean();");
                break;
            case FieldKind.Double:
                sb.Append(indent).Append(target).AppendLine(" = reader.ReadDouble();");
                break;
            case FieldKind.Decimal:
                sb.Append(indent).Append(target).AppendLine(" = ReadDecimal(ref reader);");
                break;
            case FieldKind.Guid:
                sb.Append(indent).Append(target).AppendLine(" = ReadGuid(ref reader);");
                break;
            case FieldKind.DateTime:
                sb.Append(indent).Append(target).AppendLine(" = ReadDateTime(ref reader);");
                break;
            case FieldKind.DateTimeOffset:
                sb.Append(indent).Append(target).AppendLine(" = ReadDateTimeOffset(ref reader);");
                break;
            case FieldKind.ActorRef:
                sb.Append(indent).Append(target).AppendLine(" = ReadActorRef(ref reader);");
                break;
            case FieldKind.EnvelopePayload:
                sb.Append(indent).Append(target).Append(" = ReadEnvelopePayload<").Append(field.TypeFullName).AppendLine(">(ref reader);");
                break;
            case FieldKind.Enum:
                sb.Append(indent).Append(target).Append(" = (").Append(field.Mapping.TypeFullName).AppendLine(")reader.ReadInt32();");
                break;
            case FieldKind.Object:
                sb.Append(indent).Append(target).Append(" = Read").Append(GetObjectMethodName(field.Mapping)).AppendLine("(ref reader);");
                break;
            case FieldKind.Formatted:
                sb.Append(indent).Append(target).Append(" = ").Append(GetFormatterFieldName(field.Formatter!)).AppendLine(".Read(ref reader);");
                break;
            case FieldKind.Union:
                sb.Append(indent).Append(target).Append(" = Read").Append(unionHelpers[BuildUnionSignature(field)].HelperName).AppendLine("(ref reader);");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Native collection emission (T[], List<T>, IReadOnlyList<T>, Dictionary<TKey, TValue>).
    //
    // Collections encode as MessagePack array/map framing wrapped around per-element encodings that
    // reuse the same scalar/object primitives as ordinary fields, and compose recursively so nested
    // collections (List<List<int>>, Dictionary<string, List<Reading>>) work with no special cases.
    // null encodes as MessagePack nil; empty encodes as a zero-length array/map header. The two are
    // distinct on the wire and round-trip as distinct values. This framing is permanent wire format --
    // see the encoding matrix in the PR body for the full table.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Allocates collision-free local names within a single generated method body.</summary>
    private sealed class NameAlloc
    {
        private int _counter;

        public string Next(string hint) => "__" + hint + _counter++;
    }

    private static string CollectionCountMember(FieldKind kind) => kind == FieldKind.Array ? "Length" : "Count";

    /// <summary>
    /// Whether a value of this element mapping is stored as a reference in its strongly-typed collection
    /// slot. Reference elements are declared as nullable read temporaries and stored with the
    /// null-forgiving operator (a runtime no-op) so the generated code stays warning-clean under
    /// <c>#nullable enable</c> while still round-tripping a genuine null element.
    /// </summary>
    private static bool ElementIsReference(TypeMapping mapping)
    {
        if (IsCollectionKind(mapping.Kind))
            return true;

        return mapping.Kind switch
        {
            FieldKind.String or FieldKind.ByteArray or FieldKind.ActorRef => true,
            FieldKind.Object => !mapping.IsValueType,
            _ => false
        };
    }

    private static string ElementStore(TypeMapping mapping, string valueExpr)
        => ElementIsReference(mapping) ? valueExpr + "!" : valueExpr;

    private static string ElementTempType(TypeMapping mapping)
        => ElementIsReference(mapping) ? mapping.DeclaredTypeName + "?" : mapping.DeclaredTypeName;

    private static bool IsScalarValueKind(FieldKind kind)
        => kind is FieldKind.Int32 or FieldKind.Int64 or FieldKind.Boolean or FieldKind.Double
            or FieldKind.Decimal or FieldKind.Guid or FieldKind.DateTime or FieldKind.DateTimeOffset or FieldKind.Enum;

    // ----- WRITE -----

    private static void EmitWriteCollectionBody(StringBuilder sb, TypeMapping mapping, string value, string indent, NameAlloc alloc)
    {
        sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    writer.WriteNil();");
        sb.Append(indent).AppendLine("}");
        sb.Append(indent).AppendLine("else");
        sb.Append(indent).AppendLine("{");

        var bodyIndent = indent + "    ";
        var loopIndent = bodyIndent + "    ";
        if (mapping.Kind == FieldKind.Dictionary)
        {
            var kvp = alloc.Next("kvp");
            sb.Append(bodyIndent).Append("writer.WriteMapHeader(").Append(value).AppendLine(".Count);");
            sb.Append(bodyIndent).Append("foreach (var ").Append(kvp).Append(" in ").Append(value).AppendLine(")");
            sb.Append(bodyIndent).AppendLine("{");
            EmitWriteElement(sb, mapping.TypeArguments[0], kvp + ".Key", loopIndent, alloc);
            EmitWriteElement(sb, mapping.TypeArguments[1], kvp + ".Value", loopIndent, alloc);
            sb.Append(bodyIndent).AppendLine("}");
        }
        else
        {
            var item = alloc.Next("item");
            sb.Append(bodyIndent).Append("writer.WriteArrayHeader(").Append(value).Append('.').Append(CollectionCountMember(mapping.Kind)).AppendLine(");");
            sb.Append(bodyIndent).Append("foreach (var ").Append(item).Append(" in ").Append(value).AppendLine(")");
            sb.Append(bodyIndent).AppendLine("{");
            EmitWriteElement(sb, mapping.TypeArguments[0], item, loopIndent, alloc);
            sb.Append(bodyIndent).AppendLine("}");
        }

        sb.Append(indent).AppendLine("}");
    }

    private static void EmitWriteElement(StringBuilder sb, TypeMapping mapping, string value, string indent, NameAlloc alloc)
    {
        if (IsCollectionKind(mapping.Kind))
        {
            EmitWriteCollectionBody(sb, mapping, value, indent, alloc);
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                sb.Append(indent).Append("Write").Append(GetObjectMethodName(mapping)).Append("(ref writer, ").Append(value).AppendLine(");");
                return;
            }

            var writeValue = mapping.IsValueType ? value + ".Value" : value;
            sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
            sb.Append(indent).AppendLine("    writer.WriteNil();");
            sb.Append(indent).AppendLine("else");
            sb.Append(indent).Append("    Write").Append(GetObjectMethodName(mapping)).Append("(ref writer, ").Append(writeValue).AppendLine(");");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
            sb.Append(indent).AppendLine("    writer.WriteNil();");
            sb.Append(indent).AppendLine("else");
            EmitScalarWrite(sb, mapping, value + ".Value", indent + "    ");
            return;
        }

        EmitScalarWrite(sb, mapping, value, indent);
    }

    private static void EmitScalarWrite(StringBuilder sb, TypeMapping mapping, string value, string indent)
    {
        switch (mapping.Kind)
        {
            case FieldKind.String:
            case FieldKind.ByteArray:
            case FieldKind.Int32:
            case FieldKind.Int64:
            case FieldKind.Boolean:
            case FieldKind.Double:
                sb.Append(indent).Append("writer.Write(").Append(value).AppendLine(");");
                break;
            case FieldKind.Decimal:
                sb.Append(indent).Append("WriteDecimal(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.Guid:
                sb.Append(indent).Append("WriteGuid(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.DateTime:
                sb.Append(indent).Append("WriteDateTime(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.DateTimeOffset:
                sb.Append(indent).Append("WriteDateTimeOffset(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.ActorRef:
                sb.Append(indent).Append("WriteActorRef(ref writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.Enum:
                sb.Append(indent).Append("writer.Write((int)").Append(value).AppendLine(");");
                break;
        }
    }

    // ----- READ -----

    private static void EmitReadCollectionBody(StringBuilder sb, TypeMapping mapping, string target, string indent, NameAlloc alloc)
    {
        sb.Append(indent).AppendLine("if (reader.TryReadNil())");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).Append("    ").Append(target).AppendLine(" = null;");
        sb.Append(indent).AppendLine("}");
        sb.Append(indent).AppendLine("else");
        sb.Append(indent).AppendLine("{");

        var bodyIndent = indent + "    ";
        var loopIndent = bodyIndent + "    ";
        var length = alloc.Next("len");
        var collection = alloc.Next("col");
        var index = alloc.Next("i");

        if (mapping.Kind == FieldKind.Dictionary)
        {
            var key = mapping.TypeArguments[0];
            var val = mapping.TypeArguments[1];
            var keyVar = alloc.Next("key");
            var valVar = alloc.Next("val");
            sb.Append(bodyIndent).Append("var ").Append(length).AppendLine(" = reader.ReadMapHeader();");
            sb.Append(bodyIndent).Append("var ").Append(collection).Append(" = new global::System.Collections.Generic.Dictionary<")
                .Append(key.DeclaredTypeName).Append(", ").Append(val.DeclaredTypeName).Append(">(").Append(length).AppendLine(");");
            sb.Append(bodyIndent).Append("for (var ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(length).Append("; ").Append(index).AppendLine("++)");
            sb.Append(bodyIndent).AppendLine("{");
            EmitReadElement(sb, key, keyVar, loopIndent, alloc);
            EmitReadElement(sb, val, valVar, loopIndent, alloc);
            sb.Append(loopIndent).Append(collection).Append('[').Append(ElementStore(key, keyVar)).Append("] = ").Append(ElementStore(val, valVar)).AppendLine(";");
            sb.Append(bodyIndent).AppendLine("}");
        }
        else
        {
            var element = mapping.TypeArguments[0];
            var itemVar = alloc.Next("item");
            sb.Append(bodyIndent).Append("var ").Append(length).AppendLine(" = reader.ReadArrayHeader();");
            if (mapping.Kind == FieldKind.Array)
                sb.Append(bodyIndent).Append("var ").Append(collection).Append(" = ").Append(GetArrayAllocationExpression(element.DeclaredTypeName, length)).AppendLine(";");
            else
                sb.Append(bodyIndent).Append("var ").Append(collection).Append(" = new global::System.Collections.Generic.List<").Append(element.DeclaredTypeName).Append(">(").Append(length).AppendLine(");");
            sb.Append(bodyIndent).Append("for (var ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(length).Append("; ").Append(index).AppendLine("++)");
            sb.Append(bodyIndent).AppendLine("{");
            EmitReadElement(sb, element, itemVar, loopIndent, alloc);
            if (mapping.Kind == FieldKind.Array)
                sb.Append(loopIndent).Append(collection).Append('[').Append(index).Append("] = ").Append(ElementStore(element, itemVar)).AppendLine(";");
            else
                sb.Append(loopIndent).Append(collection).Append(".Add(").Append(ElementStore(element, itemVar)).AppendLine(");");
            sb.Append(bodyIndent).AppendLine("}");
        }

        sb.Append(bodyIndent).Append(target).Append(" = ").Append(collection).AppendLine(";");
        sb.Append(indent).AppendLine("}");
    }

    /// <summary>
    /// Builds the C# allocation expression for a single-dimension array of
    /// <paramref name="elementTypeName"/>. For a jagged array the length belongs in the FIRST bracket
    /// pair with the element's own bracket pairs appended after it: element <c>int[]</c> allocates as
    /// <c>new int[len][]</c> (not the invalid <c>new int[][len]</c>), element <c>int[][]</c> as
    /// <c>new int[len][][]</c>. Bracket pairs only ever appear as an array suffix in the
    /// fully-qualified display name (generics use angle brackets), so peeling trailing <c>[]</c> pairs
    /// off the element type name recovers the correct structure.
    /// </summary>
    private static string GetArrayAllocationExpression(string elementTypeName, string lengthVar)
    {
        var core = elementTypeName;
        var suffix = string.Empty;
        while (core.EndsWith("[]", StringComparison.Ordinal))
        {
            core = core.Substring(0, core.Length - 2);
            suffix += "[]";
        }

        return "new " + core + "[" + lengthVar + "]" + suffix;
    }

    private static void EmitReadElement(StringBuilder sb, TypeMapping mapping, string resultVar, string indent, NameAlloc alloc)
    {
        sb.Append(indent).Append(ElementTempType(mapping)).Append(' ').Append(resultVar).AppendLine(";");

        if (IsCollectionKind(mapping.Kind))
        {
            EmitReadCollectionBody(sb, mapping, resultVar, indent, alloc);
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                sb.Append(indent).Append(resultVar).Append(" = Read").Append(GetObjectMethodName(mapping)).AppendLine("(ref reader);");
                return;
            }

            sb.Append(indent).AppendLine("if (reader.TryReadNil())");
            sb.Append(indent).Append("    ").Append(resultVar).AppendLine(" = null;");
            sb.Append(indent).AppendLine("else");
            sb.Append(indent).Append("    ").Append(resultVar).Append(" = Read").Append(GetObjectMethodName(mapping)).AppendLine("(ref reader);");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            sb.Append(indent).AppendLine("if (reader.TryReadNil())");
            sb.Append(indent).Append("    ").Append(resultVar).AppendLine(" = null;");
            sb.Append(indent).AppendLine("else");
            sb.Append(indent).Append("    ").Append(resultVar).Append(" = ").Append(GetScalarReadExpression(mapping)).AppendLine(";");
            return;
        }

        sb.Append(indent).Append(resultVar).Append(" = ").Append(GetScalarReadExpression(mapping)).AppendLine(";");
    }

    private static string GetScalarReadExpression(TypeMapping mapping)
    {
        return mapping.Kind switch
        {
            FieldKind.String => "reader.ReadString()",
            FieldKind.ByteArray => "reader.ReadBytes()?.ToArray()",
            FieldKind.Int32 => "reader.ReadInt32()",
            FieldKind.Int64 => "reader.ReadInt64()",
            FieldKind.Boolean => "reader.ReadBoolean()",
            FieldKind.Double => "reader.ReadDouble()",
            FieldKind.Decimal => "ReadDecimal(ref reader)",
            FieldKind.Guid => "ReadGuid(ref reader)",
            FieldKind.DateTime => "ReadDateTime(ref reader)",
            FieldKind.DateTimeOffset => "ReadDateTimeOffset(ref reader)",
            FieldKind.ActorRef => "ReadActorRef(ref reader)",
            FieldKind.Enum => "(" + mapping.TypeFullName + ")reader.ReadInt32()",
            _ => "default"
        };
    }

    // ----- SIZE -----

    private static void EmitSizeCollectionBody(StringBuilder sb, TypeMapping mapping, string value, string sizeVar, string indent, NameAlloc alloc)
    {
        sb.Append(indent).Append("int ").Append(sizeVar).AppendLine(";");
        sb.Append(indent).Append("if (").Append(value).AppendLine(" is null)");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).Append("    ").Append(sizeVar).AppendLine(" = SizeOfNil();");
        sb.Append(indent).AppendLine("}");
        sb.Append(indent).AppendLine("else");
        sb.Append(indent).AppendLine("{");

        var bodyIndent = indent + "    ";
        var loopIndent = bodyIndent + "    ";
        if (mapping.Kind == FieldKind.Dictionary)
        {
            var kvp = alloc.Next("kvp");
            sb.Append(bodyIndent).Append(sizeVar).Append(" = SizeOfMapHeader(").Append(value).AppendLine(".Count);");
            sb.Append(bodyIndent).Append("foreach (var ").Append(kvp).Append(" in ").Append(value).AppendLine(")");
            sb.Append(bodyIndent).AppendLine("{");
            EmitSizeElement(sb, mapping.TypeArguments[0], kvp + ".Key", sizeVar, loopIndent, alloc);
            EmitSizeElement(sb, mapping.TypeArguments[1], kvp + ".Value", sizeVar, loopIndent, alloc);
            sb.Append(bodyIndent).AppendLine("}");
        }
        else
        {
            var item = alloc.Next("item");
            sb.Append(bodyIndent).Append(sizeVar).Append(" = SizeOfArrayHeader(").Append(value).Append('.').Append(CollectionCountMember(mapping.Kind)).AppendLine(");");
            sb.Append(bodyIndent).Append("foreach (var ").Append(item).Append(" in ").Append(value).AppendLine(")");
            sb.Append(bodyIndent).AppendLine("{");
            EmitSizeElement(sb, mapping.TypeArguments[0], item, sizeVar, loopIndent, alloc);
            sb.Append(bodyIndent).AppendLine("}");
        }

        sb.Append(indent).AppendLine("}");
    }

    private static void EmitSizeElement(StringBuilder sb, TypeMapping mapping, string value, string sizeVar, string indent, NameAlloc alloc)
    {
        if (IsCollectionKind(mapping.Kind))
        {
            var innerSize = alloc.Next("size");
            EmitSizeCollectionBody(sb, mapping, value, innerSize, indent, alloc);
            sb.Append(indent).Append(sizeVar).Append(" += ").Append(innerSize).AppendLine(";");
            return;
        }

        if (mapping.Kind == FieldKind.Object)
        {
            var elementSize = alloc.Next("size");
            string sizeExpr;
            if (mapping.IsValueType && !mapping.IsNullable)
            {
                sizeExpr = "SizeOf" + GetObjectMethodName(mapping) + "(" + value + ")";
            }
            else
            {
                var sizedValue = mapping.IsValueType ? value + ".Value" : value;
                sizeExpr = value + " is null ? SizeOfNil() : SizeOf" + GetObjectMethodName(mapping) + "(" + sizedValue + ")";
            }

            sb.Append(indent).Append("var ").Append(elementSize).Append(" = ").Append(sizeExpr).AppendLine(";");
            sb.Append(indent).Append("if (").Append(elementSize).AppendLine(" < 0)");
            sb.Append(indent).AppendLine("    return global::Akka.Serialization.SerializerV2.UnknownSize;");
            sb.Append(indent).Append(sizeVar).Append(" += ").Append(elementSize).AppendLine(";");
            return;
        }

        if (mapping.IsNullable && IsScalarValueKind(mapping.Kind))
        {
            sb.Append(indent).Append(sizeVar).Append(" += ").Append(value).Append(" is null ? SizeOfNil() : ")
                .Append(GetScalarSizeExpression(mapping, value + ".Value")).AppendLine(";");
            return;
        }

        sb.Append(indent).Append(sizeVar).Append(" += ").Append(GetScalarSizeExpression(mapping, value)).AppendLine(";");
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
            return new TypeMapping(FieldKind.Object, GetMessageDictionaryKey(namedType), namedType.IsValueType);

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
            return new TypeMapping(FieldKind.MissingSerializableDefinition, GetFullyQualifiedTypeName(missingNestedType));

        return mapping;
    }

    /// <summary>
    /// Maps the four natively-supported collection shapes -- <c>T[]</c>, <c>List&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, and <c>Dictionary&lt;TKey,TValue&gt;</c> -- to their collection
    /// <see cref="FieldKind"/>, recursively mapping element/key/value types so collections compose.
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
        if (knownTypes.ListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ListOfT))
        {
            var element = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
            mapping = TryCollapseBadElement(element, out var collapsed)
                ? collapsed
                : new TypeMapping(FieldKind.List, typeArguments: ImmutableArray.Create(element));
            return true;
        }

        if (knownTypes.ReadOnlyListOfT != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.ReadOnlyListOfT))
        {
            var element = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
            mapping = TryCollapseBadElement(element, out var collapsed)
                ? collapsed
                : new TypeMapping(FieldKind.ReadOnlyList, typeArguments: ImmutableArray.Create(element));
            return true;
        }

        if (knownTypes.DictionaryOfKeyValue != null && SymbolEqualityComparer.Default.Equals(definition, knownTypes.DictionaryOfKeyValue))
        {
            var key = MapCollectionElement(namedType.TypeArguments[0], knownTypes);
            var value = MapCollectionElement(namedType.TypeArguments[1], knownTypes);
            mapping = TryCollapseBadElement(key, out var collapsedKey) ? collapsedKey
                : TryCollapseBadElement(value, out var collapsedValue) ? collapsedValue
                : new TypeMapping(FieldKind.Dictionary, typeArguments: ImmutableArray.Create(key, value));
            return true;
        }

        return false;
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
        => kind is FieldKind.Array or FieldKind.List or FieldKind.ReadOnlyList or FieldKind.Dictionary;

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
            // local (see GetLocalType/IsReferenceLike): "null" would not compile for it, so fall
            // back to "default" the same way every other non-reference-like kind does below.
            FieldKind.Object => IsReferenceLike(field) ? "null" : "default",
            _ => "default"
        };
    }

    private static string GetLocalType(FieldInfo field)
    {
        return IsReferenceLike(field) ? field.TypeFullName + "?" : field.TypeFullName;
    }

    private static bool IsRequired(FieldInfo field)
    {
        return !field.IsNullable;
    }

    private static bool IsReferenceLike(FieldInfo field)
    {
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
    private static string GetHasLocalName(FieldInfo field)
    {
        return "__has" + field.Name;
    }

    private static string GetFieldValueExpression(FieldInfo field)
    {
        var name = ToCamelCase(field.Name);
        return IsRequired(field) && IsReferenceLike(field) ? name + "!" : name;
    }

    private static string GetObjectMethodName(TypeMapping mapping)
    {
        return FoldTypeName(mapping.TypeFullName);
    }

    /// <summary>
    /// Folds a fully-qualified type name into a compact generated-member identifier the way
    /// System.Text.Json's <c>GetTypeInfoPropertyName</c> does: namespaces are dropped, each type
    /// identifier keeps only its simple name, and generic type arguments are concatenated --
    /// <c>global::Ns.Wrapper&lt;global::Ns.OrderRequest&gt;</c> becomes <c>WrapperOrderRequest</c>.
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

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var name = char.ToLowerInvariant(value[0]) + value.Substring(1);

        // A property named 'Event', 'Lock', 'Object', etc. camel-cases to a reserved C# keyword,
        // which cannot be used as a local identifier. Escape with '@' (a runtime no-op). The escaped
        // form composes safely with the suffixes appended elsewhere ('@eventSize', '@eventBytes'):
        // '@' is legal on any identifier, keyword or not.
        return EscapeIfKeyword(name);
    }

    /// <summary>
    /// '@'-escapes <paramref name="identifier"/> if it is a reserved C# keyword, a runtime no-op that
    /// makes the text legal as an identifier. Shared by <see cref="ToCamelCase"/> (per-field read
    /// locals) and the constructor-argument label in <see cref="GenerateReadMessageConstruction"/>: a
    /// constructor parameter literally named <c>event</c> (matched to an [AkkaField] property named
    /// <c>Event</c>) must emit the NAMED argument as <c>@event:</c>, not <c>event:</c>.
    /// </summary>
    private static string EscapeIfKeyword(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class SerializerInfo
    {
        public SerializerInfo(
            string ns,
            string className,
            string fullyQualifiedName,
            string name,
            int serializerId,
            INamedTypeSymbol? protocolType,
            string protocolTypeFullName,
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
            ProtocolType = protocolType;
            ProtocolTypeFullName = protocolTypeFullName;
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
        public INamedTypeSymbol? ProtocolType { get; }
        public string ProtocolTypeFullName { get; }
        public Accessibility DeclaredAccessibility { get; }
        public ImmutableArray<FormatterInfo> Formatters { get; }
        public ImmutableArray<ClosedGenericRegistrationInfo> ClosedGenericRegistrations { get; }

        /// <summary>Whether every syntax declaration of this class carries 'partial'. See AKKASG032.</summary>
        public bool IsPartial { get; }

        /// <summary>Whether the serializer class itself is a generic type definition. See AKKASG032.</summary>
        public bool IsGeneric { get; }

        /// <summary>Whether the class derives (directly or transitively) from <c>Akka.Serialization.V2.AkkaSerializer</c>. See AKKASG032.</summary>
        public bool DerivesFromAkkaSerializerBase { get; }
    }

    private sealed class KnownTypes
    {
        private KnownTypes(Compilation compilation)
        {
            FieldAttribute = compilation.GetTypeByMetadataName(FieldAttributeFullName);
            EnvelopePayloadAttribute = compilation.GetTypeByMetadataName(EnvelopePayloadAttributeFullName);
            UnionAttribute = compilation.GetTypeByMetadataName(UnionAttributeFullName);
            SerializableAttribute = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
            Guid = compilation.GetTypeByMetadataName("System.Guid");
            DateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
            ActorRef = compilation.GetTypeByMetadataName("Akka.Actor.IActorRef");
            ListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            ReadOnlyListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
            DictionaryOfKeyValue = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
        }

        public INamedTypeSymbol? FieldAttribute { get; }
        public INamedTypeSymbol? EnvelopePayloadAttribute { get; }
        public INamedTypeSymbol? UnionAttribute { get; }
        public INamedTypeSymbol? SerializableAttribute { get; }
        public INamedTypeSymbol? Guid { get; }
        public INamedTypeSymbol? DateTimeOffset { get; }
        public INamedTypeSymbol? ActorRef { get; }
        public INamedTypeSymbol? ListOfT { get; }
        public INamedTypeSymbol? ReadOnlyListOfT { get; }
        public INamedTypeSymbol? DictionaryOfKeyValue { get; }

        public static KnownTypes From(Compilation compilation)
        {
            return new KnownTypes(compilation);
        }
    }

    private sealed class MessageInfo
    {
        public MessageInfo(
            string simpleName,
            string fullyQualifiedName,
            string manifest,
            ImmutableArray<FieldInfo> fields,
            ImmutableArray<INamedTypeSymbol> protocols,
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
        public ImmutableArray<INamedTypeSymbol> Protocols { get; }
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
    }

    /// <summary>
    /// A single <c>[AkkaField]</c> property found unusable during extraction: static, or its getter
    /// is not accessible to the generated code. See AKKASG028.
    /// </summary>
    private sealed class InvalidFieldInfo
    {
        public InvalidFieldInfo(string propertyName, string reason)
        {
            PropertyName = propertyName;
            Reason = reason;
        }

        public string PropertyName { get; }

        /// <summary>Free-text reason, e.g. "is static; ..." or "has no accessible getter".</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// How a message's constructor is called on deserialize. <see cref="Arguments"/> supplies NAMED
    /// constructor arguments (parameter name -&gt; field name); <see cref="InitializerFieldNames"/>
    /// lists [AkkaField] properties assigned afterward via object initializer. Both are non-empty only
    /// when <see cref="IsValid"/>; otherwise <see cref="Errors"/> explains what could not be satisfied
    /// (AKKASG026). <see cref="UncoveredDefaultedParameters"/> is advisory (AKKASG027) and can be
    /// non-empty even when <see cref="IsValid"/> is true.
    /// </summary>
    private sealed class ConstructionPlan
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
    }

    /// <summary>A single NAMED constructor argument: <see cref="ParameterName"/> supplied from the field named <see cref="FieldName"/>.</summary>
    private readonly struct ConstructorArgumentPlan
    {
        public ConstructorArgumentPlan(string parameterName, string fieldName)
        {
            ParameterName = parameterName;
            FieldName = fieldName;
        }

        public string ParameterName { get; }
        public string FieldName { get; }
    }

    /// <summary>
    /// A single <c>[AkkaSerializable&lt;T&gt;]</c> registration. <see cref="Message"/> is null
    /// when the target was invalid (not a type, non-generic, unbound, or its definition lacks
    /// <c>[AkkaSerializable]</c>) so AKKASG020 fires instead of the registration silently vanishing.
    /// </summary>
    private sealed class ClosedGenericRegistrationInfo
    {
        public ClosedGenericRegistrationInfo(string targetDisplayName, MessageInfo? message)
        {
            TargetDisplayName = targetDisplayName;
            Message = message;
        }

        public string TargetDisplayName { get; }
        public MessageInfo? Message { get; }
    }

    private sealed class FieldInfo
    {
        public FieldInfo(int index, string name, string typeFullName, TypeMapping mapping, bool isNullable, FormatterInfo? formatter = null, ImmutableArray<UnionMemberInfo> unionMembers = default)
        {
            Index = index;
            Name = name;
            TypeFullName = typeFullName;
            Mapping = mapping;
            IsNullable = isNullable;
            Formatter = formatter;
            UnionMembers = unionMembers.IsDefault ? ImmutableArray<UnionMemberInfo>.Empty : unionMembers;
        }

        public int Index { get; }
        public string Name { get; }
        public string TypeFullName { get; }
        public TypeMapping Mapping { get; }
        public bool IsNullable { get; }
        public FormatterInfo? Formatter { get; }

        /// <summary>Declared members for a <see cref="FieldKind.Union"/> field; empty otherwise.</summary>
        public ImmutableArray<UnionMemberInfo> UnionMembers { get; }

        public FieldInfo WithFormatter(TypeMapping mapping, FormatterInfo formatter)
        {
            return new FieldInfo(Index, Name, TypeFullName, mapping, IsNullable, formatter, UnionMembers);
        }
    }

    private readonly struct TypeMapping
    {
        public TypeMapping(
            FieldKind kind,
            string typeFullName = "",
            bool isValueType = false,
            string declaredTypeName = "",
            bool isNullable = false,
            ImmutableArray<TypeMapping> typeArguments = default,
            string enumUnderlyingTypeName = "")
        {
            Kind = kind;
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            DeclaredTypeName = declaredTypeName;
            IsNullable = isNullable;
            TypeArguments = typeArguments.IsDefault ? ImmutableArray<TypeMapping>.Empty : typeArguments;
            EnumUnderlyingTypeName = enumUnderlyingTypeName;
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
        /// Child mappings for a collection kind: a single element mapping for
        /// <see cref="FieldKind.Array"/>/<see cref="FieldKind.List"/>/<see cref="FieldKind.ReadOnlyList"/>,
        /// and [key, value] for <see cref="FieldKind.Dictionary"/>. Empty for every non-collection kind.
        /// </summary>
        public ImmutableArray<TypeMapping> TypeArguments { get; }

        /// <summary>
        /// For <see cref="FieldKind.UnsupportedEnumUnderlyingType"/>: the display name of the enum's
        /// underlying type (for example <c>long</c>), carried alongside <see cref="TypeFullName"/> (the
        /// enum itself) so AKKASG014 can name both. Empty for every other kind.
        /// </summary>
        public string EnumUnderlyingTypeName { get; }

        public TypeMapping WithTypeFullName(string typeFullName)
            => new(Kind, typeFullName, IsValueType, DeclaredTypeName, IsNullable, TypeArguments, EnumUnderlyingTypeName);

        public TypeMapping AsCollectionElement(string declaredTypeName, bool isNullable)
            => new(Kind, TypeFullName, IsValueType, declaredTypeName, isNullable, TypeArguments, EnumUnderlyingTypeName);
    }

    /// <summary>
    /// A serializer-scoped hand-written formatter registration extracted from
    /// <c>[AkkaSerializerFormatter(typeof(TTarget), typeof(TFormatter))]</c>. Carries only
    /// strings/bools/enums (no <see cref="ISymbol"/> references) so it stays cheap to hold across
    /// incremental generator passes.
    /// </summary>
    private sealed class FormatterInfo
    {
        public FormatterInfo(string targetTypeFullName, bool isTargetValueType, string formatterTypeFullName, bool implementsInterface, FormatterCtorKind ctorKind, bool isTargetSupported)
        {
            TargetTypeFullName = targetTypeFullName;
            IsTargetValueType = isTargetValueType;
            FormatterTypeFullName = formatterTypeFullName;
            ImplementsInterface = implementsInterface;
            CtorKind = ctorKind;
            IsTargetSupported = isTargetSupported;
        }

        public string TargetTypeFullName { get; }
        public bool IsTargetValueType { get; }
        public string FormatterTypeFullName { get; }
        public bool ImplementsInterface { get; }
        public FormatterCtorKind CtorKind { get; }
        public bool IsTargetSupported { get; }
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
        Union
    }

    /// <summary>
    /// A single declared member of an <c>[AkkaUnion]</c> field. Carries only strings/bools (no
    /// <see cref="ISymbol"/> references) so it stays cheap across incremental generator passes.
    /// Facts requiring symbol access (assignability, unbound-generic detection) are captured at
    /// extraction time; facts requiring the whole-compilation message set (serializability,
    /// manifests) are resolved later against the serializer's message dictionary.
    /// </summary>
    private sealed class UnionMemberInfo
    {
        public UnionMemberInfo(string typeFullName, bool isValueType, bool isAssignable, bool isSupported, bool isSealed)
        {
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            IsAssignable = isAssignable;
            IsSupported = isSupported;
            IsSealed = isSealed;
        }

        /// <summary>Message-dictionary key for the member type (arity-aware for generics).</summary>
        public string TypeFullName { get; }

        public bool IsValueType { get; }

        /// <summary>Whether the member type is implicitly convertible to the field's static type.</summary>
        public bool IsAssignable { get; }

        /// <summary>False when the attribute argument was null, not a type, or an unbound generic.</summary>
        public bool IsSupported { get; }

        /// <summary>Whether undeclared subtypes are impossible (sealed class, struct). Advisory AKKASG025 fires otherwise.</summary>
        public bool IsSealed { get; }
    }
}
