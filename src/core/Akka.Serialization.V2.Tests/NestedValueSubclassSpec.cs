//-----------------------------------------------------------------------
// <copyright file="NestedValueSubclassSpec.cs" company="Akka.NET Project">
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

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// A property typed as a concrete, non-sealed <c>[AkkaSerializable]</c> class is a nested value: it
/// is written as that class's own fields, never as a union. A subclass value written there would
/// lose its own fields, so a subclass anywhere in the compilation fails the build (AKKASG052).
/// </summary>
public sealed class NestedValueSubclassSpec
{
    private const string Prelude = """
        #nullable enable
        using Akka.Serialization.V2;

        namespace NestedValues;

        public interface IShippingEvent { }

        [AkkaSerializer<IShippingEvent>("shipping", 152101)]
        public sealed partial class ShippingSerializer : AkkaSerializer
        { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }

        """;

    private static string Source(string addressDeclaration, string extraTypes = "", string fieldType = "Address") => Prelude + $$"""
        {{addressDeclaration}}

        {{extraTypes}}

        [AkkaSerializable(Manifest = "shipped")]
        public sealed record Shipped([property: AkkaField(1)] string OrderId, [property: AkkaField(2)] {{fieldType}} Destination) : IShippingEvent;
        """;

    private const string UnsealedAddress = """
        [AkkaSerializable]
        public record Address([property: AkkaField(1)] string Street);
        """;

    [Fact(DisplayName = "Should_ReportAKKASG052_When_NestedValueClassHasMarkedSubclass")]
    public void Should_ReportAKKASG052_When_NestedValueClassHasMarkedSubclass()
    {
        var source = Source(UnsealedAddress, """
            [AkkaSerializable]
            public sealed record PoBoxAddress([property: AkkaField(1)] string Street, [property: AkkaField(2)] int Box) : Address(Street);
            """);

        var result = GeneratorTestHarness.Run(source);

        var error = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG052").Which;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.Location.Kind.Should().NotBe(LocationKind.None, "AKKASG052 reports at the field");
        var message = error.GetMessage();
        message.Should().Contain("'Destination'").And.Contain("'NestedValues.Address'").And.Contain("'NestedValues.PoBoxAddress'");
        message.Should().Contain("Seal 'NestedValues.Address'").And.Contain("make it abstract and remove its [AkkaSerializable]");
    }

    [Fact(DisplayName = "Should_ReportAKKASG052_When_NestedValueClassHasUnmarkedSubclass")]
    public void Should_ReportAKKASG052_When_NestedValueClassHasUnmarkedSubclass()
    {
        // An unmarked subclass is just as dangerous: it converts to Address implicitly, and the
        // generated write would drop its extra state just the same.
        var source = Source(UnsealedAddress, """
            public sealed record GeocodedAddress(string Street, double Latitude) : Address(Street);
            """);

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG052")
            .Which.GetMessage().Should().Contain("'NestedValues.GeocodedAddress'");
    }

    [Fact(DisplayName = "Should_NameEverySubclass_When_NestedValueClassHasSeveral")]
    public void Should_NameEverySubclass_When_NestedValueClassHasSeveral()
    {
        // An abstract intermediate class and the concrete type below it both count.
        var source = Source(UnsealedAddress, """
            public abstract record RuralAddress(string Street) : Address(Street);

            public sealed record FarmAddress(string Street, string Parcel) : RuralAddress(Street);
            """);

        var result = GeneratorTestHarness.Run(source);

        var message = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG052").Which.GetMessage();
        message.Should().Contain("'NestedValues.FarmAddress'").And.Contain("'NestedValues.RuralAddress'");
    }

    [Fact(DisplayName = "Should_ReportAKKASG052_When_NullableFieldIsTypedAsSubclassedNestedValue")]
    public void Should_ReportAKKASG052_When_NullableFieldIsTypedAsSubclassedNestedValue()
    {
        var source = Source(UnsealedAddress, """
            public sealed record GeocodedAddress(string Street, double Latitude) : Address(Street);
            """, fieldType: "Address?");

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG052");
    }

    [Theory(DisplayName = "Should_ReportAKKASG052_When_CollectionElementIsSubclassedNestedValue")]
    [InlineData("System.Collections.Generic.IReadOnlyList<Address>")]
    [InlineData("System.Collections.Generic.IReadOnlyDictionary<string, Address>")]
    public void Should_ReportAKKASG052_When_CollectionElementIsSubclassedNestedValue(string fieldType)
    {
        // Each element (or dictionary value) is written as Address's own fields too, so a subclass
        // element loses its fields exactly as a subclass property value would. A union is not
        // supported as an element, so the only fix offered is sealing.
        var source = Source(UnsealedAddress, """
            [AkkaSerializable]
            public sealed record PoBoxAddress([property: AkkaField(1)] string Street, [property: AkkaField(2)] int Box) : Address(Street);
            """, fieldType: fieldType);

        var result = GeneratorTestHarness.Run(source);

        var message = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG052").Which.GetMessage();
        message.Should().Contain("'NestedValues.PoBoxAddress'").And.Contain("a union is not supported as a collection element");
        message.Should().NotContain("make it abstract");
    }

    [Fact(DisplayName = "Should_NotReportAKKASG052_When_NestedValueClassHasNoSubclass")]
    public void Should_NotReportAKKASG052_When_NestedValueClassHasNoSubclass()
    {
        // Leaving a record unsealed is common and harmless while nothing derives from it.
        var result = GeneratorTestHarness.Run(Source(UnsealedAddress));

        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_NotReportAKKASG052_When_NestedValueClassIsSealed")]
    public void Should_NotReportAKKASG052_When_NestedValueClassIsSealed()
    {
        var result = GeneratorTestHarness.Run(Source("""
            [AkkaSerializable]
            public sealed record Address([property: AkkaField(1)] string Street);
            """));

        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG052");
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_TreatFieldAsUnion_When_BaseIsAbstractInstead")]
    public void Should_TreatFieldAsUnion_When_BaseIsAbstractInstead()
    {
        // The fix the diagnostic offers: an abstract base makes the field a union of its
        // [AkkaSerializable] subclasses, so every subclass keeps its own fields on the wire.
        var source = Source("""
            public abstract record Address;
            """, """
            [AkkaSerializable(Manifest = "street")]
            public sealed record StreetAddress([property: AkkaField(1)] string Street) : Address;

            [AkkaSerializable(Manifest = "po-box")]
            public sealed record PoBoxAddress([property: AkkaField(1)] string Street, [property: AkkaField(2)] int Box) : Address;
            """);

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_NotReportAKKASG052_When_SubclassedClassIsOnlyATopLevelMessage")]
    public void Should_NotReportAKKASG052_When_SubclassedClassIsOnlyATopLevelMessage()
    {
        // Top-level dispatch matches each message's exact type, so a subclassed message type that
        // no property uses is not a nested value and loses nothing.
        var source = Prelude + """
            [AkkaSerializable(Manifest = "base-event")]
            public record BaseEvent([property: AkkaField(1)] string Id) : IShippingEvent;

            [AkkaSerializable(Manifest = "derived-event")]
            public sealed record DerivedEvent([property: AkkaField(1)] string Id, [property: AkkaField(2)] int Extra) : BaseEvent(Id);
            """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG052");
    }
}
