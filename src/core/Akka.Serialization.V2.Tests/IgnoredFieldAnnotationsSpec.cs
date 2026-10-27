#nullable enable

using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class IgnoredFieldAnnotationsSpec
{
    [Theory(DisplayName = "Should warn about ignored field annotations on unmarked public and internal types")]
    [InlineData("public")]
    [InlineData("internal")]
    public void Should_Warn_When_ContainingTypeIsUnmarked(string accessibility)
    {
        var source = $$"""
            using Akka.Serialization.V2;
            {{accessibility}} sealed record Payload([property: AkkaField(0)] int Value);
            """;

        var result = GeneratorTestHarness.Run(source);

        var diagnostic = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG047").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Contain("Payload").And.Contain("[AkkaSerializable]");
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length).Should().Be("Payload");
        result.CompileDiagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Should report ignored annotations once per partial containing type")]
    public void Should_ReportOnce_When_UnmarkedTypeIsPartial()
    {
        var result = GeneratorTestHarness.Run(new[]
        {
            new SourceFile("First.cs", """
                using Akka.Serialization.V2;
                public sealed partial class Payload
                {
                    [AkkaField(0)] public int First { get; init; }
                }
                """),
            new SourceFile("Second.cs", """
                using Akka.Serialization.V2;
                public sealed partial class Payload
                {
                    [AkkaField(1)] public int Second { get; init; }
                }
                """)
        });

        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG047");
    }

    [Fact(DisplayName = "Should accept field annotations on marked nested and generic payloads")]
    public void Should_NotWarn_When_ContainingTypeIsMarked()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            [AkkaSerializable]
            public sealed record Payload([property: AkkaField(0)] int Value);
            [AkkaSerializable]
            public sealed record Envelope<T>([property: AkkaField(0)] T Value);
            """);

        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG047");
    }

    [Fact(DisplayName = "Should allow standard suppression of ignored field annotations")]
    public void Should_SuppressWarning_When_PragmaDisablesDiagnostic()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            #pragma warning disable AKKASG047
            public sealed record Payload([property: AkkaField(0)] int Value);
            #pragma warning restore AKKASG047
            """);

        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG047");
    }
}
