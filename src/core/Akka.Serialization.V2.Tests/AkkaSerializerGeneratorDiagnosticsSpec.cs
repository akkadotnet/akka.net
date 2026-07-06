//-----------------------------------------------------------------------
// <copyright file="AkkaSerializerGeneratorDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Serialization.V2.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class AkkaSerializerGeneratorDiagnosticsSpec
{
    [Fact(DisplayName = "Generator should fail compilation when nested value object lacks serialization definition")]
    public void Generator_should_fail_compilation_when_nested_value_object_lacks_serialization_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120501)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Inner Inner) : IProtocol;

            public sealed record Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Id == "AKKASG007" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("must be annotated with [AkkaSerializable]", StringComparison.Ordinal));

        diagnostic.Should().NotBeNull();
    }

    [Fact(DisplayName = "Generator should fail compilation when deep nested value object lacks serialization definition")]
    public void Generator_should_fail_compilation_when_deep_nested_value_object_lacks_serialization_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120502)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Middle Middle) : IProtocol;

            [AkkaSerializable]
            public sealed record Middle([property: AkkaField(1)] Inner Inner);

            public sealed record Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Id == "AKKASG007" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("must be annotated with [AkkaSerializable]", StringComparison.Ordinal));

        diagnostic.Should().NotBeNull();
    }

    [Fact(DisplayName = "Generator should report AKKASG008 when a registered formatter does not implement IAkkaMessagePackFormatter<T>")]
    public void Generator_should_report_AKKASG008_when_formatter_does_not_implement_interface()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed record Foreign(string Value);

            public sealed class NotAFormatter
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120601)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(NotAFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Foreign Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG008" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG008 when a registered formatter is abstract")]
    public void Generator_should_report_AKKASG008_when_formatter_is_abstract()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed record Foreign(string Value);

            public abstract class AbstractFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public abstract void Write(ref MessagePackWriter writer, Foreign value);
                public abstract Foreign Read(ref MessagePackReader reader);
                public abstract int SizeOf(Foreign value);
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120602)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(AbstractFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Foreign Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG008" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG009 when a serializer registers multiple formatters for the same target type")]
    public void Generator_should_report_AKKASG009_when_formatters_duplicate_target_type()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed record Foreign(string Value);

            public sealed class FirstFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
                public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
                public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            public sealed class SecondFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
                public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
                public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120603)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(FirstFormatter))]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(SecondFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Foreign Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG009" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG010 when a registered formatter has no usable constructor")]
    public void Generator_should_report_AKKASG010_when_formatter_has_no_usable_constructor()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed record Foreign(string Value);

            public sealed class NoUsableCtorFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public NoUsableCtorFormatter(string unused)
                {
                }

                public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
                public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
                public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120604)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(NoUsableCtorFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Foreign Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG010" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should not report AKKASG007 and should succeed when a formatter is registered for a previously-unsupported nested foreign type")]
    public void Generator_should_succeed_when_formatter_registered_for_foreign_nested_type()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed record Foreign(string Value);

            public sealed class ForeignFormatter : IAkkaMessagePackFormatter<Foreign>
            {
                public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
                public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
                public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120605)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(ForeignFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Foreign Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG007");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG011 when a formatter target type is an array")]
    public void Generator_should_report_AKKASG011_when_formatter_target_type_is_an_array()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed class IntArrayFormatter : IAkkaMessagePackFormatter<int[]>
            {
                public void Write(ref MessagePackWriter writer, int[] value) => writer.Write(value.Length);
                public int[] Read(ref MessagePackReader reader) => new int[reader.ReadInt32()];
                public int SizeOf(int[] value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120606)]
            [AkkaSerializerFormatter(typeof(int[]), typeof(IntArrayFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG011" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG011 when a formatter target type is a closed generic")]
    public void Generator_should_report_AKKASG011_when_formatter_target_type_is_a_closed_generic()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed class IntListFormatter : IAkkaMessagePackFormatter<List<int>>
            {
                public void Write(ref MessagePackWriter writer, List<int> value) => writer.Write(value.Count);
                public List<int> Read(ref MessagePackReader reader) => new List<int>(reader.ReadInt32());
                public int SizeOf(List<int> value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120607)]
            [AkkaSerializerFormatter(typeof(List<int>), typeof(IntListFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG011" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG003 when a generic field type shares its name with a non-generic formatter target")]
    public void Generator_should_report_AKKASG003_when_generic_field_type_shares_name_with_formatter_target()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MessagePack;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public sealed class Result
            {
            }

            public sealed class Result<T>
            {
            }

            public sealed class ResultFormatter : IAkkaMessagePackFormatter<Result>
            {
                public void Write(ref MessagePackWriter writer, Result value) => writer.WriteNil();
                public Result Read(ref MessagePackReader reader) { reader.ReadNil(); return new Result(); }
                public int SizeOf(Result value) => Akka.Serialization.SerializerV2.UnknownSize;
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120608)]
            [AkkaSerializerFormatter(typeof(Result), typeof(ResultFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Result<int> Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // The Result<int> field must NOT match the formatter registered for the non-generic
        // Result (formatter matching is on the arity-less type name): it stays unsupported and
        // fails with AKKASG003 instead of emitting ill-typed formatter code (CS1503).
        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG003" && diagnostic.Severity == DiagnosticSeverity.Error);
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS1503");
    }

    [Fact(DisplayName = "Generator should report AKKASG011 when a formatter target argument is null")]
    public void Generator_should_report_AKKASG011_when_formatter_target_argument_is_null()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120609)]
            [AkkaSerializerFormatter(null!, typeof(AddressFormatter))]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG011" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should still report AKKASG004 for a fieldless message that does not opt into AllowEmpty")]
    public void Generator_should_report_AKKASG004_for_fieldless_message_without_AllowEmpty()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120701)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "heartbeat-v1")]
            public sealed record ArteryHeartbeatRepro : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // The AllowEmpty opt-in exists specifically so this guardrail can stay strict by default:
        // a fieldless type is almost always a forgotten [AkkaField], so AKKASG004 must still fire
        // unless the author deliberately opts in.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG004" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("AllowEmpty", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG004 and should compile cleanly when a fieldless message opts into AllowEmpty")]
    public void Generator_should_not_report_AKKASG004_when_fieldless_message_opts_into_AllowEmpty()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120703)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "heartbeat-v1", AllowEmpty = true)]
            public sealed record ArteryHeartbeatRepro : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG004");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should not emit CS1503 for a required [AkkaSerializable] struct nested field")]
    public void Generator_should_not_emit_CS1503_for_required_struct_nested_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120702)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Inner InnerValue) : IProtocol;

            [AkkaSerializable]
            public readonly record struct Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        // IsReferenceLike used to return true unconditionally for FieldKind.Object, generating an
        // `Inner?`-vs-`Inner` mismatch (CS1503) for a value-type nested message used as a required
        // field. It must now thread the annotated type's is-value-type through, exactly like the
        // formatter escape hatch already does for FieldKind.Formatted.
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS1503");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should not emit CS1503 for an optional [AkkaSerializable] struct nested field")]
    public void Generator_should_not_emit_CS1503_for_optional_struct_nested_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer(Name = "sample", SerializerId = 120704)]
            public sealed partial class SampleSerializer : MessagePackSerializer<IProtocol>
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Inner? InnerValue) : IProtocol;

            [AkkaSerializable]
            public readonly record struct Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS1503");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "AkkaSerializationGeneratorDiagnostics",
            new[] { syntaxTree },
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new AkkaSerializerGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var generatorDiagnostics);

        return generatorDiagnostics.AddRange(updatedCompilation.GetDiagnostics());
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path)) ?? Enumerable.Empty<MetadataReference>();

        var explicitAssemblies = new[]
        {
            typeof(ActorSystem).Assembly,
            typeof(AkkaSerializerAttribute).Assembly,
            typeof(SerializerV2).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
            Assembly.GetExecutingAssembly()
        };

        return trustedAssemblies.Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First());
    }
}
