#nullable enable

using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class DeclarationDiagnosticsSpec
{
    [Theory(DisplayName = "Should report declaration errors without a serializer")]
    [InlineData("[AkkaSerializable] public sealed record Message(int Value);", "AKKASG004")]
    [InlineData("[AkkaSerializable] public sealed record Message([property: AkkaField(0)] int A, [property: AkkaField(0)] int B);", "AKKASG005")]
    [InlineData("[AkkaSerializable] public sealed record Message<T>([property: AkkaField(0)] T A, [property: AkkaField(0)] T B);", "AKKASG005")]
    [InlineData("[AkkaSerializable] public sealed record Message<T>(T Value);", "AKKASG004")]
    [InlineData("[AkkaSerializable] public sealed class Message { [AkkaField(0)] public static int Value => 1; }", "AKKASG028")]
    [InlineData("public class Outer { [AkkaSerializable(AllowEmpty = true)] private sealed record Message; }", "AKKASG045")]
    public void Should_ReportDeclarationError_When_NoSerializerExists(string declaration, string id)
    {
        var result = GeneratorTestHarness.Run("using Akka.Serialization.V2;\n" + declaration);
        var diagnostic = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == id).Subject;
        diagnostic.Location.GetLineSpan().Path.Should().Be("Source.cs");
        diagnostic.Location.SourceSpan.Length.Should().BeGreaterThan(0);
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact(DisplayName = "Should report a declaration error once when two serializers reach it")]
    public void Should_ReportOnce_When_TwoSerializersReachType()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            public interface IFirst { }
            public interface ISecond { }
            [AkkaSerializable(Manifest = "message")]
            public sealed record Message([property: AkkaField(0)] int A, [property: AkkaField(0)] int B) : IFirst, ISecond;
            [AkkaSerializer<IFirst>("first", 120900)]
            public sealed partial class FirstSerializer : AkkaSerializer { }
            [AkkaSerializer<ISecond>("second", 120901)]
            public sealed partial class SecondSerializer : AkkaSerializer { }
            """);
        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG005");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact(DisplayName = "Should report declaration errors even when serializer declaration is invalid")]
    public void Should_ReportDeclarationError_When_SerializerIsInvalid()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            public interface IProtocol { }
            [AkkaSerializable(Manifest = "message")]
            public sealed record Message(int Value) : IProtocol;
            [AkkaSerializer<IProtocol>("", 120900)]
            public sealed partial class BrokenSerializer : AkkaSerializer { }
            """);
        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG004");
        result.GeneratedSources.Should().BeEmpty();
    }
    [Fact(DisplayName = "Should report generic declaration errors once across registered constructions")]
    public void Should_ReportOnce_When_GenericConstructionsAreRegistered()
    {
        var result = GeneratorTestHarness.Run("""
            using Akka.Serialization.V2;
            public interface IProtocol { }
            [AkkaSerializable]
            public sealed record Message<T>([property: AkkaField(0)] T A, [property: AkkaField(0)] T B) : IProtocol;
            [AkkaSerializer<IProtocol>("sample", 120902)]
            [AkkaSerializable<Message<int>>(Manifest = "int")]
            [AkkaSerializable<Message<string>>(Manifest = "string")]
            public sealed partial class SampleSerializer : AkkaSerializer { }
            """);
        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG005");
        result.GeneratedSources.Should().BeEmpty();
    }

}
