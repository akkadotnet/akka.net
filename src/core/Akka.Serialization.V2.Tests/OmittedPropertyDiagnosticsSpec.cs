#nullable enable

using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class OmittedPropertyDiagnosticsSpec
{
    [Theory(DisplayName = "Should warn on each unmarked stored property before serializer registration")]
    [InlineData("public string Author { get; init; } = string.Empty;")]
    [InlineData("public string Author { get; set; } = string.Empty;")]
    [InlineData("public string Author { get; } = string.Empty;")]
    public void Should_WarnOnOmittedState_When_NoSerializerExists(string property)
    {
        var result = GeneratorTestHarness.Run($$"""
            using Akka.Serialization.V2;
            [AkkaSerializable]
            public sealed record Message
            {
                [AkkaField(0)] public string Content { get; init; } = string.Empty;
                {{property}}
            }
            """);
        var warning = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG046").Subject;
        warning.Severity.Should().Be(DiagnosticSeverity.Warning);
        warning.GetMessage().Should().Contain("Author").And.Contain("not serialized");
        warning.Location.GetLineSpan().StartLinePosition.Line.Should().Be(5);
    }

    [Fact(DisplayName = "Should warn about omitted positional record and generic properties")]
    public void Should_Warn_When_PositionalOrGenericStateIsOmitted()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            [AkkaSerializable]
            public sealed record Message<T>([property: AkkaField(0)] T Content, string Author);
            """);
        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG046");
    }

    [Fact(DisplayName = "Should not warn on computed or static properties or fields")]
    public void Should_NotWarn_When_MembersAreNotStoredInstanceProperties()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            [AkkaSerializable]
            public sealed record Message([property: AkkaField(0)] string Content)
            {
                public int Length => Content.Length;
                public int OtherLength { get { return Content.Length; } }
                public static string Category { get; set; } = "message";
                public string Field = "unserialized";
                private string PrivateState { get; set; } = "private";
            }
            """);
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG046");
    }

    [Fact(DisplayName = "Should keep AllowEmpty separate from omitted-property intent")]
    public void Should_WarnOnStoredState_When_AllowEmptyIsTrue()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            [AkkaSerializable(AllowEmpty = true)]
            public sealed record Message { public string Author { get; init; } = "unknown"; }
            """);
        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG046");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG004");
    }

    [Fact(DisplayName = "Should support local pragma suppression of intentional omitted state")]
    public void Should_SuppressWarning_When_PragmaDisablesIt()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            [AkkaSerializable]
            public sealed record Message([property: AkkaField(0)] string Content)
            {
                #pragma warning disable AKKASG046
                public string Author { get; init; } = "unknown";
                #pragma warning restore AKKASG046
                public string Recipient { get; init; } = "unknown";
            }
            """);
        var effective = CompilationWithAnalyzers.GetEffectiveDiagnostics(result.GeneratorDiagnostics, result.OutputCompilation);
        var warning = effective.Should().ContainSingle(d => d.Id == "AKKASG046").Subject;
        warning.GetMessage().Should().Contain("Recipient").And.NotContain("Author");
    }
}
