//-----------------------------------------------------------------------
// <copyright file="CSharpUnionSpec.cs" company="Akka.NET Project">
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
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Persistence.Journal;
using Akka.Serialization;
using Akka.Serialization.V2.Tests.Harness;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// C# 15 <c>union</c> types as <c>[AkkaField]</c> property types. The sources below are real C# 15
/// (compiled by the harness with <c>LanguageVersion.Preview</c>) targeting the .NET 10 runtime, with
/// the consumer-side polyfills a net10.0 project declares. The generated serializer is emitted,
/// loaded, and run against the test's own actor system.
/// </summary>
public sealed class CSharpUnionSpec : AkkaSpec
{
    public CSharpUnionSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
    {
    }

    private const string Polyfills = """
        namespace System.Runtime.CompilerServices
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
            internal sealed class UnionAttribute : Attribute { }

            internal interface IUnion { object? Value { get; } }

            [AttributeUsage(AttributeTargets.Class)]
            internal sealed class IsClosedTypeAttribute : Attribute { }
        }
        """;

    private const string CoverageCases = """
        #nullable enable
        using System;
        using Akka.Serialization.V2;

        namespace Unions;

        [AkkaSerializable(Manifest = "paid-invoice")]
        public sealed record PaidInvoice([property: AkkaField(1)] long PublisherId, [property: AkkaField(2)] long InvoiceNumber);

        [AkkaSerializable(Manifest = "paid-upstream")]
        public sealed record PaidUpstream([property: AkkaField(1)] string? Reference, [property: AkkaField(2)] decimal? Amount);

        [AkkaSerializable(Manifest = "trial", AllowEmpty = true)]
        public sealed record Trial;

        [AkkaSerializable(Manifest = "complimentary", AllowEmpty = true)]
        public sealed record Complimentary;

        [AkkaSerializable(Manifest = "migrated", AllowEmpty = true)]
        public sealed record Migrated;

        public union Coverage(PaidInvoice, PaidUpstream, Trial, Complimentary, Migrated);

        public interface ISubscriptionEvent;

        [AkkaSerializer<ISubscriptionEvent>("subscriptions", 152001)]
        public sealed partial class SubscriptionSerializer : AkkaSerializer
        {
            public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
        }
        """;

    private const string RoundTripMessages = """
        #nullable enable
        using System;
        using Akka.Serialization.V2;

        namespace Unions;

        [AkkaSerializable(Manifest = "period-covered")]
        public sealed record PeriodCovered(
            [property: AkkaField(1)] long SubscriptionId,
            [property: AkkaField(2)] Coverage Coverage,
            [property: AkkaField(3)] Coverage? PreviousCoverage) : ISubscriptionEvent;

        public static class Samples
        {
            public static object[] EveryCase() =>
            [
                new PeriodCovered(1, new PaidInvoice(42, 1001), null),
                new PeriodCovered(2, new PaidUpstream("ref-7", 19.99m), new Trial()),
                new PeriodCovered(3, new PaidUpstream(null, null), null),
                new PeriodCovered(4, new Trial(), new PaidInvoice(1, 2)),
                new PeriodCovered(5, new Complimentary(), null),
                new PeriodCovered(6, new Migrated(), null),
                new PeriodCovered(7, default, null),
            ];

            public static string? CaseTypeName(object message) => ((PeriodCovered)message).Coverage.Value?.GetType().Name;
        }
        """;

    private static Harness.GeneratorRunResult RunPreview(params string[] sources)
    {
        var files = sources.Select((text, i) => new SourceFile($"Source{i}.cs", text)).Append(new SourceFile("Polyfills.cs", Polyfills)).ToList();
        return GeneratorTestHarness.Run(files, GeneratorTestHarness.PreviewParseOptions);
    }

    [Fact(DisplayName = "Should_RoundTripEveryCaseAndDefault_When_FieldIsCSharpUnion")]
    public void Should_RoundTripEveryCaseAndDefault_When_FieldIsCSharpUnion()
    {
        var result = RunPreview(CoverageCases, RoundTripMessages);

        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Unions.SubscriptionSerializer");
        var samples = (object[])Invoke(assembly, "Unions.Samples", "EveryCase")!;

        foreach (var original in samples)
        {
            var bytes = serializer.ToBinary(original);
            var restored = serializer.FromBinary(bytes, serializer.Manifest(original));

            restored.Should().Be(original);
            Invoke(assembly, "Unions.Samples", "CaseTypeName", restored).Should().Be(Invoke(assembly, "Unions.Samples", "CaseTypeName", original));
            ((SerializerV2)serializer).SizeHint(original).Should().BeGreaterOrEqualTo(0);
        }
    }

    [Fact(DisplayName = "Should_WriteSameBytesAsClosedHierarchyUnion_When_CaseIsHeldInCSharpUnion")]
    public void Should_WriteSameBytesAsClosedHierarchyUnion_When_CaseIsHeldInCSharpUnion()
    {
        // The same cases, once as a closed record hierarchy and once as a C# union:
        // the field's bytes must match, so data written by one shape reads back with the other.
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Unions.Wire;

            public abstract record Shape
            {
                private Shape() { }

                [AkkaSerializable(Manifest = "circle")]
                public sealed record Circle([property: AkkaField(1)] double Radius) : Shape;

                [AkkaSerializable(Manifest = "square")]
                public sealed record Square([property: AkkaField(1)] double Side) : Shape;
            }

            public union ShapeUnion(Shape.Circle, Shape.Square);

            public interface IDrawing;

            [AkkaSerializable(Manifest = "drawn-hierarchy")]
            public sealed record DrawnHierarchy([property: AkkaField(1)] Shape Shape) : IDrawing;

            [AkkaSerializable(Manifest = "drawn-union")]
            public sealed record DrawnUnion([property: AkkaField(1)] ShapeUnion Shape) : IDrawing;

            [AkkaSerializer<IDrawing>("drawing", 152002)]
            public sealed partial class DrawingSerializer : AkkaSerializer
            {
                public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
            }

            public static class Samples
            {
                public static object[] Pair() => [new DrawnHierarchy(new Shape.Square(2.5)), new DrawnUnion(new Shape.Square(2.5))];
            }
            """;

        var result = RunPreview(source);
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Unions.Wire.DrawingSerializer");
        var pair = (object[])Invoke(assembly, "Unions.Wire.Samples", "Pair")!;

        var hierarchyBytes = serializer.ToBinary(pair[0]);
        var unionBytes = serializer.ToBinary(pair[1]);

        unionBytes.Should().Equal(hierarchyBytes);
        serializer.FromBinary(hierarchyBytes, "drawn-union").Should().Be(pair[1]);
    }

    [Fact(DisplayName = "Should_ReportAKKASG016_When_CSharpUnionCaseHasNoManifest")]
    public void Should_ReportAKKASG016_When_CSharpUnionCaseHasNoManifest()
    {
        var source = CoverageCases.Replace("""[AkkaSerializable(Manifest = "trial", AllowEmpty = true)]""", "[AkkaSerializable(AllowEmpty = true)]") + """

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] Coverage Coverage) : ISubscriptionEvent;
            """;

        var result = RunPreview(source);

        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG016")
            .Which.GetMessage(null).Should().Contain("Unions.Trial");
    }

    [Fact(DisplayName = "Should_ReportAKKASG017_When_CSharpUnionCasesShareManifest")]
    public void Should_ReportAKKASG017_When_CSharpUnionCasesShareManifest()
    {
        var source = CoverageCases.Replace("""Manifest = "migrated", """, """Manifest = "trial", """) + """

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] Coverage Coverage) : ISubscriptionEvent;
            """;

        var result = RunPreview(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG017");
    }

    [Fact(DisplayName = "Should_ReportAKKASG015_When_CSharpUnionCaseIsNotAkkaSerializable")]
    public void Should_ReportAKKASG015_When_CSharpUnionCaseIsNotAkkaSerializable()
    {
        var source = CoverageCases.Replace("public union Coverage(PaidInvoice,", "public union Coverage(string, PaidInvoice,") + """

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] Coverage Coverage) : ISubscriptionEvent;
            """;

        var result = RunPreview(source);

        result.GeneratorDiagnostics.Should().Contain(d => d.Id == "AKKASG015" && d.GetMessage(null).Contains("System.String"));
        result.RunResult.GeneratedTrees.Should().NotContain(tree => tree.FilePath.EndsWith("SubscriptionSerializer.AkkaSerialization.g.cs", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Should_ReportAKKASG051_When_UnionHasNoManifest")]
    public void Should_ReportAKKASG051_When_UnionHasNoManifest()
    {
        var source = CoverageCases + """

            [AkkaSerializable(AllowEmpty = true)]
            public union CoverageMessage(PaidInvoice, Trial) : ISubscriptionEvent;
            """;

        var result = RunPreview(source);

        var error = result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG051").Which;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage(null).Should().Contain("Unions.CoverageMessage");
    }

    [Fact(DisplayName = "Should_ReportOnlyAKKASG051_When_AkkaSerializableUnionIsUsedAsField")]
    public void Should_ReportOnlyAKKASG051_When_AkkaSerializableUnionIsUsedAsField()
    {
        var source = CoverageCases + """

            [AkkaSerializable(AllowEmpty = true)]
            public union CoverageMessage(PaidInvoice, Trial);

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] CoverageMessage Coverage) : ISubscriptionEvent;
            """;

        var result = RunPreview(source);

        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).Should().Equal("AKKASG051");
    }

    [Fact(DisplayName = "Should_RoundTripTopLevelUnionCasesAndRejectDefault")]
    public void Should_RoundTripTopLevelUnionCasesAndRejectDefault()
    {
        var source = CoverageCases.Replace("long InvoiceNumber);", "long InvoiceNumber) : ISubscriptionEvent;") + """

            [AkkaSerializable(Manifest = "coverage-message")]
            public union CoverageMessage(PaidInvoice, Trial) : ISubscriptionEvent;

            [AkkaSerializable(Manifest = "holder")]
            public sealed record Holder([property: AkkaField(1)] CoverageMessage Coverage) : ISubscriptionEvent;

            public static class TopLevelSamples
            {
                public static object[] All() => [(CoverageMessage)new PaidInvoice(1, 2), (CoverageMessage)new Trial()];
                public static object Empty() => default(CoverageMessage);
                public static object Standalone() => new PaidInvoice(1, 2);
                public static object Wrap(object value) => new Holder((CoverageMessage)value);
                public static bool IsUnion(object value) => value is CoverageMessage;
            }
            """;
        var result = RunPreview(source);
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Unions.SubscriptionSerializer");
        foreach (var original in (object[])Invoke(assembly, "Unions.TopLevelSamples", "All")!)
        {
            serializer.Manifest(original).Should().Be("coverage-message");
            var bytes = serializer.ToBinary(original);
            var restored = serializer.FromBinary(bytes, "coverage-message");
            restored.Should().Be(original);
            Invoke(assembly, "Unions.TopLevelSamples", "IsUnion", restored).Should().Be(true);
            ((SerializerV2)serializer).SizeHint(original).Should().Be(bytes.Length);
            var holder = Invoke(assembly, "Unions.TopLevelSamples", "Wrap", original)!;
            var holderBytes = serializer.ToBinary(holder);
            holderBytes.Skip(2).Should().Equal(bytes);
        }
        var standalone = Invoke(assembly, "Unions.TopLevelSamples", "Standalone")!;
        serializer.Manifest(standalone).Should().Be("paid-invoice");
        serializer.FromBinary(serializer.ToBinary(standalone), "paid-invoice").Should().Be(standalone);
        var empty = Invoke(assembly, "Unions.TopLevelSamples", "Empty")!;
        Action write = () => serializer.ToBinary(empty);
        write.Should().Throw<System.Runtime.Serialization.SerializationException>();
    }

    [Fact(DisplayName = "Should_RoundTripRegisteredClosedGenericUnion")]
    public void Should_RoundTripRegisteredClosedGenericUnion()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;
            namespace GenericUnion;
            public interface IProtocol;
            [AkkaSerializable(Manifest = "number")]
            public sealed record Number([property: AkkaField(1)] int Value);
            [AkkaSerializable(Manifest = "text")]
            public sealed record Text([property: AkkaField(1)] string Value);
            [AkkaSerializable(Manifest = "generic-union")]
            public union Choice<T>(Number, T) : IProtocol;
            [AkkaSerializer<IProtocol>("generic-union", 152005)]
            [AkkaSerializerIncludes<Choice<Text>>(Manifest = "choice-text")]
            public sealed partial class Serializer : AkkaSerializer
            {
                public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
            }
            public static class Samples
            {
                public static object[] All() => [(Choice<Text>)new Number(42), (Choice<Text>)new Text("hello")];
            }
            """;
        var result = RunPreview(source);
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "GenericUnion.Serializer");
        foreach (var original in (object[])Invoke(assembly, "GenericUnion.Samples", "All")!)
        {
            serializer.Manifest(original).Should().Be("choice-text");
            serializer.FromBinary(serializer.ToBinary(original), "choice-text").Should().Be(original);
        }
    }

    [Fact(DisplayName = "Should_GenerateForUnionDeclaredInReferencedAssembly")]
    public void Should_GenerateForUnionDeclaredInReferencedAssembly()
    {
        const string types = """
            using Akka.Serialization.V2;
            namespace ExternalUnion;
            public interface IProtocol;
            [AkkaSerializable(Manifest = "case")]
            public sealed record Case([property: AkkaField(1)] int Value);
            [AkkaSerializable(Manifest = "choice")]
            public union Choice(Case) : IProtocol;
            """;
        var sourceAssembly = RunPreview(types);
        using var stream = new MemoryStream();
        var emit = sourceAssembly.OutputCompilation.WithAssemblyName("ExternalUnionTypes").Emit(stream);
        emit.Success.Should().BeTrue(string.Join(", ", emit.Diagnostics));
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        const string consumer = """
            using Akka.Serialization.V2;
            using ExternalUnion;
            [AkkaSerializer<IProtocol>("external", 152006)]
            [AkkaSerializerIncludes<Choice>]
            public sealed partial class Serializer : AkkaSerializer
            {
                public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
            }
            """;
        var result = GeneratorTestHarness.Run(new[] { new SourceFile("Consumer.cs", consumer) }, GeneratorTestHarness.PreviewParseOptions, reference);
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.RunResult.GeneratedTrees.Single().ToString().Should().Contain("message.Value is null");
    }

    [Theory(DisplayName = "Should_ValidateTopLevelUnionCases")]
    [InlineData("[AkkaSerializable(Manifest = \"trial\", AllowEmpty = true)]", "[AkkaSerializable(AllowEmpty = true)]", "AKKASG016")]
    [InlineData("[AkkaSerializable(Manifest = \"trial\", AllowEmpty = true)]", "[AkkaSerializable(Manifest = \"paid-invoice\", AllowEmpty = true)]", "AKKASG017")]
    [InlineData("[AkkaSerializable(Manifest = \"trial\", AllowEmpty = true)]", "", "AKKASG015")]
    public void Should_ValidateTopLevelUnionCases(string before, string after, string diagnostic)
    {
        var source = CoverageCases.Replace(before, after) + """

            [AkkaSerializable(Manifest = "coverage-message")]
            public union CoverageMessage(PaidInvoice, Trial) : ISubscriptionEvent;
            """;
        RunPreview(source).GeneratorDiagnostics.Should().Contain(d => d.Id == diagnostic);
    }

    [Fact(DisplayName = "Should_RoundTripNullableValueTypeCase_When_UnionDeclaresIt")]
    public void Should_RoundTripNullableValueTypeCase_When_UnionDeclaresIt()
    {
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Unions.NullableCase;

            [AkkaSerializable(Manifest = "point")]
            public readonly record struct Point([property: AkkaField(1)] int X, [property: AkkaField(2)] int Y);

            [AkkaSerializable(Manifest = "label")]
            public sealed record Label([property: AkkaField(1)] string Text);

            public union Marker(Point?, Label);

            public interface IMarked;

            [AkkaSerializable(Manifest = "marked")]
            public sealed record Marked([property: AkkaField(1)] Marker Marker) : IMarked;

            [AkkaSerializer<IMarked>("marked", 152004)]
            public sealed partial class MarkedSerializer : AkkaSerializer
            {
                public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
            }

            public static class Samples
            {
                public static object[] All() => [new Marked(new Marker((Point?)new Point(3, 4))), new Marked(new Marker(new Label("x"))), new Marked(default)];
            }
            """;

        var result = RunPreview(source);
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Unions.NullableCase.MarkedSerializer");
        foreach (var original in (object[])Invoke(assembly, "Unions.NullableCase.Samples", "All")!)
            serializer.FromBinary(serializer.ToBinary(original), serializer.Manifest(original)).Should().Be(original);
    }

    [Fact(DisplayName = "Should_UseOwnSchema_When_AkkaSerializableStructAlsoImplementsIUnion")]
    public void Should_UseOwnSchema_When_AkkaSerializableStructAlsoImplementsIUnion()
    {
        // A hand-written [AkkaSerializable] struct that happens to implement IUnion keeps its own
        // field schema: treating it as a union would drop Currency and rebuild it through Money(Amount).
        const string source = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Unions.OwnSchema;

            [AkkaSerializable(Manifest = "amount")]
            public sealed record Amount([property: AkkaField(1)] decimal Value);

            [AkkaSerializable(Manifest = "money")]
            public readonly record struct Money([property: AkkaField(1)] Amount Amount, [property: AkkaField(2)] string Currency) : System.Runtime.CompilerServices.IUnion
            {
                public Money(Amount amount) : this(amount, "USD") { }

                public object? Value => Amount;
            }

            public interface IPriced;

            [AkkaSerializable(Manifest = "priced")]
            public sealed record Priced([property: AkkaField(1)] Money Price) : IPriced;

            [AkkaSerializer<IPriced>("priced", 152003)]
            public sealed partial class PricedSerializer : AkkaSerializer
            {
                public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
            }

            public static class Samples
            {
                public static object Euro() => new Priced(new Money(new Amount(12.5m), "EUR"));
            }
            """;

        var result = RunPreview(source);
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializer = CreateSerializer(assembly, "Unions.OwnSchema.PricedSerializer");
        var original = Invoke(assembly, "Unions.OwnSchema.Samples", "Euro")!;

        serializer.FromBinary(serializer.ToBinary(original), serializer.Manifest(original)).Should().Be(original);
    }

    [Fact(DisplayName = "Should_RecoverUnionEvents_When_PersistentActorIsRecreated")]
    public async Task Should_RecoverUnionEvents_When_PersistentActorIsRecreated()
    {
        var source = CoverageCases + """

            [AkkaSerializable(Manifest = "coverage-event")]
            public union CoverageEvent(PaidInvoice, Trial) : ISubscriptionEvent;

            public sealed class UnionActor : Akka.Persistence.ReceivePersistentActor
            {
                public override string PersistenceId => "union-recovery";

                public UnionActor(Akka.Actor.IActorRef probe)
                {
                    var recovered = new System.Collections.Generic.List<CoverageEvent>();
                    Recover<CoverageEvent>(e => recovered.Add(e));
                    Recover<Akka.Persistence.RecoveryCompleted>(_ => probe.Tell(recovered.ToArray(), Self));
                    Command<CoverageEvent>(e => Persist(e, persisted => probe.Tell(persisted, Self)));
                }
            }

            public static class RecoverySamples
            {
                public static object[] Events() => [(CoverageEvent)new PaidInvoice(42, 1001), (CoverageEvent)new Trial()];
                public static Akka.Actor.Props Props(Akka.Actor.IActorRef probe)
                    => Akka.Actor.Props.Create(() => new UnionActor(probe));
            }
            """;
        var result = GeneratorTestHarness.Run(
            new[] { new SourceFile("Recovery.cs", source), new SourceFile("Polyfills.cs", Polyfills) },
            GeneratorTestHarness.PreviewParseOptions,
            MetadataReference.CreateFromFile(typeof(ReceivePersistentActor).Assembly.Location));
        result.GeneratorDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var assembly = GeneratorTestHarness.EmitAndLoad(result);
        var serializerType = assembly.GetType("Unions.SubscriptionSerializer", throwOnError: true)!;
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(system => ImmutableHashSet.Create(
            (SerializerDetails)serializerType.GetMethod("CreateSerializerDetails")!.Invoke(null, new object[] { system })!)));
        var config = ConfigurationFactory.ParseString($"""
            akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
            akka.persistence.journal.inmem.class = "{typeof(SerializedPayloadJournal).AssemblyQualifiedName}"
            """);
        var system = ActorSystem.Create("union-recovery", setup.WithSetup(BootstrapSetup.Create().WithConfig(config)));
        try
        {
            var events = (object[])Invoke(assembly, "Unions.RecoverySamples", "Events")!;
            system.Serialization.FindSerializerFor(events[0]).GetType().Should().Be(serializerType);
            var props = (Props)Invoke(assembly, "Unions.RecoverySamples", "Props", TestActor)!;
            var actor = system.ActorOf(props);
            (await ExpectMsgAsync<Array>(TimeSpan.FromSeconds(10))).Length.Should().Be(0);
            foreach (var e in events)
            {
                actor.Tell(e);
                (await ExpectMsgAsync<object>(TimeSpan.FromSeconds(10))).Should().Be(e);
            }

            await WatchAsync(actor);
            system.Stop(actor);
            await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(10));
            var recovering = system.ActorOf(props);
            var recovered = await ExpectMsgAsync<Array>(TimeSpan.FromSeconds(10));
            recovered.Cast<object>().Should().Equal(events);
            system.Stop(recovering);
        }
        finally
        {
            await system.Terminate();
        }
    }

    // Store bytes rather than live payloads: the ordinary memory journal bypasses serialization.
    public sealed class SerializedPayloadJournal : MemoryJournal
    {
        private sealed record StoredPayload(int SerializerId, string Manifest, byte[] Bytes);

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            var serialization = Context.System.Serialization;
            return base.WriteMessagesAsync(messages.Select(write => new AtomicWrite(
                ((IImmutableList<IPersistentRepresentation>)write.Payload).Select(persistent =>
                {
                    var serializer = serialization.FindSerializerFor(persistent.Payload);
                    return persistent.WithPayload(new StoredPayload(serializer.Identifier,
                        Akka.Serialization.Serialization.ManifestFor(serializer, persistent.Payload), serializer.ToBinary(persistent.Payload)));
                }).ToImmutableList())), cancellationToken);
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var serialization = context.System.Serialization;
            return base.ReplayMessagesAsync(context, persistenceId, fromSequenceNr, toSequenceNr, max, persistent =>
            {
                var stored = (StoredPayload)persistent.Payload;
                recoveryCallback(persistent.WithPayload(serialization.Deserialize(stored.Bytes, stored.SerializerId, stored.Manifest)));
            });
        }
    }

    private Akka.Serialization.SerializerWithStringManifest CreateSerializer(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName, throwOnError: true)!;
        return (Akka.Serialization.SerializerWithStringManifest)Activator.CreateInstance(type, (ExtendedActorSystem)Sys)!;
    }

    private static object? Invoke(Assembly assembly, string typeName, string method, params object[] args)
        => assembly.GetType(typeName, throwOnError: true)!.GetMethod(method)!.Invoke(null, args);
}
