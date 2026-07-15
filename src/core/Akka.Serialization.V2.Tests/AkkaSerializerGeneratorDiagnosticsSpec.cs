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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120501)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120502)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120601)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(NotAFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120602)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(AbstractFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120603)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(FirstFormatter))]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(SecondFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120604)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(NoUsableCtorFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120605)]
            [AkkaSerializerFormatter(typeof(Foreign), typeof(ForeignFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120606)]
            [AkkaSerializerFormatter(typeof(int[]), typeof(IntArrayFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120607)]
            [AkkaSerializerFormatter(typeof(List<int>), typeof(IntListFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120608)]
            [AkkaSerializerFormatter(typeof(Result), typeof(ResultFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120609)]
            [AkkaSerializerFormatter(null!, typeof(AddressFormatter))]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120701)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120703)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120702)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120704)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120901)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocolA>(Name = "sample-a", SerializerId = 120902)]
            public sealed partial class SampleSerializerA : global::Akka.Serialization.V2.MessagePackSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocolB>(Name = "sample-b", SerializerId = 120903)]
            public sealed partial class SampleSerializerB : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocolA>(Name = "sample-a", SerializerId = 120904)]
            public sealed partial class SampleSerializerA : global::Akka.Serialization.V2.MessagePackSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializer<IProtocolB>(Name = "sample-b", SerializerId = 120904)]
            public sealed partial class SampleSerializerB : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121001)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121002)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121003)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121004)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121005)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121006)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 121007)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120701)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120702)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120703)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

    [Fact(DisplayName = "Generator should report AKKASG020 when an instantiation target is not generic")]
    public void Generator_should_report_AKKASG020_when_instantiation_target_is_not_generic()
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120705)]
            [AkkaSerializable<Plain>(Manifest = "plain-again-v1")]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var diagnostics = RunGenerator(source);

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG020" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Generator should report AKKASG022 when a generic protocol message has no instantiations")]
    public void Generator_should_report_AKKASG022_when_generic_message_has_no_instantiations()
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120706)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120707)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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

            [AkkaSerializer<IProtocol>(Name = "sample", SerializerId = 120708)]
            public sealed partial class SampleSerializer : global::Akka.Serialization.V2.MessagePackSerializer
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
