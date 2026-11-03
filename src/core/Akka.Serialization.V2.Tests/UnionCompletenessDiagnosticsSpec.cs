//-----------------------------------------------------------------------
// <copyright file="UnionCompletenessDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// A union's member set must cover every type that can reach the field, or the first write of a
/// missing type fails at runtime. AKKASG048 catches a derived type that a parameterless
/// <c>[AkkaUnion]</c> cannot discover because it lacks <c>[AkkaSerializable]</c>; AKKASG049 catches
/// an explicit member list that leaves out a case of a closed hierarchy.
/// </summary>
public sealed class UnionCompletenessDiagnosticsSpec
{
    private const string SerializerSource = """

        public interface ISubscriptionEvent { }

        [AkkaSerializer<ISubscriptionEvent>("subscriptions", 150001)]
        public sealed partial class SubscriptionSerializer : AkkaSerializer
        { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
        """;

    // A closed hierarchy: the private constructor means only the nested cases can derive from Coverage.
    private static string ClosedCoverage(string trialAttribute, string coverageAttribute = "[AkkaUnion]") => $$"""
        #nullable enable
        using System;
        using Akka.Serialization.V2;

        namespace Completeness;

        {{coverageAttribute}}
        public abstract record Coverage
        {
            private Coverage() { }

            [AkkaSerializable(Manifest = "paid-invoice")]
            public sealed record PaidInvoice([property: AkkaField(1)] long PublisherId, [property: AkkaField(2)] long InvoiceNumber) : Coverage;

            [AkkaSerializable(Manifest = "paid-upstream")]
            public sealed record PaidUpstream([property: AkkaField(1)] string? Reference, [property: AkkaField(2)] decimal? Amount) : Coverage;

            {{trialAttribute}}
            public sealed record Trial : Coverage;

            [AkkaSerializable(Manifest = "complimentary", AllowEmpty = true)]
            public sealed record Complimentary : Coverage;

            [AkkaSerializable(Manifest = "migrated", AllowEmpty = true)]
            public sealed record Migrated : Coverage;
        }
        """;

    private const string PeriodCoveredDiscovered = """

        [AkkaSerializable(Manifest = "period-covered")]
        public sealed record PeriodCovered([property: AkkaField(1)] long SubscriptionId, [property: AkkaField(2)] DateTime Start,
            [property: AkkaField(3)] DateTime End, [property: AkkaField(4)] Coverage Coverage) : ISubscriptionEvent;
        """;

    [Fact(DisplayName = "Should_ReportAKKASG048_When_DiscoveredClosedHierarchyCaseLacksAkkaSerializable")]
    public void Should_ReportAKKASG048_When_DiscoveredClosedHierarchyCaseLacksAkkaSerializable()
    {
        var source = ClosedCoverage(trialAttribute: string.Empty) + PeriodCoveredDiscovered + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        var error = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG048").Which;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage(null).Should().Contain("Completeness.Coverage.Trial").And.Contain("Completeness.Coverage").And.Contain("PeriodCovered");
        error.Location.Kind.Should().NotBe(LocationKind.None, "AKKASG048 reports at the union field");

        // An error stops emission, so the broken dispatch never reaches the consumer's build.
        result.RunResult.GeneratedTrees.Should().NotContain(tree => tree.FilePath.EndsWith("SubscriptionSerializer.AkkaSerialization.g.cs", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Should_GenerateCompilingSerializer_When_EveryClosedHierarchyCaseIsAkkaSerializable")]
    public void Should_GenerateCompilingSerializer_When_EveryClosedHierarchyCaseIsAkkaSerializable()
    {
        var source = ClosedCoverage(trialAttribute: """[AkkaSerializable(Manifest = "trial", AllowEmpty = true)]""") + PeriodCoveredDiscovered + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        // AllDiagnostics includes the output compilation, so this also proves the generated code compiles.
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        foreach (var manifest in new[] { "paid-invoice", "paid-upstream", "trial", "complimentary", "migrated" })
            generated.Should().Contain("\"" + manifest + "\"");
    }

    [Fact(DisplayName = "Should_ReportAKKASG048_When_DiscoveredInterfaceUnionHasUnmarkedImplementor")]
    public void Should_ReportAKKASG048_When_DiscoveredInterfaceUnionHasUnmarkedImplementor()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Completeness.Open;

            [AkkaUnion]
            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrderEvent;

            public sealed record OrderForgotten(string OrderId) : IOrderEvent;

            public abstract record OrderBase : IOrderEvent;

            [AkkaSerializable(Manifest = "notice")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        // Only the concrete, unmarked implementor: an abstract type is never a runtime type.
        result.GeneratorDiagnostics.Where(d => d.Id == "AKKASG048").Should().ContainSingle()
            .Which.GetMessage(null).Should().Contain("OrderForgotten");
    }

    [Fact(DisplayName = "Should_ReportAKKASG048_InsteadOfAKKASG019_When_EveryImplementorIsUnmarked")]
    public void Should_ReportAKKASG048_InsteadOfAKKASG019_When_EveryImplementorIsUnmarked()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Completeness.AllUnmarked;

            [AkkaUnion]
            public interface IOrderEvent { }

            public sealed record OrderForgotten(string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "notice")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG048");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019", "AKKASG048 already names the fix");
    }

    [Fact(DisplayName = "Should_WarnAKKASG049_When_ExplicitUnionListOmitsCaseOfClosedHierarchy")]
    public void Should_WarnAKKASG049_When_ExplicitUnionListOmitsCaseOfClosedHierarchy()
    {
        var source = ClosedCoverage(trialAttribute: """[AkkaSerializable(Manifest = "trial", AllowEmpty = true)]""", coverageAttribute: string.Empty) + """

            [AkkaSerializable(Manifest = "period-covered")]
            public sealed record PeriodCovered(
                [property: AkkaField(1)] long SubscriptionId,
                [property: AkkaField(2), AkkaUnion(typeof(Coverage.PaidInvoice), typeof(Coverage.PaidUpstream), typeof(Coverage.Trial))] Coverage Coverage) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        var warning = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG049").Which;
        warning.Severity.Should().Be(DiagnosticSeverity.Warning);
        warning.GetMessage(null).Should().Contain("Coverage.Complimentary").And.Contain("Coverage.Migrated").And.NotContain("Coverage.Trial'");

        // A warning: the listed cases still generate.
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_TreatIsClosedTypeBaseAsClosed_When_ExplicitUnionListOmitsCase")]
    public void Should_TreatIsClosedTypeBaseAsClosed_When_ExplicitUnionListOmitsCase()
    {
        // The shape a C# 15 'closed record' lowers to: an abstract class carrying IsClosedTypeAttribute,
        // here declared through the same consumer-side polyfill a net10.0 project needs.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                internal sealed class IsClosedTypeAttribute : System.Attribute { }
            }

            namespace Completeness.ClosedKeyword
            {
                [System.Runtime.CompilerServices.IsClosedType]
                public abstract record Shape;

                [AkkaSerializable(Manifest = "circle")]
                public sealed record Circle([property: AkkaField(1)] double Radius) : Shape;

                [AkkaSerializable(Manifest = "square")]
                public sealed record Square([property: AkkaField(1)] double Side) : Shape;

                [AkkaSerializable(Manifest = "drawn")]
                public sealed record Drawn([property: AkkaField(1), AkkaUnion(typeof(Circle))] Shape Shape) : ISubscriptionEvent;
            }
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG049")
            .Which.GetMessage(null).Should().Contain("Square");
    }

    [Fact(DisplayName = "Should_NotWarnAKKASG049_When_ExplicitUnionListNarrowsAnOpenBase")]
    public void Should_NotWarnAKKASG049_When_ExplicitUnionListNarrowsAnOpenBase()
    {
        // An interface (or a class with a non-private constructor) can gain implementors anywhere, so
        // a narrower list is a choice, not an omission.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Completeness.OpenList;

            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "cancelled")]
            public sealed record OrderCancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;

            public abstract record Shape;

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : Shape;

            [AkkaSerializable(Manifest = "square")]
            public sealed record Square([property: AkkaField(1)] double Side) : Shape;

            [AkkaSerializable(Manifest = "notice")]
            public sealed record OrderNotice(
                [property: AkkaField(1), AkkaUnion(typeof(OrderPlaced))] IOrderEvent Event,
                [property: AkkaField(2), AkkaUnion(typeof(Circle))] Shape Shape) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG049" || d.Id == "AKKASG048");
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }
}
