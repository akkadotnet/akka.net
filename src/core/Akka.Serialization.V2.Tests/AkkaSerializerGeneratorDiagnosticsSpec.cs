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

            [AkkaSerializer<IProtocol>("sample", 120501)]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120502)]
            public sealed partial class SampleSerializer : AkkaSerializer
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

    // "Generator should report AKKASG008 when a registered formatter does not implement
    // IAkkaMessagePackFormatter<T>" was deleted here: with AkkaSerializerFormatterAttribute<TTarget,
    // TFormatter> constrained `where TFormatter : IAkkaMessagePackFormatter<TTarget>`, a formatter
    // that does not implement the interface for the target type is now a compile-time error
    // (CS0311) at the attribute usage site -- [AkkaSerializerFormatter<Foreign, NotAFormatter>]
    // cannot compile, so the generator diagnostic this test asserted on can never fire.

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

            [AkkaSerializer<IProtocol>("sample", 120602)]
            [AkkaSerializerFormatter<Foreign, AbstractFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120603)]
            [AkkaSerializerFormatter<Foreign, FirstFormatter>]
            [AkkaSerializerFormatter<Foreign, SecondFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120604)]
            [AkkaSerializerFormatter<Foreign, NoUsableCtorFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120605)]
            [AkkaSerializerFormatter<Foreign, ForeignFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120606)]
            [AkkaSerializerFormatter<int[], IntArrayFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120607)]
            [AkkaSerializerFormatter<List<int>, IntListFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120608)]
            [AkkaSerializerFormatter<Result, ResultFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
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

    // "Generator should report AKKASG011 when a formatter target argument is null" was deleted
    // here: TTarget is now a generic type argument of AkkaSerializerFormatterAttribute<TTarget,
    // TFormatter>, and a type argument can never be null -- there is no syntax to write "no type"
    // where a type argument is expected, so [AkkaSerializerFormatter<null, ...>] cannot even be
    // written, let alone compile. AKKASG011's former null-target check is unreachable and was
    // removed from ExtractFormatters along with it.

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

            [AkkaSerializer<IProtocol>("sample", 120701)]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120703)]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120702)]
            public sealed partial class SampleSerializer : AkkaSerializer
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

            [AkkaSerializer<IProtocol>("sample", 120704)]
            public sealed partial class SampleSerializer : AkkaSerializer
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

    [Fact(DisplayName = "Generator should report AKKASG012 when top-level messages duplicate manifest")]
    public void Generator_should_report_AKKASG012_when_top_level_messages_duplicate_manifest()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 120901)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record MessageA([property: AkkaField(1)] string Value) : IProtocol;

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record MessageB([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG012" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("shared-v1", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("MessageA", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("MessageB", StringComparison.Ordinal));

        // Guards against a control-flow regression: if AKKASG012 stopped suppressing emission, the
        // generated deserializer would switch on the same manifest string constant twice, which the
        // C# compiler flags as an unreachable pattern (CS8510).
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8510");
    }

    [Fact(DisplayName = "Generator should not report AKKASG012 when the same manifest is reused across different serializers")]
    public void Generator_should_not_report_AKKASG012_when_manifest_reused_across_serializers()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocolA
            {
            }

            public interface IProtocolB
            {
            }

            [AkkaSerializer<IProtocolA>("sample-a", 120902)]
            public sealed partial class SampleSerializerA : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocolB>("sample-b", 120903)]
            public sealed partial class SampleSerializerB : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record MessageA([property: AkkaField(1)] string Value) : IProtocolA;

            [AkkaSerializable(Manifest = "shared-v1")]
            public sealed record MessageB([property: AkkaField(1)] string Value) : IProtocolB;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG012");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG013 when two serializers duplicate the same serializer id")]
    public void Generator_should_report_AKKASG013_when_serializers_duplicate_serializer_id()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocolA
            {
            }

            public interface IProtocolB
            {
            }

            [AkkaSerializer<IProtocolA>("sample-a", 120904)]
            public sealed partial class SampleSerializerA : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocolB>("sample-b", 120904)]
            public sealed partial class SampleSerializerB : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "message-a-v1")]
            public sealed record MessageA([property: AkkaField(1)] string Value) : IProtocolA;

            [AkkaSerializable(Manifest = "message-b-v1")]
            public sealed record MessageB([property: AkkaField(1)] string Value) : IProtocolB;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG013" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("120904", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("SampleSerializerA", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("SampleSerializerB", StringComparison.Ordinal));

        diagnostics.Count(d => d.Id == "AKKASG013").Should().Be(1);
    }

    [Fact(DisplayName = "Generator should not report AKKASG003 and should compile cleanly for natively-supported collection shapes")]
    public void Generator_should_not_report_AKKASG003_for_supported_collection_shapes()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121001)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable]
            public sealed record Reading([property: AkkaField(1)] string SensorId, [property: AkkaField(2)] double Value);

            [AkkaSerializable(Manifest = "collections-v1")]
            public sealed record Collections(
                [property: AkkaField(1)] int[] Ints,
                [property: AkkaField(2)] List<string> Names,
                [property: AkkaField(3)] IReadOnlyList<Reading> Readings,
                [property: AkkaField(4)] Dictionary<int, string> Map,
                [property: AkkaField(5)] List<List<int>> Matrix,
                [property: AkkaField(6)] Dictionary<string, List<Reading>> Grouped,
                [property: AkkaField(7)] List<int>? MaybeInts,
                [property: AkkaField(8)] List<int?> OptionalInts) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // The old ReadingBatch workaround existed because List<Reading> and friends hit AKKASG003; they
        // are now natively supported, so AKKASG003 must not fire and the generated collection code must
        // compile inside the in-memory compilation.
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG003");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should not report AKKASG003 for a List of a nested [AkkaSerializable] type")]
    public void Generator_should_not_report_AKKASG003_for_list_of_serializable_type()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121002)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable]
            public sealed record Reading([property: AkkaField(1)] string SensorId, [property: AkkaField(2)] double Value);

            [AkkaSerializable(Manifest = "batch-v1")]
            public sealed record Batch([property: AkkaField(1)] List<Reading> Readings) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG003");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should still report AKKASG003 for a genuinely unsupported exotic collection type")]
    public void Generator_should_report_AKKASG003_for_unsupported_exotic_type()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121003)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            // HashSet<T> is deliberately outside the supported set (T[], List<T>, IReadOnlyList<T>,
            // Dictionary<TKey,TValue>) -- the scope boundary must still fail with AKKASG003.
            [AkkaSerializable(Manifest = "exotic-v1")]
            public sealed record Exotic([property: AkkaField(1)] HashSet<int> Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG003" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG003 for a collection whose element type is unsupported")]
    public void Generator_should_report_AKKASG003_for_collection_of_unsupported_element()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Text;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121004)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "bad-element-v1")]
            public sealed record BadElement([property: AkkaField(1)] List<StringBuilder> Values) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // A collection whose element is unsupported collapses to unsupported so AKKASG003 fires with the
        // full field type -- it must not silently emit ill-typed code or an incorrect encoding.
        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG003" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG014 for a long-backed enum scalar field")]
    public void Generator_should_report_AKKASG014_for_long_backed_enum_scalar_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public enum LongStatus : long
            {
                A = 0,
                Big = long.MaxValue
            }

            [AkkaSerializer<IProtocol>("sample", 121005)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "long-enum-v1")]
            public sealed record LongEnumMessage([property: AkkaField(1)] LongStatus Status) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // Enums encode as int32; a long-backed enum value outside int32 range would silently truncate,
        // so it must be rejected at compile time -- naming both the enum and its backing type.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG014" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("LongStatus", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("long", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG014 for a uint-backed enum inside a List<>")]
    public void Generator_should_report_AKKASG014_for_uint_backed_enum_inside_list()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public enum UIntStatus : uint
            {
                A = 0,
                Big = uint.MaxValue
            }

            [AkkaSerializer<IProtocol>("sample", 121006)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "uint-enum-list-v1")]
            public sealed record UIntEnumListMessage([property: AkkaField(1)] List<UIntStatus> Statuses) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // The unsupported enum backing must propagate OUT of the collection so AKKASG014 fires naming
        // the enum -- not collapse to the generic AKKASG003.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG014" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("UIntStatus", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("uint", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG014 for int32-representable enum backings")]
    public void Generator_should_not_report_AKKASG014_for_int32_representable_enum_backings()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public enum IntStatus
            {
                A = 0,
                B = 1
            }

            public enum ShortStatus : short
            {
                A = 0,
                B = 1
            }

            [AkkaSerializer<IProtocol>("sample", 121007)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "ok-enum-v1")]
            public sealed record OkEnumMessage(
                [property: AkkaField(1)] IntStatus IntStatus,
                [property: AkkaField(2)] ShortStatus ShortStatus,
                [property: AkkaField(3)] List<ShortStatus> ShortStatuses) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG014");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG015 when a union member is not serializable")]
    public void Generator_should_report_AKKASG015_when_union_member_not_serializable()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public interface IEvent
            {
            }

            public sealed record NotSerializable(string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 120701)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaUnion(typeof(NotSerializable))] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG015" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG016 when a union member has no manifest")]
    public void Generator_should_report_AKKASG016_when_union_member_has_no_manifest()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public interface IEvent
            {
            }

            [AkkaSerializable]
            public sealed record ManifestlessMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 120702)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaUnion(typeof(ManifestlessMember))] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG016" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG018 when a union member is not assignable to the field type")]
    public void Generator_should_report_AKKASG018_when_union_member_not_assignable()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "unrelated-v1")]
            public sealed record Unrelated([property: AkkaField(1)] string Value);

            [AkkaSerializer<IProtocol>("sample", 120703)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaUnion(typeof(Unrelated))] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG018" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG019 when a union member type is declared more than once")]
    public void Generator_should_report_AKKASG019_when_union_member_declared_twice()
    {
        // AkkaUnionAttribute(Type first, params Type[] rest) makes an EMPTY member set
        // unrepresentable (the "at least one member type is required" half of AKKASG019 was
        // removed along with its impossible-to-write test), but a REPEATED member is still fully
        // representable -- [AkkaUnion(typeof(Repeated), typeof(Repeated))] compiles fine -- so this
        // half of the check remains.
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "repeated-v1")]
            public sealed record Repeated([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 120704)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaUnion(typeof(Repeated), typeof(Repeated))] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG019" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("declared more than once", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG020 when a closed generic registration target is not generic")]
    public void Generator_should_report_AKKASG020_when_closed_generic_registration_target_is_not_generic()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "plain-v1")]
            public sealed record Plain([property: AkkaField(1)] string Value) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 120705)]
            [AkkaSerializable<Plain>(Manifest = "plain-again-v1")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG020" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG022 when a generic protocol message has no closed generic registrations")]
    public void Generator_should_report_AKKASG022_when_generic_message_has_no_closed_generic_registrations()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 120706)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG022" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG023 when a closed generic field type is not registered")]
    public void Generator_should_report_AKKASG023_when_closed_generic_field_not_registered()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload);

            [AkkaSerializable]
            public sealed record Payload([property: AkkaField(1)] string Value);

            [AkkaSerializer<IProtocol>("sample", 120707)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] Wrapper<Payload> Inner) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG023" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report advisory AKKASG025 when a union member is not sealed")]
    public void Generator_should_report_AKKASG025_when_union_member_not_sealed()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaUnion(typeof(OpenMember), typeof(SealedMember))]
            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "open-v1")]
            public record OpenMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializable(Manifest = "sealed-v1")]
            public sealed record SealedMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 120708)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // Advisory only: fires for the unsealed member, stays Info severity, and does NOT fail the
        // build -- the union still generates and no errors are produced.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG025" &&
            diagnostic.Severity == DiagnosticSeverity.Info &&
            diagnostic.GetMessage(null).Contains("OpenMember", StringComparison.Ordinal));
        diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == "AKKASG025" &&
            diagnostic.GetMessage(null).Contains("SealedMember", StringComparison.Ordinal));
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG026 when no constructor maps every required parameter to an [AkkaField] property")]
    public void Generator_should_report_AKKASG026_when_no_constructor_matches_fields()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121101)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "no-ctor-v1")]
            public sealed class NoMatchingCtorMessage : IProtocol
            {
                public NoMatchingCtorMessage(int notAField)
                {
                }

                [AkkaField(1)] public string Value { get; set; } = string.Empty;
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG026" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("NoMatchingCtorMessage", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG026 when a property not covered by the constructor has no accessible setter")]
    public void Generator_should_report_AKKASG026_when_leftover_property_is_unsettable()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121102)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "unsettable-v1")]
            public sealed class UnsettableLeftoverMessage : IProtocol
            {
                public UnsettableLeftoverMessage(string a)
                {
                    A = a;
                }

                [AkkaField(1)] public string A { get; }

                [AkkaField(2)] public string B { get; } = string.Empty;
            }
            """;

        var diagnostics = RunGenerator(source);

        // "a" maps to "A" (unique case-insensitive match); "B" is left over with no setter at all.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG026" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("'B'", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG026 when a constructor parameter ambiguously matches multiple fields case-insensitively")]
    public void Generator_should_report_AKKASG026_when_case_insensitive_mapping_is_ambiguous()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121103)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "ambiguous-v1")]
            public sealed class AmbiguousMappingMessage : IProtocol
            {
                public AmbiguousMappingMessage(string id)
                {
                    Id = id;
                    ID = id;
                }

                [AkkaField(1)] public string Id { get; }

                [AkkaField(2)] public string ID { get; }
            }
            """;

        var diagnostics = RunGenerator(source);

        // "id" matches both "Id" and "ID" case-insensitively -- ambiguous, so it maps to neither;
        // the parameter has no default value, so the (only) constructor is not eligible at all.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG026" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("AmbiguousMappingMessage", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG027 when the selected constructor has a defaulted parameter not covered by any [AkkaField]")]
    public void Generator_should_report_AKKASG027_when_defaulted_parameter_uncovered()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121104)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "defaulted-param-v1")]
            public sealed class DefaultedParameterMessage : IProtocol
            {
                public DefaultedParameterMessage(string id, int maxRetries = 3)
                {
                    Id = id;
                    MaxRetries = maxRetries;
                }

                [AkkaField(1)] public string Id { get; }

                public int MaxRetries { get; }
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG027" &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.GetMessage(null).Contains("maxRetries", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("DefaultedParameterMessage", StringComparison.Ordinal));

        // Advisory only: the constructor is still eligible (maxRetries simply keeps its default), so
        // the type still generates and compiles cleanly.
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG028 when an [AkkaField] property is static")]
    public void Generator_should_report_AKKASG028_when_field_property_is_static()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121105)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "static-field-v1")]
            public sealed class StaticFieldMessage : IProtocol
            {
                [AkkaField(1)] public static string StaticValue { get; set; } = string.Empty;

                [AkkaField(2)] public string Value { get; set; } = string.Empty;
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG028" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("StaticValue", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("static", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG028 when an [AkkaField] property has no accessible getter")]
    public void Generator_should_report_AKKASG028_when_field_property_is_private()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121106)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "private-field-v1")]
            public sealed class PrivateFieldMessage : IProtocol
            {
                public PrivateFieldMessage(string other)
                {
                    Other = other;
                }

                [AkkaField(1)] private string PrivateValue { get; set; } = string.Empty;

                [AkkaField(2)] public string Other { get; }
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG028" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("PrivateValue", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("accessible getter", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG029 when a protocol message forgets [AkkaSerializable]")]
    public void Generator_should_report_AKKASG029_when_protocol_message_missing_attribute()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130001)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;

            // Forgotten [AkkaSerializable]: silently invisible to the generated switches today.
            public sealed record Forgotten(string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG029" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("Forgotten", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("SampleSerializer", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG029 for an abstract base implementing the protocol")]
    public void Generator_should_not_report_AKKASG029_for_abstract_base()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130002)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            // Never a concrete runtime message type; its concrete subtypes are checked individually.
            public abstract class AbstractBase : IProtocol
            {
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG029");
    }

    [Fact(DisplayName = "Generator should not report AKKASG029 for an [AkkaSerializable]-marked generic definition with a registration")]
    public void Generator_should_not_report_AKKASG029_for_marked_generic_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 130003)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        // Governed entirely by AKKASG022's registration machinery -- this compiles fully clean.
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG029 for an unmarked generic definition implementing the protocol")]
    public void Generator_should_report_AKKASG029_for_unmarked_generic_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130004)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            // No [AkkaSerializable]: none of its closed constructions could ever be registered.
            public sealed record UnmarkedWrapper<T>(T Payload) : IProtocol;

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG029" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("UnmarkedWrapper", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator observes the compiler reject multiple [AkkaSerializer<T>] attributes on one class (CS0579, no AKKASG030 needed)")]
    public void Generator_observes_compiler_rejects_multiple_serializer_attributes()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocolA
            {
            }

            public interface IProtocolB
            {
            }

            [AkkaSerializer<IProtocolA>("sample-a", 130005)]
            [AkkaSerializer<IProtocolB>("sample-b", 130006)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        // AkkaSerializerAttribute<TProtocol> declares AllowMultiple = false; the C# compiler
        // enforces that against the OPEN generic attribute definition, not each closed
        // construction, so two [AkkaSerializer<T>] attributes with DIFFERENT protocol types on
        // one class are already rejected at the language level (CS0579) -- pinning this so a
        // future compiler/language change that silently admits it does not go unnoticed.
        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CS0579");
    }

    [Fact(DisplayName = "Generator should report AKKASG031 when two [AkkaSerializer] classes bind the same protocol")]
    public void Generator_should_report_AKKASG031_when_protocol_bound_by_multiple_serializers()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample-a", 130007)]
            public sealed partial class SerializerA : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocol>("sample-b", 130008)]
            public sealed partial class SerializerB : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG031" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("SerializerA", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("SerializerB", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG031 when two [AkkaSerializer] classes bind different protocols")]
    public void Generator_should_not_report_AKKASG031_for_distinct_protocols()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocolA
            {
            }

            public interface IProtocolB
            {
            }

            [AkkaSerializer<IProtocolA>("sample-a", 130009)]
            public sealed partial class SerializerA : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocolB>("sample-b", 130010)]
            public sealed partial class SerializerB : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-a-v1")]
            public sealed record OuterA([property: AkkaField(1)] string Value) : IProtocolA;

            [AkkaSerializable(Manifest = "outer-b-v1")]
            public sealed record OuterB([property: AkkaField(1)] string Value) : IProtocolB;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG031");
    }

    [Fact(DisplayName = "Generator should report AKKASG032 when the [AkkaSerializer] class is not partial")]
    public void Generator_should_report_AKKASG032_when_not_partial()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130011)]
            public sealed class NotPartialSerializer : AkkaSerializer
            {
                public NotPartialSerializer(ExtendedActorSystem system) : base(system)
                {
                }
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG032" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("NotPartialSerializer", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("partial", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG032 when the [AkkaSerializer] class does not derive from AkkaSerializer")]
    public void Generator_should_report_AKKASG032_when_wrong_base_class()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130012)]
            public sealed partial class WrongBaseSerializer
            {
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG032" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("WrongBaseSerializer", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("AkkaSerializer", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should report AKKASG032 when the [AkkaSerializer] class is generic")]
    public void Generator_should_report_AKKASG032_when_generic()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130013)]
            public sealed partial class GenericSerializer<T> : AkkaSerializer
            {
                public GenericSerializer(ExtendedActorSystem system) : base(system)
                {
                }
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG032" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("GenericSerializer", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("generic", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG032 for a well-formed [AkkaSerializer] class")]
    public void Generator_should_not_report_AKKASG032_for_valid_shape()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130014)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG032");
    }

    [Fact(DisplayName = "Generator should report AKKASG033 when the protocol type is not an interface")]
    public void Generator_should_report_AKKASG033_when_protocol_type_not_interface()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public class ClassProtocol
            {
            }

            [AkkaSerializer<ClassProtocol>("sample", 130015)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG033" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("SampleSerializer", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("ClassProtocol", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG033 when the protocol type is an interface")]
    public void Generator_should_not_report_AKKASG033_for_interface_protocol()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 130016)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG033");
    }

    [Fact(DisplayName = "Generator should report AKKASG034 when a closed generic registration neither implements the protocol nor is referenced anywhere")]
    public void Generator_should_report_AKKASG034_when_registration_is_orphaned()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload);

            [AkkaSerializer<IProtocol>("sample", 130017)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG034" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("Wrapper", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("SampleSerializer", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG034 when a closed generic registration is reachable as a nested field")]
    public void Generator_should_not_report_AKKASG034_when_registration_reachable_as_nested_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload);

            [AkkaSerializable]
            public sealed record Payload([property: AkkaField(1)] string Value);

            [AkkaSerializer<IProtocol>("sample", 130018)]
            [AkkaSerializable<Wrapper<Payload>>]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] Wrapper<Payload> Inner) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG034");
    }

    [Fact(DisplayName = "Generator should not report AKKASG034 when a closed generic registration implements the protocol")]
    public void Generator_should_not_report_AKKASG034_when_registration_implements_protocol()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 130019)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report advisory AKKASG035 when a field declares both [AkkaEnvelopePayload] and [AkkaUnion]")]
    public void Generator_should_report_AKKASG035_when_envelope_and_union_share_a_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "inner-v1")]
            public sealed record Inner([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 140101)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaEnvelopePayload, AkkaUnion(typeof(Inner))] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // Advisory only: envelope payload wins (its documented precedence), the union declaration
        // is dropped during extraction, and the serializer still generates without errors.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG035" &&
            diagnostic.Severity == DiagnosticSeverity.Info &&
            diagnostic.GetMessage(null).Contains("Event", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("Outer", StringComparison.Ordinal));
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should not report AKKASG035 for an envelope payload field whose static type declares a type-level [AkkaUnion]")]
    public void Generator_should_not_report_AKKASG035_for_type_level_union()
    {
        // The type-level [AkkaUnion] on IEvent serves that interface's other, non-envelope fields;
        // its presence on an envelope payload field's static type is incidental, not a conflicting
        // author intent, so the advisory deliberately stays quiet here.
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaUnion(typeof(Inner))]
            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "inner-v1")]
            public sealed record Inner([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 140102)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1), AkkaEnvelopePayload] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG035");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report advisory AKKASG036 when a union member is abstract")]
    public void Generator_should_report_AKKASG036_when_union_member_is_abstract()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaUnion(typeof(AbstractMember), typeof(OpenMember), typeof(SealedMember))]
            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "abstract-v1")]
            public abstract record AbstractMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializable(Manifest = "open-v1")]
            public record OpenMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializable(Manifest = "sealed-v1")]
            public sealed record SealedMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 140201)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // Exact-runtime-type dispatch can never select an abstract member -- Warning, not Info.
        // (Other, pre-existing errors fire alongside it here: an abstract record cannot be
        // reconstructed on deserialize either, so AKKASG026 also triggers -- 036 exists precisely
        // to name the root cause of that confusing combination.)
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG036" &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.GetMessage(null).Contains("AbstractMember", StringComparison.Ordinal));

        // An abstract member fires AKKASG036 ONLY -- stacking the weaker unsealed advisory on the
        // same member would be noise; the merely-unsealed member keeps its AKKASG025.
        diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == "AKKASG025" &&
            diagnostic.GetMessage(null).Contains("AbstractMember", StringComparison.Ordinal));
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG025" &&
            diagnostic.Severity == DiagnosticSeverity.Info &&
            diagnostic.GetMessage(null).Contains("OpenMember", StringComparison.Ordinal));
        diagnostics.Should().NotContain(diagnostic =>
            (diagnostic.Id == "AKKASG025" || diagnostic.Id == "AKKASG036") &&
            diagnostic.GetMessage(null).Contains("SealedMember", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not report AKKASG036 when every union member is concrete")]
    public void Generator_should_not_report_AKKASG036_for_concrete_members()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaUnion(typeof(OpenMember), typeof(SealedMember))]
            public interface IEvent
            {
            }

            [AkkaSerializable(Manifest = "open-v1")]
            public record OpenMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializable(Manifest = "sealed-v1")]
            public sealed record SealedMember([property: AkkaField(1)] string Value) : IEvent;

            [AkkaSerializer<IProtocol>("sample", 140202)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] IEvent Event) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG036");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report advisory AKKASG037 when a generic [AkkaSerializable] definition specifies Manifest")]
    public void Generator_should_report_AKKASG037_when_generic_definition_specifies_manifest()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable(Manifest = "wrapper-v1")]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 140301)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        // Advisory only: the definition's Manifest is silently ignored (each registered closed
        // construction supplies its own), and the serializer still generates without errors.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG037" &&
            diagnostic.Severity == DiagnosticSeverity.Info &&
            diagnostic.GetMessage(null).Contains("Wrapper", StringComparison.Ordinal) &&
            diagnostic.GetMessage(null).Contains("wrapper-v1", StringComparison.Ordinal));
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should not report AKKASG037 for a manifest-less generic definition or a non-generic message with Manifest")]
    public void Generator_should_not_report_AKKASG037_without_manifest_on_definition()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializable]
            public sealed record Wrapper<T>(
                [property: AkkaField(1)] string Id,
                [property: AkkaField(2)] T Payload) : IProtocol;

            [AkkaSerializer<IProtocol>("sample", 140302)]
            [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] string Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG037");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // Immutable / read-only collection shapes (openspec task 5.7): ImmutableArray<T>,
    // ImmutableList<T>, ImmutableHashSet<T>, ImmutableDictionary<TKey,TValue>,
    // IReadOnlyCollection<T>, IReadOnlyDictionary<TKey,TValue>. Round-trip/wire-format coverage
    // lives in ImmutableCollectionFieldSpec.cs; these tests are the AKKASG003 scope-boundary
    // checks (compiles cleanly when supported, still collapses to Unsupported when the
    // element/value type is not).
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Generator should not report AKKASG003 and should compile cleanly for immutable/read-only collection shapes")]
    public void Generator_should_not_report_AKKASG003_for_immutable_collection_shapes()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121005)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable]
            public sealed record Reading([property: AkkaField(1)] string SensorId, [property: AkkaField(2)] double Value);

            [AkkaSerializable(Manifest = "immutable-collections-v1")]
            public sealed record ImmutableCollections(
                [property: AkkaField(1)] ImmutableArray<int> Ints,
                [property: AkkaField(2)] ImmutableList<string> Names,
                [property: AkkaField(3)] ImmutableHashSet<int> UniqueInts,
                [property: AkkaField(4)] ImmutableDictionary<int, string> Map,
                [property: AkkaField(5)] IReadOnlyCollection<Reading> Readings,
                [property: AkkaField(6)] IReadOnlyDictionary<string, int> Counts,
                [property: AkkaField(7)] ImmutableList<Reading> NestedReadings,
                [property: AkkaField(8)] ImmutableDictionary<string, List<int>> Grouped,
                [property: AkkaField(9)] ImmutableList<int>? MaybeInts,
                [property: AkkaField(10)] ImmutableList<int?> OptionalInts) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AKKASG003");
        diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Generator should report AKKASG003 for each immutable/read-only collection shape when its element or value type is unsupported")]
    public void Generator_should_report_AKKASG003_for_immutable_collection_of_unsupported_element()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Text;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121006)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "bad-elements-v1")]
            public sealed record BadElements(
                [property: AkkaField(1)] ImmutableArray<StringBuilder> ImmutableArrayValues,
                [property: AkkaField(2)] ImmutableList<StringBuilder> ImmutableListValues,
                [property: AkkaField(3)] ImmutableHashSet<StringBuilder> ImmutableHashSetValues,
                [property: AkkaField(4)] ImmutableDictionary<string, StringBuilder> ImmutableDictionaryValues,
                [property: AkkaField(5)] IReadOnlyCollection<StringBuilder> ReadOnlyCollectionValues,
                [property: AkkaField(6)] IReadOnlyDictionary<string, StringBuilder> ReadOnlyDictionaryValues) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // Every one of the six shapes must independently collapse its field to Unsupported so
        // AKKASG003 fires once per field -- none may silently emit ill-typed code instead.
        diagnostics.Count(diagnostic => diagnostic.Id == "AKKASG003" && diagnostic.Severity == DiagnosticSeverity.Error).Should().Be(6);
    }

    [Fact(DisplayName = "Generator should still report AKKASG003 for a mutable HashSet<T>/Dictionary<K,V>-adjacent type outside the immutable scope boundary")]
    public void Generator_should_report_AKKASG003_for_types_outside_immutable_scope_boundary()
    {
        const string source = """
            #nullable enable
            using System.Collections.Immutable;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121007)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            // ImmutableSortedSet<T>/ImmutableSortedDictionary/ImmutableQueue/ImmutableStack are
            // deliberately outside the approved scope (ImmutableArray<T>, ImmutableList<T>,
            // ImmutableHashSet<T>, ImmutableDictionary<TKey,TValue> only) -- the boundary must still
            // fail with AKKASG003 rather than silently widening support.
            [AkkaSerializable(Manifest = "exotic-immutable-v1")]
            public sealed record ExoticImmutable([property: AkkaField(1)] ImmutableSortedSet<int> Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG003" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should append the [AkkaEnvelopePayload]/[AkkaUnion] hint to AKKASG003 when the unsupported field type is an interface")]
    public void Generator_should_append_polymorphism_hint_to_AKKASG003_for_interface_field()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            public interface IUnannotated
            {
            }

            [AkkaSerializer<IProtocol>("sample", 199001)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] IUnannotated Value) : IProtocol;
            """;

        var diagnostics = RunGenerator(source);

        // Same id/title/severity as every other AKKASG003 -- this is a second descriptor variant,
        // not a format-string branch, so a genuinely unrepresentable type's message (asserted
        // elsewhere in this file) stays byte-identical to before this hint existed.
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "AKKASG003" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage(null).Contains("Mark the property [AkkaEnvelopePayload], or declare a closed member set with [AkkaUnion].", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Generator should not append the cross-assembly hint to AKKASG007 when the nested type is declared in this same compilation")]
    public void Generator_should_not_append_cross_assembly_hint_to_AKKASG007_for_same_assembly_type()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace DiagnosticSample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 199002)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer([property: AkkaField(1)] Inner Inner) : IProtocol;

            public sealed record Inner([property: AkkaField(1)] string Value);
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = diagnostics.FirstOrDefault(d => d.Id == "AKKASG007" && d.Severity == DiagnosticSeverity.Error);
        diagnostic.Should().NotBeNull();

        // The cross-assembly descriptor variant only fires when the nested type's ContainingAssembly
        // differs from the compilation being generated for; Inner is declared right here, so the
        // message must stay exactly the pre-hint text -- no assembly name, no formatter suggestion.
        diagnostic!.GetMessage(null).Contains("is declared in assembly", StringComparison.Ordinal).Should().BeFalse();
        diagnostic.GetMessage(null).Contains("AkkaSerializerFormatter<", StringComparison.Ordinal).Should().BeFalse();
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
            typeof(AkkaSerializerAttribute<>).Assembly,
            typeof(SerializerV2).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
            Assembly.GetExecutingAssembly()
        };

        return trustedAssemblies.Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First());
    }
}
