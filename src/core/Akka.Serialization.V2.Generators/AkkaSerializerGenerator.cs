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
    private const string EnvelopePayloadAttributeFullName = "Akka.Serialization.V2.AkkaEnvelopePayloadAttribute";

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
            var isNullable = member.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValueType(member.Type);
            var isEnvelopePayload = member.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, knownTypes.EnvelopePayloadAttribute));
            var mapping = isEnvelopePayload ? new TypeMapping(FieldKind.EnvelopePayload) : MapType(member.Type, knownTypes);
            fields.Add(new FieldInfo(index, member.Name, member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), mapping, isNullable));
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
        GenerateSerializeDirect(sb, topLevelMessages);
        GenerateDeserialize(sb, topLevelMessages);
        GenerateSizeHint(sb, topLevelMessages);
        GenerateCountingBufferWriter(sb);

        foreach (var message in reachableMessages)
        {
            GenerateSizeMessage(sb, message);
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
        foreach (var field in message.Fields)
            GenerateSizeField(sb, field);
        sb.AppendLine("            return size;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateSizeField(StringBuilder sb, FieldInfo field)
    {
        var value = "message." + field.Name;
        var localName = ToCamelCase(field.Name) + "Size";
        sb.Append("            size += SizeOfInt32(").Append(field.Index).AppendLine(");");
        if (TryGetInlineSizeExpression(field, value, out var expression))
        {
            sb.Append("            size += ").Append(expression).AppendLine(";");
            return;
        }

        sb.Append("            var ").Append(localName).Append(" = ");
        GenerateSizeExpression(sb, field, value);
        sb.AppendLine(";");
        sb.Append("            if (").Append(localName).AppendLine(" < 0)");
        sb.AppendLine("                return global::Akka.Serialization.SerializerV2.UnknownSize;");
        sb.Append("            size += ").Append(localName).AppendLine(";");
    }

    private static bool TryGetInlineSizeExpression(FieldInfo field, string value, out string expression)
    {
        if (IsNullableValueField(field))
        {
            expression = value + " is null ? SizeOfNil() : " + GetScalarSizeExpression(field.Mapping, value + ".Value");
            return true;
        }

        if (field.Mapping.Kind != FieldKind.Object && field.Mapping.Kind != FieldKind.EnvelopePayload)
        {
            expression = GetScalarSizeExpression(field.Mapping, value);
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static void GenerateSizeExpression(StringBuilder sb, FieldInfo field, string value)
    {
        switch (field.Mapping.Kind)
        {
            case FieldKind.EnvelopePayload:
                sb.Append("SizeOfEnvelopePayload(").Append(value).Append(')');
                break;
            case FieldKind.Object when field.IsNullable:
                sb.Append(value).Append(" is null ? SizeOfNil() : SizeOf").Append(GetObjectMethodName(field.Mapping)).Append('(').Append(value).Append(')');
                break;
            case FieldKind.Object:
                sb.Append("SizeOf").Append(GetObjectMethodName(field.Mapping)).Append('(').Append(value).Append(')');
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
        foreach (var field in message.Fields)
            GenerateWriteField(sb, field);
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateReadMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private ").Append(message.FullyQualifiedName).Append(" Read").Append(GetMessageMethodName(message))
            .AppendLine("(ref global::MessagePack.MessagePackReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fieldCount = reader.ReadMapHeader();");
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
            GenerateReadField(sb, field);
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
        sb.Append("        writer.Write(").Append(field.Index).AppendLine(");");
        if (IsNullableValueField(field))
        {
            sb.Append("        if (").Append(value).AppendLine(" is null)");
            sb.AppendLine("            writer.WriteNil();");
            sb.AppendLine("        else");
            GenerateWriteFieldValue(sb, field, value + ".Value", "            ");
            return;
        }

        GenerateWriteFieldValue(sb, field, value, "        ");
    }

    private static void GenerateWriteFieldValue(StringBuilder sb, FieldInfo field, string value, string indent)
    {
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
                if (field.IsNullable)
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
        }
    }

    private static void GenerateReadField(StringBuilder sb, FieldInfo field)
    {
        var target = ToCamelCase(field.Name);
        if (IsNullableValueField(field))
        {
            sb.AppendLine("                    if (reader.TryReadNil())");
            sb.Append("                        ").Append(target).AppendLine(" = null;");
            sb.AppendLine("                    else");
            GenerateReadFieldValue(sb, field, target, "                        ");
            return;
        }

        if (field.Mapping.Kind is FieldKind.Object or FieldKind.EnvelopePayload && field.IsNullable)
        {
            sb.AppendLine("                    if (reader.TryReadNil())");
            sb.Append("                        ").Append(target).AppendLine(" = null;");
            sb.AppendLine("                    else");
            GenerateReadFieldValue(sb, field, target, "                        ");
            return;
        }

        GenerateReadFieldValue(sb, field, target, "                    ");
    }

    private static void GenerateReadFieldValue(StringBuilder sb, FieldInfo field, string target, string indent)
    {
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
        }
    }

    private static TypeMapping MapType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (TryGetNullableValueType(type, out var underlyingType))
            return MapType(underlyingType, knownTypes);

        if (type is INamedTypeSymbol enumType && type.TypeKind == TypeKind.Enum)
            return new TypeMapping(FieldKind.Enum, GetFullyQualifiedTypeName(enumType));

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return new TypeMapping(FieldKind.ByteArray);

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
        return mapping.Kind is FieldKind.String or FieldKind.ByteArray or FieldKind.ActorRef or FieldKind.EnvelopePayload or FieldKind.Object;
    }

    private static bool IsNullableValueField(FieldInfo field)
    {
        return field.IsNullable && !IsReferenceLike(field.Mapping);
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
            EnvelopePayloadAttribute = compilation.GetTypeByMetadataName(EnvelopePayloadAttributeFullName);
            SerializableAttribute = compilation.GetTypeByMetadataName(SerializableAttributeFullName);
            Guid = compilation.GetTypeByMetadataName("System.Guid");
            DateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
            ActorRef = compilation.GetTypeByMetadataName("Akka.Actor.IActorRef");
        }

        public INamedTypeSymbol? FieldAttribute { get; }
        public INamedTypeSymbol? EnvelopePayloadAttribute { get; }
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
        MissingSerializableDefinition
    }
}
