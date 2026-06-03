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
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akka.Serialization.V2.Generators;

[Generator]
public sealed class AkkaSerializerGenerator : IIncrementalGenerator
{
    private const string SerializerAttributeFullName = "Akka.Serialization.V2.AkkaSerializerAttribute";
    private const string SerializableAttributeFullName = "Akka.Serialization.V2.AkkaSerializableAttribute";
    private const string FieldAttributeFullName = "Akka.Serialization.V2.AkkaFieldAttribute";

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
        "[AkkaSerializable] type '{0}' must declare at least one [AkkaField] property",
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

                var allMessages = pair.Right
                    .Where(message => message != null)
                    .Cast<MessageInfo>()
                    .ToImmutableArray();
                var allMessagesByType = allMessages.ToImmutableDictionary(message => message.FullyQualifiedName);
                var topLevelMessages = allMessages
                    .Where(message => serializer.ProtocolType != null && message.Protocols.Any(protocol => SymbolEqualityComparer.Default.Equals(protocol, serializer.ProtocolType)))
                    .Cast<MessageInfo>()
                    .ToImmutableArray();
                var reachableMessages = CollectReachableMessages(topLevelMessages, allMessagesByType);

                if (!ValidateMessages(ctx, topLevelMessages, reachableMessages))
                    continue;

                ctx.AddSource(serializer.ClassName + ".AkkaSerialization.g.cs", Generate(serializer, topLevelMessages, reachableMessages));
            }
        });
    }

    private static SerializerInfo? ExtractSerializer(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        var messagePackSerializer = context.SemanticModel.Compilation.GetTypeByMetadataName("Akka.Serialization.V2.MessagePackSerializer`1");
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

        return new SerializerInfo(
            GetNamespace(symbol),
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            name ?? string.Empty,
            serializerId,
            protocolType,
            protocolTypeFullName);
    }

    private static MessageInfo? ExtractMessage(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
        var knownTypes = KnownTypes.From(context.SemanticModel.Compilation);
        var manifest = string.Empty;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Manifest" && argument.Value.Value is string value)
                manifest = value;
        }

        var fields = new List<FieldInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var fieldAttribute = member.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.FieldAttribute));
            if (fieldAttribute == null || fieldAttribute.ConstructorArguments.Length != 1)
                continue;

            var index = (int)fieldAttribute.ConstructorArguments[0].Value!;
            var isNullable = member.NullableAnnotation == NullableAnnotation.Annotated;
            fields.Add(new FieldInfo(index, member.Name, member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), MapType(member.Type, knownTypes), isNullable));
        }

        return new MessageInfo(
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            symbol.AllInterfaces.ToImmutableArray());
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
            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Object))
            {
                if (allMessagesByType.TryGetValue(field.Mapping.TypeFullName, out var nestedMessage))
                    pending.Enqueue(nestedMessage);
            }
        }

        return messages.ToImmutable();
    }

    private static bool ValidateMessages(SourceProductionContext context, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages)
    {
        var isValid = true;
        foreach (var message in topLevelMessages.Where(message => string.IsNullOrWhiteSpace(message.Manifest)))
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingManifest, Location.None, message.FullyQualifiedName));
            isValid = false;
        }

        foreach (var message in reachableMessages)
        {
            if (message.Fields.Length == 0)
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
        }

        return isValid;
    }

    private static string Generate(SerializerInfo serializer, ImmutableArray<MessageInfo> topLevelMessages, ImmutableArray<MessageInfo> reachableMessages)
    {
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

        sb.Append("public sealed partial class ").Append(serializer.ClassName).AppendLine();
        sb.AppendLine("{");
        sb.Append("    public ").Append(serializer.ClassName).AppendLine("(global::Akka.Actor.ExtendedActorSystem system) : base(system)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public override int Identifier => ").Append(serializer.SerializerId).AppendLine(";");
        sb.AppendLine();
        GenerateRegistration(sb, serializer);
        GenerateManifest(sb, topLevelMessages);
        GenerateSerialize(sb, topLevelMessages);
        GenerateDeserialize(sb, topLevelMessages);
        sb.AppendLine("    public override int SizeHint(object obj) => 128;");
        sb.AppendLine();

        foreach (var message in reachableMessages)
        {
            GenerateWriteMessage(sb, message);
            GenerateReadMessage(sb, message);
        }

        sb.AppendLine("}");
        return sb.ToString();
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
        sb.AppendLine("        var akkaWriter = new global::Akka.Serialization.V2.AkkaWriter(writer);");
        sb.AppendLine("        switch (obj)");
        sb.AppendLine("        {");
        foreach (var message in messages)
        {
            sb.Append("            case ").Append(message.FullyQualifiedName).AppendLine(" message:");
            sb.Append("                Write").Append(GetMessageMethodName(message)).AppendLine("(akkaWriter, message);");
            sb.AppendLine("                break;");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                throw new global::System.ArgumentException($\"Unsupported generated serializer type: {obj.GetType()}\", nameof(obj));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return (int)akkaWriter.BytesWritten;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateDeserialize(StringBuilder sb, ImmutableArray<MessageInfo> messages)
    {
        sb.AppendLine("    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)");
        sb.AppendLine("    {");
        sb.AppendLine("        var reader = new global::Akka.Serialization.V2.AkkaReader(bytes);");
        sb.AppendLine("        return manifest switch");
        sb.AppendLine("        {");
        foreach (var message in messages)
            sb.Append("            \"").Append(Escape(message.Manifest)).Append("\" => Read").Append(GetMessageMethodName(message)).AppendLine("(reader),");
        sb.AppendLine("            _ => throw new global::System.Runtime.Serialization.SerializationException($\"Unknown generated serializer manifest [{manifest}] for serializer [{GetType()}].\")");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateWriteMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private static void Write").Append(GetMessageMethodName(message))
            .Append("(global::Akka.Serialization.V2.AkkaWriter writer, ").Append(message.FullyQualifiedName).AppendLine(" message)");
        sb.AppendLine("    {");
        sb.Append("        writer.BeginObject(").Append(message.Fields.Length).AppendLine(");");
        foreach (var field in message.Fields)
            GenerateWriteField(sb, field);
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateReadMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private ").Append(message.FullyQualifiedName).Append(" Read").Append(GetMessageMethodName(message))
            .AppendLine("(global::Akka.Serialization.V2.AkkaReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fieldCount = reader.BeginReadObject();");
        foreach (var field in message.Fields)
        {
            sb.Append("        ").Append(GetLocalType(field)).Append(' ').Append(ToCamelCase(field.Name)).Append(" = ")
                .Append(DefaultValue(field)).AppendLine(";");
            if (IsRequired(field))
                sb.Append("        var ").Append(GetHasLocalName(field)).AppendLine(" = false;");
        }

        sb.AppendLine("        for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var fieldId = reader.ReadFieldId();");
        sb.AppendLine("            switch (fieldId)");
        sb.AppendLine("            {");
        foreach (var field in message.Fields)
        {
            sb.Append("                case ").Append(field.Index).AppendLine(":");
            GenerateReadField(sb, field);
            if (IsRequired(field))
                sb.Append("                    ").Append(GetHasLocalName(field)).AppendLine(" = true;");
            sb.AppendLine("                    break;");
        }
        sb.AppendLine("                default:");
        sb.AppendLine("                    reader.SkipField();");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        foreach (var field in message.Fields.Where(IsRequired))
        {
            var target = ToCamelCase(field.Name);
            sb.Append("        if (!").Append(GetHasLocalName(field));
            if (IsReferenceLike(field.Mapping))
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

    private static void GenerateWriteField(StringBuilder sb, FieldInfo field)
    {
        var value = "message." + field.Name;
        sb.Append("        writer.WriteInt32(").Append(field.Index).AppendLine(");");
        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                sb.Append("        writer.WriteString(").Append(value).AppendLine(");");
                break;
            case FieldKind.Int32:
                sb.Append("        writer.WriteInt32(").Append(value).AppendLine(");");
                break;
            case FieldKind.Int64:
                sb.Append("        writer.WriteInt64(").Append(value).AppendLine(");");
                break;
            case FieldKind.Boolean:
                sb.Append("        writer.WriteBoolean(").Append(value).AppendLine(");");
                break;
            case FieldKind.Double:
                sb.Append("        writer.WriteDouble(").Append(value).AppendLine(");");
                break;
            case FieldKind.Decimal:
                sb.Append("        writer.WriteDecimal(").Append(value).AppendLine(");");
                break;
            case FieldKind.Guid:
                sb.Append("        writer.WriteGuid(").Append(value).AppendLine(");");
                break;
            case FieldKind.DateTime:
                sb.Append("        writer.WriteDateTime(").Append(value).AppendLine(");");
                break;
            case FieldKind.DateTimeOffset:
                sb.Append("        writer.WriteDateTimeOffset(").Append(value).AppendLine(");");
                break;
            case FieldKind.ActorRef:
                sb.Append("        WriteActorRef(writer, ").Append(value).AppendLine(");");
                break;
            case FieldKind.Enum:
                sb.Append("        writer.WriteInt32((int)").Append(value).AppendLine(");");
                break;
            case FieldKind.Object:
                if (field.IsNullable)
                {
                    sb.Append("        if (").Append(value).AppendLine(" is null)");
                    sb.AppendLine("            writer.WriteNil();");
                    sb.AppendLine("        else");
                    sb.Append("            Write").Append(GetObjectMethodName(field.Mapping)).Append("(writer, ").Append(value).AppendLine(");");
                }
                else
                {
                    sb.Append("        Write").Append(GetObjectMethodName(field.Mapping)).Append("(writer, ").Append(value).AppendLine(");");
                }
                break;
        }
    }

    private static void GenerateReadField(StringBuilder sb, FieldInfo field)
    {
        var target = ToCamelCase(field.Name);
        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadString();");
                break;
            case FieldKind.Int32:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadInt32();");
                break;
            case FieldKind.Int64:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadInt64();");
                break;
            case FieldKind.Boolean:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadBoolean();");
                break;
            case FieldKind.Double:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadDouble();");
                break;
            case FieldKind.Decimal:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadDecimal();");
                break;
            case FieldKind.Guid:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadGuid();");
                break;
            case FieldKind.DateTime:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadDateTime();");
                break;
            case FieldKind.DateTimeOffset:
                sb.Append("                    ").Append(target).AppendLine(" = reader.ReadDateTimeOffset();");
                break;
            case FieldKind.ActorRef:
                sb.Append("                    ").Append(target).AppendLine(" = ReadActorRef(reader);");
                break;
            case FieldKind.Enum:
                sb.Append("                    ").Append(target).Append(" = (").Append(field.TypeFullName).AppendLine(")reader.ReadInt32();");
                break;
            case FieldKind.Object:
                if (field.IsNullable)
                {
                    sb.AppendLine("                    if (reader.TryReadNil())");
                    sb.Append("                        ").Append(target).AppendLine(" = null;");
                    sb.AppendLine("                    else");
                    sb.Append("                        ").Append(target).Append(" = Read").Append(GetObjectMethodName(field.Mapping)).AppendLine("(reader);");
                }
                else
                {
                    sb.Append("                    ").Append(target).Append(" = Read").Append(GetObjectMethodName(field.Mapping)).AppendLine("(reader);");
                }
                break;
        }
    }

    private static TypeMapping MapType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (type.TypeKind == TypeKind.Enum)
            return new TypeMapping(FieldKind.Enum);

        if (type is INamedTypeSymbol namedType && namedType.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.SerializableAttribute)))
            return new TypeMapping(FieldKind.Object, GetFullyQualifiedTypeName(namedType));

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

        if (type is INamedTypeSymbol { IsGenericType: false, TypeKind: TypeKind.Class or TypeKind.Struct } missingNestedType)
            return new TypeMapping(FieldKind.MissingSerializableDefinition, GetFullyQualifiedTypeName(missingNestedType));

        return mapping;
    }

    private static string DefaultValue(FieldInfo field)
    {
        return field.Mapping.Kind switch
        {
            FieldKind.String => "null",
            FieldKind.Int32 => "0",
            FieldKind.Int64 => "0L",
            FieldKind.Boolean => "false",
            FieldKind.Double => "0.0",
            FieldKind.Decimal => "0m",
            FieldKind.ActorRef => "global::Akka.Actor.ActorRefs.NoSender",
            FieldKind.Object => "null",
            _ => "default"
        };
    }

    private static string GetLocalType(FieldInfo field)
    {
        return IsReferenceLike(field.Mapping) ? field.TypeFullName + "?" : field.TypeFullName;
    }

    private static bool IsRequired(FieldInfo field)
    {
        return !field.IsNullable;
    }

    private static bool IsReferenceLike(TypeMapping mapping)
    {
        return mapping.Kind is FieldKind.String or FieldKind.ActorRef or FieldKind.Object;
    }

    private static string GetHasLocalName(FieldInfo field)
    {
        return "has" + field.Name;
    }

    private static string GetConstructorArgument(FieldInfo field)
    {
        var name = ToCamelCase(field.Name);
        return IsRequired(field) && IsReferenceLike(field.Mapping) ? name + "!" : name;
    }

    private static string GetObjectMethodName(TypeMapping mapping)
    {
        return mapping.TypeFullName
            .Replace("global::", string.Empty)
            .Replace(".", "_")
            .Replace("+", "_");
    }

    private static string GetMessageMethodName(MessageInfo message)
    {
        return message.FullyQualifiedName
            .Replace("global::", string.Empty)
            .Replace(".", "_")
            .Replace("+", "_");
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

    private static string ToCamelCase(string value)
    {
        return string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class SerializerInfo
    {
        public SerializerInfo(string ns, string className, string fullyQualifiedName, string name, int serializerId, INamedTypeSymbol? protocolType, string protocolTypeFullName)
        {
            Namespace = ns;
            ClassName = className;
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            SerializerId = serializerId;
            ProtocolType = protocolType;
            ProtocolTypeFullName = protocolTypeFullName;
        }

        public string Namespace { get; }
        public string ClassName { get; }
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public int SerializerId { get; }
        public INamedTypeSymbol? ProtocolType { get; }
        public string ProtocolTypeFullName { get; }
    }

    private sealed class KnownTypes
    {
        private KnownTypes(Compilation compilation)
        {
            FieldAttribute = compilation.GetTypeByMetadataName(FieldAttributeFullName);
            SerializableAttribute = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
            Guid = compilation.GetTypeByMetadataName("System.Guid");
            DateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
            ActorRef = compilation.GetTypeByMetadataName("Akka.Actor.IActorRef");
        }

        public INamedTypeSymbol? FieldAttribute { get; }
        public INamedTypeSymbol? SerializableAttribute { get; }
        public INamedTypeSymbol? Guid { get; }
        public INamedTypeSymbol? DateTimeOffset { get; }
        public INamedTypeSymbol? ActorRef { get; }

        public static KnownTypes From(Compilation compilation)
        {
            return new KnownTypes(compilation);
        }
    }

    private sealed class MessageInfo
    {
        public MessageInfo(string simpleName, string fullyQualifiedName, string manifest, ImmutableArray<FieldInfo> fields, ImmutableArray<INamedTypeSymbol> protocols)
        {
            SimpleName = simpleName;
            FullyQualifiedName = fullyQualifiedName;
            Manifest = manifest;
            Fields = fields;
            Protocols = protocols;
        }

        public string SimpleName { get; }
        public string FullyQualifiedName { get; }
        public string Manifest { get; }
        public ImmutableArray<FieldInfo> Fields { get; }
        public ImmutableArray<INamedTypeSymbol> Protocols { get; }
    }

    private sealed class FieldInfo
    {
        public FieldInfo(int index, string name, string typeFullName, TypeMapping mapping, bool isNullable)
        {
            Index = index;
            Name = name;
            TypeFullName = typeFullName;
            Mapping = mapping;
            IsNullable = isNullable;
        }

        public int Index { get; }
        public string Name { get; }
        public string TypeFullName { get; }
        public TypeMapping Mapping { get; }
        public bool IsNullable { get; }
    }

    private readonly struct TypeMapping
    {
        public TypeMapping(FieldKind kind, string typeFullName = "")
        {
            Kind = kind;
            TypeFullName = typeFullName;
        }

        public FieldKind Kind { get; }
        public string TypeFullName { get; }
    }

    private enum FieldKind
    {
        Unsupported,
        String,
        Int32,
        Int64,
        Boolean,
        Double,
        Decimal,
        Guid,
        DateTime,
        DateTimeOffset,
        ActorRef,
        Enum,
        Object,
        MissingSerializableDefinition
    }
}
