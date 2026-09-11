//-----------------------------------------------------------------------
// <copyright file="MarkedUnionBaseSpec.cs" company="Akka.NET Project">
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
/// Design.md Decision 21 ("Unions Without A Member List"): <c>[AkkaUnion]</c> with no arguments on
/// an interface or abstract class declares the closed set "every visible [AkkaSerializable]
/// implementor", discovered at build time instead of listed. Ships with the same referenced-
/// assembly walk as Decision 19 (<see cref="CrossAssemblyBaselineSpec"/>, <see cref="PlacementDiagnosticsSpec"/>),
/// so this file focuses on the marker form itself: local discovery, cross-assembly discovery, and
/// that the explicit list keeps working unaffected.
/// </summary>
public sealed class MarkedUnionBaseSpec
{
    [Fact(DisplayName = "A parameterless [AkkaUnion] on a local interface discovers every local [AkkaSerializable] implementor, with no listed members")]
    public void Local_marked_union_base_discovers_local_implementors()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case1;

            [AkkaUnion]
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
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var result = GeneratorTestHarness.Run(source);
        var all = result.AllDiagnostics;

        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        all.Should().NotContain(d => d.Id == "AKKASG003");

        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));
        generated.Should().Contain("WriteOrderPlaced").And.Contain("WriteOrderCancelled");
        generated.Should().Contain("placed-v1").And.Contain("cancelled-v1");
    }

    [Fact(DisplayName = "A parameterless [AkkaUnion] with zero visible implementors compiles clean and emits an (empty) union, rather than failing AKKASG019's empty-set rule")]
    public void Local_marked_union_base_with_no_implementors_is_not_an_error()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case1b;

            [AkkaUnion]
            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrderProtocol;

            public interface IOrderProtocol { }

            [AkkaSerializer<IOrderProtocol>("orders", 140102)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var result = GeneratorTestHarness.Run(source);

        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.AllDiagnostics.Should().NotContain(d => d.Id == "AKKASG019");
    }

    [Fact(DisplayName = "A parameterless [AkkaUnion] declared upstream discovers implementors on BOTH sides of the assembly boundary")]
    public void Marked_union_base_spans_the_assembly_boundary()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case2.AssemblyA;

            [AkkaUnion]
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
            { public static partial SerializerRegistration CreateRegistration(); }
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

    [Fact(DisplayName = "An explicit type-level [AkkaUnion(typeof(...))] list is unaffected by Decision 21 and still works")]
    public void Explicit_union_list_still_works()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace MarkedUnion.Case3;

            [AkkaUnion(typeof(Placed), typeof(Cancelled))]
            public interface IOrderEvent { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record Placed([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            public sealed record Cancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;

            // A third implementor exists but is deliberately NOT in the explicit list -- proving the
            // list, not discovery, still governs this form.
            [AkkaSerializable(Manifest = "amended-v1")]
            public sealed record Amended([property: AkkaField(1)] string OrderId) : IOrderEvent;

            [AkkaSerializable(Manifest = "notice-v1")]
            public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IOrderProtocol;

            public interface IOrderProtocol { }

            [AkkaSerializer<IOrderProtocol>("orders", 140104)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var result = GeneratorTestHarness.Run(source);
        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));

        result.AllDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generated.Should().Contain("WritePlaced").And.Contain("WriteCancelled");
        generated.Should().NotContain("WriteAmended");
    }
}
