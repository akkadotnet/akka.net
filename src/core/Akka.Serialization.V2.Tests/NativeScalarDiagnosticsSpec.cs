//-----------------------------------------------------------------------
// <copyright file="NativeScalarDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Akka.Serialization.V2.Tests.Harness.DiagnosticSource;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Diagnostics around the native <c>TimeSpan</c> and small scalars (G-3): they no longer fail, a type that
/// is still unsupported gets an AKKASG007 that says what to do about it, and a registered formatter still
/// wins over a native encoding.
/// </summary>
public sealed class NativeScalarDiagnosticsSpec
{
    [Fact(DisplayName = "Should_NotReportAKKASG007_When_AFieldIsTimeSpanOrASmallScalar")]
    public void Should_NotReportAKKASG007_When_AFieldIsTimeSpanOrASmallScalar()
    {
        var run = Run(
            string.Empty,
            "[property: AkkaField(1)] TimeSpan A, [property: AkkaField(2)] float B, [property: AkkaField(3)] short C, [property: AkkaField(4)] byte D, " +
            "[property: AkkaField(5)] sbyte E, [property: AkkaField(6)] ushort F, [property: AkkaField(7)] uint G, [property: AkkaField(8)] ulong H, " +
            "[property: AkkaField(9)] char I, [property: AkkaField(10)] TimeSpan? J, [property: AkkaField(11)] List<TimeSpan> K");

        run.Errors.Should().BeEmpty();
    }

    [Theory(DisplayName = "Should_ReportAKKASG007NamingAFormatter_When_AFieldIsAForeignTypeWithNoSupport")]
    [InlineData("Uri", "System.Uri")]
    [InlineData("Half", "System.Half")]
    [InlineData("DateOnly", "System.DateOnly")]
    [InlineData("TimeOnly", "System.TimeOnly")]
    public void Should_ReportAKKASG007NamingAFormatter_When_AFieldIsAForeignTypeWithNoSupport(string fieldType, string expectedDisplayType)
    {
        var run = Run(string.Empty, $"[property: AkkaField(1)] {fieldType} Value");

        var message = run.Errors.Should().ContainSingle(d => d.Id == "AKKASG007").Subject.GetMessage(null);

        // Says what is wrong with THIS type and the fixes that exist for a type the author does not own.
        message.Should().Contain($"'{expectedDisplayType}'")
            .And.Contain("declared in assembly")
            .And.Contain($"[AkkaSerializerFormatter<{expectedDisplayType}, TFormatter>]")
            .And.Contain("'SampleSerializer'");

        // The stale wording is gone: no "yet" (cross-assembly [AkkaSerializable] has worked since #8534)
        // and no "declare the type in this assembly" (impossible for a BCL type).
        message.Should().NotContain("cannot read a schema").And.NotContain("declare the type in this assembly").And.NotContain("nested value object");
    }

    [Fact(DisplayName = "Should_UseTheRegisteredFormatter_When_AFormatterIsRegisteredForTimeSpan")]
    public void Should_UseTheRegisteredFormatter_When_AFormatterIsRegisteredForTimeSpan()
    {
        // A registered formatter takes precedence over a native encoding at field position today; collection
        // positions follow the same rule, so a serializer that already formats TimeSpan keeps doing so everywhere.
        const string formatter = """
            public sealed class SecondsFormatter : IAkkaMessagePackFormatter<TimeSpan>
            {
                public void Write(ref MessagePackWriter writer, TimeSpan value) => writer.Write(value.TotalSeconds);
                public TimeSpan Read(ref MessagePackReader reader) => TimeSpan.FromSeconds(reader.ReadDouble());
                public int SizeOf(TimeSpan value) => 9;
            }

            """;
        var source = DiagnosticSource.Header + formatter + """
            [AkkaSerializer<IProtocol>("sample", 199301)]
            [AkkaSerializerFormatter<TimeSpan, SecondsFormatter>]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "outer-v1")]
            public sealed record Outer(
                [property: AkkaField(1)] TimeSpan Single,
                [property: AkkaField(2)] List<TimeSpan> Many,
                [property: AkkaField(3)] Dictionary<string, TimeSpan?> ByName) : IProtocol;
            """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var generated = result.GeneratedSources["SampleSerializer.AkkaSerialization.g.cs"];
        generated.Should().NotContain(".Ticks").And.NotContain("new global::System.TimeSpan(");
        generated.Should().Contain("_akkaFormatter_System_TimeSpan.Write(ref writer, message.Single)");
        generated.Should().Contain("_akkaFormatter_System_TimeSpan.Write(ref writer, __item");
        generated.Should().Contain("_akkaFormatter_System_TimeSpan.Write(ref writer, __kvp");
    }
}
