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
    private const string SerializerAttributeFullName = "Akka.Serialization.V2.AkkaSerializerAttribute";
    private const string SerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializableAttribute";
    private const string FieldAttributeFullName = "Akka.Serialization.V2.AkkaFieldAttribute";
    private const string EnvelopePayloadAttributeFullName = "Akka.Serialization.V2.AkkaEnvelopePayloadAttribute";
    private const string UnionAttributeFullName = "Akka.Serialization.V2.AkkaUnionAttribute";
    private const string FormatterAttributeFullName = "Akka.Serialization.V2.AkkaSerializerFormatterAttribute";
    private const string FormatterInterfaceFullName = "Akka.Serialization.V2.IAkkaMessagePackFormatter`1";
    private const string ExtendedActorSystemFullName = "Akka.Actor.ExtendedActorSystem";

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

        context.RegisterSourceOutput(serializers.Combine(messages), static (ctx, pair) =>
        {
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

                if (!ValidateFormatters(ctx, serializer))
                    continue;

                var allMessages = pair.Right
                    .Where(message => message != null)
                    .Cast<MessageInfo>()
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

                ctx.AddSource(serializer.ClassName + ".AkkaSerialization.g.cs", Generate(serializer, topLevelMessages, reachableMessages, resolvedMessagesByType));
            }
        });
    }

    private static SerializerInfo? ExtractSerializer(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        var compilation = context.SemanticModel.Compilation;
        var messagePackSerializer = compilation.GetTypeByMetadataName("Akka.Serialization.V2.MessagePackSerializer`1");
        string? name = null;
        var serializerId = 0;

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Value is string value)
                name = value;
            else if (argument.Key == "SerializerId" && argument.Value.Value is int id)
                serializerId = id;
        }

        var baseType = symbol.BaseType;
        string protocolTypeFullName = string.Empty;
        INamedTypeSymbol? protocolType = null;
        while (baseType != null)
        {
            if (messagePackSerializer != null && SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, messagePackSerializer))
            {
                protocolType = baseType.TypeArguments[0] as INamedTypeSymbol;
                protocolTypeFullName = baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                break;
            }

            baseType = baseType.BaseType;
        }

        var formatterAttributeType = compilation.GetTypeByMetadataName(FormatterAttributeFullName);
        var formatterInterfaceType = compilation.GetTypeByMetadataName(FormatterInterfaceFullName);
        var extendedActorSystemType = compilation.GetTypeByMetadataName(ExtendedActorSystemFullName);
        var formatters = ExtractFormatters(symbol, formatterAttributeType, formatterInterfaceType, extendedActorSystemType);

        return new SerializerInfo(
            GetNamespace(symbol),
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            name ?? string.Empty,
            serializerId,
            protocolType,
            protocolTypeFullName,
            symbol.DeclaredAccessibility,
            formatters);
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

        var fields = new List<FieldInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var fieldAttribute = member.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute));
            if (fieldAttribute == null || fieldAttribute.ConstructorArguments.Length != 1)
                continue;

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
        }

        return new MessageInfo(
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            symbol.AllInterfaces.ToImmutableArray(),
            allowEmpty);
    }

    /// <summary>
    /// Extracts the declared member set of an <c>[AkkaUnion]</c> property. Symbol-dependent facts
    /// (assignability to the field's static type, unbound-generic detection) are captured here;
    /// facts that need the whole message set (serializability, manifests) are validated later in
    /// <see cref="ValidateMessages"/> against the serializer's message dictionary. Malformed
    /// arguments (null, not a type, unbound generic) are recorded as unsupported entries so a
    /// diagnostic fires instead of the member silently vanishing.
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

        var unionAttribute = member.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.UnionAttribute));
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
                builder.Add(new UnionMemberInfo(displayName, isValueType: false, isAssignable: false, isSupported: false));
                continue;
            }

            builder.Add(new UnionMemberInfo(
                GetMessageDictionaryKey(memberType),
                memberType.IsValueType,
                compilation.HasImplicitConversion(memberType, member.Type),
                isSupported: true));
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

        foreach (var message in reachableMessages)
        {
            GenerateSizeMessage(sb, message);
            GenerateWriteMessage(sb, message);
            GenerateReadMessage(sb, message);
            GenerateUnionHelpers(sb, message, messagesByType);
        }

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

    private static void GenerateSizeMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private int SizeOf").Append(GetMessageMethodName(message))
            .Append('(').Append(message.FullyQualifiedName).AppendLine(" message)");
        sb.AppendLine("    {");
        sb.AppendLine("        checked");
        sb.AppendLine("        {");
        sb.Append("            var size = SizeOfMapHeader(").Append(message.Fields.Length).AppendLine(");");
        var alloc = new NameAlloc();
        foreach (var field in message.Fields)
            GenerateSizeField(sb, message, field, alloc);
        sb.AppendLine("            return size;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSizeField(StringBuilder sb, MessageInfo message, FieldInfo field, NameAlloc alloc)
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
        GenerateSizeExpression(sb, message, field, value);
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

    private static void GenerateSizeExpression(StringBuilder sb, MessageInfo message, FieldInfo field, string value)
    {
        switch (field.Mapping.Kind)
        {
            case FieldKind.EnvelopePayload:
                sb.Append("SizeOfEnvelopePayload(").Append(value).Append(')');
                break;
            case FieldKind.Union when field.IsNullable:
                sb.Append(value).Append(" is null ? SizeOfNil() : SizeOfUnion_").Append(GetMessageMethodName(message)).Append('_').Append(field.Name).Append('(').Append(value).Append(')');
                break;
            case FieldKind.Union:
                sb.Append("SizeOfUnion_").Append(GetMessageMethodName(message)).Append('_').Append(field.Name).Append('(').Append(value).Append(')');
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

    private static void GenerateWriteMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private void Write").Append(GetMessageMethodName(message))
            .Append("(ref global::MessagePack.MessagePackWriter writer, ").Append(message.FullyQualifiedName).AppendLine(" message)");
        sb.AppendLine("    {");
        sb.Append("        writer.WriteMapHeader(").Append(message.Fields.Length).AppendLine(");");
        var alloc = new NameAlloc();
        foreach (var field in message.Fields)
            GenerateWriteField(sb, message, field, alloc);
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateReadMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private ").Append(message.FullyQualifiedName).Append(" Read").Append(GetMessageMethodName(message))
            .AppendLine("(ref global::MessagePack.MessagePackReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fieldCount = reader.ReadMapHeader();");
        var alloc = new NameAlloc();
        foreach (var field in message.Fields)
        {
            sb.Append("        ").Append(GetLocalType(field)).Append(' ').Append(ToCamelCase(field.Name)).Append(" = ")
                .Append(DefaultValue(field)).AppendLine(";");
            if (IsRequired(field))
                sb.Append("        var ").Append(GetHasLocalName(field)).AppendLine(" = false;");
        }

        sb.AppendLine("        for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var fieldId = reader.ReadInt32();");
        sb.AppendLine("            switch (fieldId)");
        sb.AppendLine("            {");
        foreach (var field in message.Fields)
        {
            sb.Append("                case ").Append(field.Index).AppendLine(":");
            GenerateReadField(sb, message, field, alloc);
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

        sb.Append("        return new ").Append(message.FullyQualifiedName).Append('(')
            .Append(string.Join(", ", message.Fields.Select(GetConstructorArgument)))
            .AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
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

    private static string GetUnionHelperName(MessageInfo message, FieldInfo field)
    {
        return "Union_" + GetMessageMethodName(message) + "_" + field.Name;
    }

    private static void GenerateUnionHelpers(StringBuilder sb, MessageInfo message, ImmutableDictionary<string, MessageInfo> messagesByType)
    {
        foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Union))
        {
            var helperName = GetUnionHelperName(message, field);
            var members = field.UnionMembers
                .Where(member => member.IsSupported && messagesByType.ContainsKey(member.TypeFullName))
                .Select(member => (Member: member, Message: messagesByType[member.TypeFullName]))
                .ToImmutableArray();

            GenerateUnionWrite(sb, field, helperName, members);
            GenerateUnionRead(sb, field, helperName, members);
            GenerateUnionSize(sb, field, helperName, members);
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

        sb.Append("        throw new global::System.Runtime.Serialization.SerializationException($\"Type [{runtimeType}] is not a declared union member for field [")
            .Append(Escape(field.Name)).AppendLine("].\");");
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
        sb.Append("                            throw new global::System.Runtime.Serialization.SerializationException(\"Union manifest must precede the payload for field [")
            .Append(Escape(field.Name)).AppendLine("].\");");
        sb.AppendLine("                        default:");
        sb.Append("                            throw new global::System.Runtime.Serialization.SerializationException($\"Unknown union manifest [{manifest}] for field [")
            .Append(Escape(field.Name)).AppendLine("].\");");
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
        sb.Append("            throw new global::System.Runtime.Serialization.SerializationException(\"Missing union payload for field [")
            .Append(Escape(field.Name)).AppendLine("].\");");
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

    private static void GenerateWriteField(StringBuilder sb, MessageInfo message, FieldInfo field, NameAlloc alloc)
    {
        var value = "message." + field.Name;
        sb.Append("        writer.Write(").Append(field.Index).AppendLine(");");
        if (IsNullableValueField(field))
        {
            sb.Append("        if (").Append(value).AppendLine(" is null)");
            sb.AppendLine("            writer.WriteNil();");
            sb.AppendLine("        else");
            GenerateWriteFieldValue(sb, message, field, value + ".Value", "            ", alloc);
            return;
        }

        GenerateWriteFieldValue(sb, message, field, value, "        ", alloc);
    }

    private static void GenerateWriteFieldValue(StringBuilder sb, MessageInfo message, FieldInfo field, string value, string indent, NameAlloc alloc)
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
                    sb.Append(indent).Append("    Write").Append(GetUnionHelperName(message, field)).Append("(ref writer, ").Append(value).AppendLine(");");
                }
                else
                {
                    sb.Append(indent).Append("Write").Append(GetUnionHelperName(message, field)).Append("(ref writer, ").Append(value).AppendLine(");");
                }
                break;
        }
    }

    private static void GenerateReadField(StringBuilder sb, MessageInfo message, FieldInfo field, NameAlloc alloc)
    {
        var target = ToCamelCase(field.Name);

        // Collection fields own their MessagePack nil handling end-to-end (EmitReadCollectionBody
        // does its own TryReadNil), so they are read directly regardless of the field's nullability:
        // a nil-on-the-wire assigns null, and the post-loop required-field guard rejects a null in a
        // non-nullable collection slot exactly as it does for any other non-nullable reference field.
        if (IsCollectionKind(field.Mapping.Kind))
        {
            GenerateReadFieldValue(sb, message, field, target, "                    ", alloc);
            return;
        }

        if (IsNullableValueField(field))
        {
            sb.AppendLine("                    if (reader.TryReadNil())");
            sb.Append("                        ").Append(target).AppendLine(" = null;");
            sb.AppendLine("                    else");
            GenerateReadFieldValue(sb, message, field, target, "                        ", alloc);
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
            GenerateReadFieldValue(sb, message, field, target, "                        ", alloc);
            return;
        }

        GenerateReadFieldValue(sb, message, field, target, "                    ", alloc);
    }

    private static void GenerateReadFieldValue(StringBuilder sb, MessageInfo message, FieldInfo field, string target, string indent, NameAlloc alloc)
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
                sb.Append(indent).Append(target).Append(" = Read").Append(GetUnionHelperName(message, field)).AppendLine("(ref reader);");
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

        if (type is INamedTypeSymbol namedType && namedType.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute)))
            return new TypeMapping(FieldKind.Object, GetFullyQualifiedTypeName(namedType), namedType.IsValueType);

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

    private static string GetHasLocalName(FieldInfo field)
    {
        return "has" + field.Name;
    }

    private static string GetConstructorArgument(FieldInfo field)
    {
        var name = ToCamelCase(field.Name);
        return IsRequired(field) && IsReferenceLike(field) ? name + "!" : name;
    }

    private static string GetObjectMethodName(TypeMapping mapping)
    {
        return SanitizeTypeName(mapping.TypeFullName);
    }

    private static string GetFormatterFieldName(FormatterInfo formatter)
    {
        return "_akkaFormatter_" + SanitizeTypeName(formatter.TargetTypeFullName);
    }

    private static string SanitizeTypeName(string typeFullName)
    {
        // Escape literal underscores FIRST so sanitization is collision-free:
        // 'My.Ns.Foo_Bar' -> 'My_Ns_Foo__Bar' and 'My.Ns.Foo.Bar' -> 'My_Ns_Foo_Bar' stay
        // distinct instead of both collapsing to 'My_Ns_Foo_Bar' (duplicate generated members).
        return typeFullName
            .Replace("global::", string.Empty)
            .Replace("_", "__")
            .Replace(".", "_")
            .Replace("+", "_");
    }

    private static string GetMessageMethodName(MessageInfo message)
    {
        return SanitizeTypeName(message.FullyQualifiedName);
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
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
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
            ImmutableArray<FormatterInfo> formatters)
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
        public MessageInfo(string simpleName, string fullyQualifiedName, string manifest, ImmutableArray<FieldInfo> fields, ImmutableArray<INamedTypeSymbol> protocols, bool allowEmpty)
        {
            SimpleName = simpleName;
            FullyQualifiedName = fullyQualifiedName;
            Manifest = manifest;
            Fields = fields;
            Protocols = protocols;
            AllowEmpty = allowEmpty;
        }

        public string SimpleName { get; }
        public string FullyQualifiedName { get; }
        public string Manifest { get; }
        public ImmutableArray<FieldInfo> Fields { get; }
        public ImmutableArray<INamedTypeSymbol> Protocols { get; }
        public bool AllowEmpty { get; }

        public MessageInfo WithFields(ImmutableArray<FieldInfo> fields)
        {
            return new MessageInfo(SimpleName, FullyQualifiedName, Manifest, fields, Protocols, AllowEmpty);
        }
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
        public UnionMemberInfo(string typeFullName, bool isValueType, bool isAssignable, bool isSupported)
        {
            TypeFullName = typeFullName;
            IsValueType = isValueType;
            IsAssignable = isAssignable;
            IsSupported = isSupported;
        }

        /// <summary>Message-dictionary key for the member type (arity-aware for generics).</summary>
        public string TypeFullName { get; }

        public bool IsValueType { get; }

        /// <summary>Whether the member type is implicitly convertible to the field's static type.</summary>
        public bool IsAssignable { get; }

        /// <summary>False when the attribute argument was null, not a type, or an unbound generic.</summary>
        public bool IsSupported { get; }
    }
}
