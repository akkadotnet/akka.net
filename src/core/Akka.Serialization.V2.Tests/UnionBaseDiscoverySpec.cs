//-----------------------------------------------------------------------
// <copyright file="UnionBaseDiscoverySpec.cs" company="Akka.NET Project">
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
/// Design.md Decision 21 ("Unions Without A Member List"): a field typed as an interface or
/// abstract class is a union over "every visible [AkkaSerializable] implementor", discovered at
/// build time with no attribute anywhere. Ships with the same referenced-assembly walk as Decision
/// 19 (<see cref="CrossAssemblyBaselineSpec"/>, <see cref="PlacementDiagnosticsSpec"/>), so this
/// file focuses on discovery itself: local, empty, and across the assembly boundary.
/// </summary>
public sealed class UnionBaseDiscoverySpec
{
    [Fact(DisplayName = "An interface field discovers every local [AkkaSerializable] implementor, with no attribute")]
    public void Local_interface_field_discovers_local_implementors()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case1;

            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            public sealed record OrderCancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrderProtocol;

            public interface IOrderProtocol { }

            [AkkaSerializer<IOrderProtocol>("orders", 140101)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
            """;

        var result = GeneratorTestHarness.Run(source);
        var all = result.AllDiagnostics;

        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        all.Should().NotContain(d => d.Id == "AKKASG003");

        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("WriteOrderPlaced").And.Contain("WriteOrderCancelled");
        generated.Should().Contain("placed-v1").And.Contain("cancelled-v1");
    }

    [Fact(DisplayName = "An interface field with zero visible implementors reports AKKASG019 and emits no serializer")]
    public void Local_interface_field_with_no_implementors_reports_AKKASG019()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case1b;

            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrderProtocol;

            public interface IOrderProtocol { }

            [AkkaSerializer<IOrderProtocol>("orders", 140102)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
            """;

        var result = GeneratorTestHarness.Run(source);

        // A union with no members can only throw: report it instead of emitting a dispatch helper
        // with no arms (its dead code raised CS0162, which fails a warnings-as-errors build).
        result.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "AKKASG019" && d.Severity == DiagnosticSeverity.Error)
            .Which.GetMessage(null).Should().Contain("IOrderEvent");
        result.RunResult.GeneratedTrees.Should().NotContain(tree => tree.FilePath.EndsWith("OrdersSerializer.AkkaSerialization.g.cs", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "An interface declared upstream discovers implementors on BOTH sides of the assembly boundary")]
    public void Interface_union_spans_the_assembly_boundary()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case2.AssemblyA;

            public interface IDestination { }

            [AkkaSerializable(Manifest = "dest-comms-v1")]
            public sealed record CommsDestination([property: AkkaField(1)] string Host) : IDestination;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MarkedUnion.Case2.AssemblyA;

            namespace MarkedUnion.Case2.AssemblyB;

            [AkkaSerializable(Manifest = "dest-tech-v1")]
            public sealed record TechDestination([property: AkkaField(1)] string Cluster) : IDestination;

            [AkkaSerializable(Manifest = "envelope-v1")]
            public sealed record Envelope([property: AkkaField(1)] IDestination Destination) : IEnvelopeProtocol;

            public interface IEnvelopeProtocol { }

            [AkkaSerializer<IEnvelopeProtocol>("envelope", 140103)]
            public sealed partial class EnvelopeSerializer : AkkaSerializer
            { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "MarkedUnion.Case2.A");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);
        var all = result.AllDiagnostics;

        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        all.Should().NotContain(d => d.Id == "AKKASG003" || d.Id == "AKKASG015" || d.Id == "AKKASG019");

        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));

        // Both the referenced-assembly member (CommsDestination) and the local one (TechDestination)
        // are in the discovered set: the union helper dispatches both.
        generated.Should().Contain("WriteCommsDestination").And.Contain("WriteTechDestination");
        generated.Should().Contain("dest-comms-v1").And.Contain("dest-tech-v1");
    }

    private const string DestinationImplementations = """
        [AkkaSerializable(Manifest = "dest-comms-v1")]
        public sealed record CommsDestination([property: AkkaField(1)] string Host) : IDestination;

        [AkkaSerializable(Manifest = "dest-tech-v1")]
        public sealed record TechDestination([property: AkkaField(1)] string Cluster) : IDestination;
        """;

    private const string EnvelopeMessage = """
        public interface IEnvelopeProtocol { }

        [AkkaSerializable(Manifest = "envelope-v1")]
        public sealed record Envelope([property: AkkaField(1)] IDestination Destination) : IEnvelopeProtocol;
        """;

    private const string EnvelopeSerializer = """

        [AkkaSerializer<IEnvelopeProtocol>("envelope", 140104)]
        public sealed partial class EnvelopeSerializer : AkkaSerializer
        { public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system); }
        """;

    private const string Header = "#nullable enable\nusing Akka.Serialization.V2;\n";

    private static void AssertBothDestinationsDiscovered(Harness.GeneratorRunResult result)
    {
        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("dest-comms-v1").And.Contain("dest-tech-v1");
    }

    [Fact(DisplayName = "Should_DiscoverImplementors_When_InterfaceIsInAImplementorsInBAndMessageWithSerializerInC")]
    public void Should_DiscoverImplementors_When_InterfaceIsInAImplementorsInBAndMessageWithSerializerInC()
    {
        var assemblyA = GeneratorTestHarness.CompileToReference(Header + "namespace ThreeWay.Case1.A;\npublic interface IDestination { }\n", "ThreeWay.Case1.A");
        var assemblyB = GeneratorTestHarness.CompileToReference(Header + "using ThreeWay.Case1.A;\nnamespace ThreeWay.Case1.B;\n" + DestinationImplementations, "ThreeWay.Case1.B", assemblyA);

        var result = GeneratorTestHarness.Run(Header + "using ThreeWay.Case1.A;\nnamespace ThreeWay.Case1.C;\n" + EnvelopeMessage + EnvelopeSerializer, assemblyA, assemblyB);

        AssertBothDestinationsDiscovered(result);
    }

    [Fact(DisplayName = "Should_DiscoverImplementors_When_MessageWithInterfaceFieldIsInAImplementorsInBAndSerializerInC")]
    public void Should_DiscoverImplementors_When_MessageWithInterfaceFieldIsInAImplementorsInBAndSerializerInC()
    {
        // The message itself comes from A's metadata. Its IDestination field's key must still be
        // requested from the referenced-assembly walk, or B's implementors are never collected and
        // the union resolves empty (AKKASG019).
        var assemblyA = GeneratorTestHarness.CompileToReference(Header + "namespace ThreeWay.Case2.A;\npublic interface IDestination { }\n" + EnvelopeMessage, "ThreeWay.Case2.A");
        var assemblyB = GeneratorTestHarness.CompileToReference(Header + "using ThreeWay.Case2.A;\nnamespace ThreeWay.Case2.B;\n" + DestinationImplementations, "ThreeWay.Case2.B", assemblyA);

        var result = GeneratorTestHarness.Run(Header + "using ThreeWay.Case2.A;\nnamespace ThreeWay.Case2.C;\n" + EnvelopeSerializer, assemblyA, assemblyB);

        AssertBothDestinationsDiscovered(result);
    }
}
