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

                var messagesForSerializer = pair.Right
                    .Where(message => message != null && message.Protocols.Contains(serializer.ProtocolTypeFullName))
                    .Cast<MessageInfo>()
                    .ToImmutableArray();

                ValidateMessages(ctx, messagesForSerializer);
                ctx.AddSource(serializer.ClassName + ".AkkaSerialization.g.cs", Generate(serializer, messagesForSerializer));
            }
        });
    }

    private static SerializerInfo? ExtractSerializer(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
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
        while (baseType != null)
        {
            if (baseType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::Akka.Serialization.V2.MessagePackSerializer<TProtocol>")
            {
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
            protocolTypeFullName);
    }

    private static MessageInfo? ExtractMessage(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var attribute = context.Attributes[0];
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
                .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == FieldAttributeFullName);
            if (fieldAttribute == null || fieldAttribute.ConstructorArguments.Length != 1)
                continue;

            var index = (int)fieldAttribute.ConstructorArguments[0].Value!;
            fields.Add(new FieldInfo(index, member.Name, member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), MapType(member.Type)));
        }

        return new MessageInfo(
            symbol.Name,
            GetFullyQualifiedTypeName(symbol),
            manifest,
            fields.OrderBy(f => f.Index).ToImmutableArray(),
            symbol.AllInterfaces.Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToImmutableHashSet());
    }

    private static void ValidateMessages(SourceProductionContext context, ImmutableArray<MessageInfo> messages)
    {
        foreach (var message in messages)
        {
            if (message.Fields.Length == 0)
                context.ReportDiagnostic(Diagnostic.Create(MissingFields, Location.None, message.FullyQualifiedName));

            foreach (var duplicate in message.Fields.GroupBy(field => field.Index).Where(group => group.Count() > 1))
                context.ReportDiagnostic(Diagnostic.Create(DuplicateFieldIndex, Location.None, message.FullyQualifiedName, duplicate.Key));

            foreach (var field in message.Fields.Where(field => field.Mapping.Kind == FieldKind.Unsupported))
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedFieldType, Location.None, field.Name, message.FullyQualifiedName, field.TypeFullName));
        }
    }

    private static string Generate(SerializerInfo serializer, ImmutableArray<MessageInfo> messages)
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
        GenerateManifest(sb, messages);
        GenerateSerialize(sb, messages);
        GenerateDeserialize(sb, messages);
        sb.AppendLine("    public override int SizeHint(object obj) => 128;");
        sb.AppendLine();

        foreach (var message in messages)
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
        sb.AppendLine("    public static partial global::Akka.Serialization.SerializationSetup CreateSetup()");
        sb.AppendLine("    {");
        sb.AppendLine("        return CreateRegistration().CreateSetup();");
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
            sb.Append("                Write").Append(message.SimpleName).AppendLine("(akkaWriter, message);");
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
            sb.Append("            \"").Append(Escape(message.Manifest)).Append("\" => Read").Append(message.SimpleName).AppendLine("(reader),");
        sb.AppendLine("            _ => throw new global::System.Runtime.Serialization.SerializationException($\"Unknown generated serializer manifest [{manifest}] for serializer [{GetType()}].\")");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateWriteMessage(StringBuilder sb, MessageInfo message)
    {
        sb.Append("    private static void Write").Append(message.SimpleName)
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
        sb.Append("    private ").Append(message.FullyQualifiedName).Append(" Read").Append(message.SimpleName)
            .AppendLine("(global::Akka.Serialization.V2.AkkaReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fieldCount = reader.BeginReadObject();");
        foreach (var field in message.Fields)
            sb.Append("        ").Append(GetLocalType(field)).Append(' ').Append(ToCamelCase(field.Name)).Append(" = ")
                .Append(DefaultValue(field.Mapping)).AppendLine(";");

        foreach (var field in message.Fields)
        {
            sb.Append("        if (fieldCount > ").Append(field.Index).AppendLine(")");
            sb.AppendLine("        {");
            GenerateReadField(sb, field);
            sb.AppendLine("        }");
        }

        sb.Append("        for (var index = ").Append(message.Fields.Length).AppendLine("; index < fieldCount; index++)");
        sb.AppendLine("        {");
        sb.AppendLine("            reader.SkipField();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        return new ").Append(message.FullyQualifiedName).Append('(')
            .Append(string.Join(", ", message.Fields.Select(field => ToCamelCase(field.Name))))
            .AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateWriteField(StringBuilder sb, FieldInfo field)
    {
        var value = "message." + field.Name;
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
        }
    }

    private static void GenerateReadField(StringBuilder sb, FieldInfo field)
    {
        var target = ToCamelCase(field.Name);
        switch (field.Mapping.Kind)
        {
            case FieldKind.String:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadString() ?? string.Empty;");
                break;
            case FieldKind.Int32:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadInt32();");
                break;
            case FieldKind.Int64:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadInt64();");
                break;
            case FieldKind.Boolean:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadBoolean();");
                break;
            case FieldKind.Double:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadDouble();");
                break;
            case FieldKind.Decimal:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadDecimal();");
                break;
            case FieldKind.Guid:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadGuid();");
                break;
            case FieldKind.DateTime:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadDateTime();");
                break;
            case FieldKind.DateTimeOffset:
                sb.Append("            ").Append(target).AppendLine(" = reader.ReadDateTimeOffset();");
                break;
            case FieldKind.ActorRef:
                sb.Append("            ").Append(target).AppendLine(" = ReadActorRef(reader);");
                break;
            case FieldKind.Enum:
                sb.Append("            ").Append(target).Append(" = (").Append(field.TypeFullName).AppendLine(")reader.ReadInt32();");
                break;
        }
    }

    private static TypeMapping MapType(ITypeSymbol type)
    {
        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type.TypeKind == TypeKind.Enum)
            return new TypeMapping(FieldKind.Enum);

        return type.SpecialType switch
        {
            SpecialType.System_String => new TypeMapping(FieldKind.String),
            SpecialType.System_Int32 => new TypeMapping(FieldKind.Int32),
            SpecialType.System_Int64 => new TypeMapping(FieldKind.Int64),
            SpecialType.System_Boolean => new TypeMapping(FieldKind.Boolean),
            SpecialType.System_Double => new TypeMapping(FieldKind.Double),
            SpecialType.System_Decimal => new TypeMapping(FieldKind.Decimal),
            SpecialType.System_DateTime => new TypeMapping(FieldKind.DateTime),
            _ when fullName == "global::System.Guid" => new TypeMapping(FieldKind.Guid),
            _ when fullName == "global::System.DateTimeOffset" => new TypeMapping(FieldKind.DateTimeOffset),
            _ when fullName == "global::Akka.Actor.IActorRef" => new TypeMapping(FieldKind.ActorRef),
            _ => new TypeMapping(FieldKind.Unsupported)
        };
    }

    private static string DefaultValue(TypeMapping mapping)
    {
        return mapping.Kind switch
        {
            FieldKind.String => "string.Empty",
            FieldKind.Int32 => "0",
            FieldKind.Int64 => "0L",
            FieldKind.Boolean => "false",
            FieldKind.Double => "0.0",
            FieldKind.Decimal => "0m",
            FieldKind.ActorRef => "global::Akka.Actor.ActorRefs.NoSender",
            _ => "default"
        };
    }

    private static string GetLocalType(FieldInfo field)
    {
        return field.Mapping.Kind == FieldKind.ActorRef ? "global::Akka.Actor.IActorRef?" : field.TypeFullName;
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
        public SerializerInfo(string ns, string className, string fullyQualifiedName, string name, int serializerId, string protocolTypeFullName)
        {
            Namespace = ns;
            ClassName = className;
            FullyQualifiedName = fullyQualifiedName;
            Name = name;
            SerializerId = serializerId;
            ProtocolTypeFullName = protocolTypeFullName;
        }

        public string Namespace { get; }
        public string ClassName { get; }
        public string FullyQualifiedName { get; }
        public string Name { get; }
        public int SerializerId { get; }
        public string ProtocolTypeFullName { get; }
    }

    private sealed class MessageInfo
    {
        public MessageInfo(string simpleName, string fullyQualifiedName, string manifest, ImmutableArray<FieldInfo> fields, ImmutableHashSet<string> protocols)
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
        public ImmutableHashSet<string> Protocols { get; }
    }

    private sealed class FieldInfo
    {
        public FieldInfo(int index, string name, string typeFullName, TypeMapping mapping)
        {
            Index = index;
            Name = name;
            TypeFullName = typeFullName;
            Mapping = mapping;
        }

        public int Index { get; }
        public string Name { get; }
        public string TypeFullName { get; }
        public TypeMapping Mapping { get; }
    }

    private readonly struct TypeMapping
    {
        public TypeMapping(FieldKind kind)
        {
            Kind = kind;
        }

        public FieldKind Kind { get; }
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
        Enum
    }
}
