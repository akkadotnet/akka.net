//-----------------------------------------------------------------------
// <copyright file="UnionCompletenessDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using Akka.Serialization.V2.Generators;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// A union's member set must cover every type that can reach the field, or the first write of a
/// missing type fails at runtime. A field typed as an interface, abstract class, or closed
/// hierarchy discovers its members from <c>[AkkaSerializable]</c> types; AKKASG048 catches a
/// derived type that discovery cannot see because it lacks <c>[AkkaSerializable]</c>.
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
    private static string ClosedCoverage(string trialAttribute) => $$"""
        #nullable enable
        using System;
        using Akka.Serialization.V2;

        namespace Completeness;

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

            public interface IOrderEvent { }

            public sealed record OrderForgotten(string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "notice")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG048");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019", "AKKASG048 already names the fix");
    }

    [Fact(DisplayName = "Should_InferUnion_When_ConcreteBaseCarriesIsClosedTypeAttribute")]
    public void Should_InferUnion_When_ConcreteBaseCarriesIsClosedTypeAttribute()
    {
        // A concrete base with a public constructor and top-level (not nested) cases is closed only
        // because it carries IsClosedTypeAttribute -- the shape a C# 15 'closed record' lowers to,
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
                public record Shape;

                [AkkaSerializable(Manifest = "circle")]
                public sealed record Circle([property: AkkaField(1)] double Radius) : Shape;

                [AkkaSerializable(Manifest = "square")]
                public sealed record Square([property: AkkaField(1)] double Side) : Shape;

                [AkkaSerializable(Manifest = "drawn")]
                public sealed record Drawn([property: AkkaField(1)] Shape Shape) : ISubscriptionEvent;
            }
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        // Only the generator's view is asserted: the compiler itself rejects a source-level
        // [IsClosedType] (CS8335, reserved for compiler use), but a closed type read from a
        // referenced assembly's metadata carries exactly this attribute.
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("Union_Shape").And.Contain("\"circle\"").And.Contain("\"square\"");
    }

    // Real C# 15 syntax (LanguageVersion.Preview through the harness's newer Roslyn). The compiler
    // adds IsClosedTypeAttribute only at emit time and Roslyn does not return it from a source symbol,
    // so these prove the generator recognizes the `closed` modifier itself -- including on a base
    // with no private constructor, where nothing else marks the hierarchy as closed.
    private const string ClosedRecordSource = """
        #nullable enable
        using Akka.Serialization.V2;

        namespace System.Runtime.CompilerServices
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            internal sealed class IsClosedTypeAttribute : System.Attribute { }
        }

        namespace Completeness.CSharp15
        {
            public closed record Shape
            {
                [AkkaSerializable(Manifest = "circle")]
                public sealed record Circle([property: AkkaField(1)] double Radius) : Shape;

                [AkkaSerializable(Manifest = "square")]
                public sealed record Square([property: AkkaField(1)] double Side) : Shape;

                {{TRIANGLE}}
                public sealed record Triangle([property: AkkaField(1)] double Base) : Shape;
            }

            {{FIELD}}
        }
        """ + SerializerSource;

    private static Harness.GeneratorRunResult RunClosedRecord(string triangle, string field)
    {
        var source = ClosedRecordSource.Replace("{{TRIANGLE}}", triangle).Replace("{{FIELD}}", field);
        return GeneratorTestHarness.Run(new[] { new SourceFile("Source.cs", source) }, GeneratorTestHarness.PreviewParseOptions);
    }

    [Fact(DisplayName = "Should_RoundTripEveryCase_When_FieldIsCSharp15ClosedRecordUnion")]
    public void Should_RoundTripEveryCase_When_FieldIsCSharp15ClosedRecordUnion()
    {
        var result = RunClosedRecord("""[AkkaSerializable(Manifest = "triangle")]""", """
            [AkkaSerializable(Manifest = "drawn")]
            public sealed record Drawn([property: AkkaField(1)] Shape Shape) : ISubscriptionEvent;

            public static class Samples
            {
                public static object[] All() => [new Drawn(new Shape.Circle(1)), new Drawn(new Shape.Square(2)), new Drawn(new Shape.Triangle(3))];
            }
            """);

        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        using var system = Akka.Actor.ActorSystem.Create("closed-record-union");
        var serializer = (Akka.Serialization.SerializerWithStringManifest)Activator.CreateInstance(
            assembly.GetType("SubscriptionSerializer", throwOnError: true)!, (Akka.Actor.ExtendedActorSystem)system)!;
        var samples = (object[])assembly.GetType("Completeness.CSharp15.Samples", throwOnError: true)!.GetMethod("All")!.Invoke(null, null)!;

        foreach (var original in samples)
            serializer.FromBinary(serializer.ToBinary(original), serializer.Manifest(original)).Should().Be(original);
    }

    [Fact(DisplayName = "Should_ReportAKKASG048_When_CSharp15ClosedRecordCaseLacksAkkaSerializable")]
    public void Should_ReportAKKASG048_When_CSharp15ClosedRecordCaseLacksAkkaSerializable()
    {
        var result = RunClosedRecord(string.Empty, """
            [AkkaSerializable(Manifest = "drawn")]
            public sealed record Drawn([property: AkkaField(1)] Shape Shape) : ISubscriptionEvent;
            """);

        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG048")
            .Which.GetMessage(null).Should().Contain("Completeness.CSharp15.Shape.Triangle");
    }

    [Fact(DisplayName = "Should_ReportAKKASG003_NotCrash_When_FieldIsArrayOfInterface")]
    public void Should_ReportAKKASG003_NotCrash_When_FieldIsArrayOfInterface()
    {
        // An interface is a union only as a property's own type: an array of it is not a named
        // type, so it is never a union key, and collecting union keys must not throw over it
        // (CS8785) -- it stays an ordinary unsupported field.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Completeness.ArrayField;

            public interface IShape { }

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : IShape;

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] IShape[] Shapes) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(source);

        result.AllDiagnostics.Should().NotContain(d => d.Id == "CS8785");
        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG003");
        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG019" || d.Id == "AKKASG048");
    }

    [Fact(DisplayName = "Should_NotReportAKKASG048_When_ImplementorIsRefStruct")]
    public void Should_NotReportAKKASG048_When_ImplementorIsRefStruct()
    {
        // A ref struct may implement an interface (C# 13) but can never be converted to it, so it
        // can never reach a union field.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Completeness.RefStruct;

            public interface IShape { }

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : IShape;

            public ref struct StackShape : IShape { }

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] IShape Shape) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.Run(new[] { new SourceFile("Source.cs", source) }, GeneratorTestHarness.PreviewParseOptions);

        result.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG048");
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "Should_ReportAKKASG048_When_EditMakesUnmarkedTypeImplementUnionBase")]
    public void Should_ReportAKKASG048_When_EditMakesUnmarkedTypeImplementUnionBase()
    {
        // An interface field's set is every [AkkaSerializable] implementor, so a type in ANOTHER file
        // that starts implementing it without the attribute must invalidate the cached compilation
        // facts and surface AKKASG048 on the next incremental run.
        const string holder = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Completeness.Caching;

            public interface IShape { }

            [AkkaSerializable(Manifest = "circle")]
            public sealed record Circle([property: AkkaField(1)] double Radius) : IShape;

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] IShape Shape) : ISubscriptionEvent;
            """ + SerializerSource;

        var result = GeneratorTestHarness.RunIncremental(
            new[] { new SourceFile("Holder.cs", holder), new SourceFile("Other.cs", "namespace Completeness.Caching; public sealed class Unrelated { }") },
            new[] { new SourceFile("Holder.cs", holder), new SourceFile("Other.cs", "namespace Completeness.Caching; public sealed class Unrelated : IShape { }") });

        result.Before.GeneratorDiagnostics.Should().NotContain(d => d.Id == "AKKASG048");
        result.After.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG048")
            .Which.GetMessage(null).Should().Contain("Completeness.Caching.Unrelated");
    }

}
