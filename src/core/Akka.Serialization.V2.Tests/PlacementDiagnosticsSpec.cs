//-----------------------------------------------------------------------
// <copyright file="PlacementDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Design.md Decision 19's placement diagnostics ("making the compiler complain: the pit of
/// success"): a misplaced serializer or message becomes a compile-time error or warning instead of
/// a silent gap. Three of the four rules from the decision page's table are diagnostics; the
/// fourth (the runtime "unsupported generated serializer type" exception text) has no compile-time
/// site and is instead a golden-output assertion here, since it is exercised only when a value
/// actually fails to match at run time.
/// </summary>
public sealed class PlacementDiagnosticsSpec
{
    [Fact(DisplayName = "AKKASG029 widens to referenced assemblies (Decision 19): an unmarked implementor of the protocol declared in a referenced assembly is reported at the serializer's own attribute")]
    public void ProtocolCoverage_widens_to_referenced_assembly_unmarked_implementors()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Placement.Case0.AssemblyA;

            public interface IOrders { }

            // Deliberately NOT [AkkaSerializable] -- implements the downstream protocol but forgot
            // the attribute, exactly the AKKASG029 shape, now visible from a referenced assembly.
            public sealed record OrderPlaced(string OrderId) : IOrders;

            // Assembly A's own metadata only names an assembly it actually USES (see
            // GeneratorCompilationFactsSpec's own test on this). OrderPlaced alone names nothing
            // from V2, so this otherwise-unused marker is what makes A's compiled AssemblyRef table
            // genuinely reference Akka.Serialization.V2 -- the walk's own pre-filter.
            [AkkaSerializable(Manifest = "marker-v1")]
            public sealed class UnrelatedMarker
            {
            }
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using Placement.Case0.AssemblyA;

            namespace Placement.Case0.AssemblyB;

            [AkkaSerializer<IOrders>("orders", 140000)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "Placement.Case0.A");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);
        var all = result.AllDiagnostics;

        var diagnostic = all.FirstOrDefault(d => d.Id == "AKKASG029");
        diagnostic.Should().NotBeNull();
        diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(null).Should().Contain("OrderPlaced");
        diagnostic.GetMessage(null).Should().Contain("IOrders");
        diagnostic.GetMessage(null).Should().Contain("OrdersSerializer");
        diagnostic.Location.Should().NotBe(Location.None);
    }

    [Fact(DisplayName = "AKKASG043: a type declared downstream that implements a protocol an UPSTREAM serializer already binds reports at the type's own declaration")]
    public void ProtocolOwnedUpstream_reports_at_the_local_type_declaration()
    {
        const string sourceA = """
            #nullable enable
            using System;
            using System.Buffers;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace Placement.Case1.AssemblyA;

            public interface IMessage { }

            // CompileToReference never runs the generator against assembly A (it supplies types
            // only), so this class is hand-written, not partial, and never actually invoked in this
            // test -- only its [AkkaSerializer<IMessage>] declaration matters to the Facts walk.
            [AkkaSerializer<IMessage>("core", 140001)]
            public sealed class CoreSerializer : AkkaSerializer
            {
                public CoreSerializer(ExtendedActorSystem system) : base(system) { }
                public override int Identifier => 140001;
                public override string Manifest(object obj) => throw new NotSupportedException();
                public override int Serialize(object obj, IBufferWriter<byte> writer) => throw new NotSupportedException();
                public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => throw new NotSupportedException();
            }
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Serialization.V2;
            using Placement.Case1.AssemblyA;

            namespace Placement.Case1.AssemblyB;

            [AkkaSerializable(Manifest = "downstream-v1")]
            public sealed record AcceptCassette([property: AkkaField(1)] int Layer) : IMessage;
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "Placement.Case1.A");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);
        var all = result.AllDiagnostics;

        var diagnostic = all.FirstOrDefault(d => d.Id == "AKKASG043");
        diagnostic.Should().NotBeNull();
        diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(null).Should().Contain("AcceptCassette");
        diagnostic.GetMessage(null).Should().Contain("IMessage");
        diagnostic.GetMessage(null).Should().Contain("CoreSerializer");
        diagnostic.GetMessage(null).Should().Contain("Placement.Case1.A");

        // Reports at the LOCAL type declaration in B -- the only local site there is, per the S6
        // location rule -- so the location must resolve to a real site, not the Location.None
        // fallback DiagnosticRegistry.ToDiagnostic uses when a LocationKey has no entry. Every
        // location in this generator is file-path/span based (LocationSpec.ToLocation), not a live
        // SyntaxTree reference -- by design, so the cached location bag stays symbol-free -- so a
        // real site never carries a SourceTree either; NotBe(Location.None) is the correct check.
        diagnostic.Location.Should().NotBe(Location.None);
    }

    [Fact(DisplayName = "AKKASG044: a serializer with no local messages, no referenced-assembly messages, and no registrations gets a Warning at its own attribute")]
    public void SerializerHasNoMessages_reports_a_warning_at_the_serializer_attribute()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace Placement.Case2;

            public interface IEmptyProtocol { }

            [AkkaSerializer<IEmptyProtocol>("empty", 140002)]
            public sealed partial class EmptySerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var result = GeneratorTestHarness.Run(source);
        var all = result.AllDiagnostics;

        var diagnostic = all.FirstOrDefault(d => d.Id == "AKKASG044");
        diagnostic.Should().NotBeNull();
        diagnostic!.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage(null).Should().Contain("EmptySerializer");
        diagnostic.GetMessage(null).Should().Contain("IEmptyProtocol");

        // Advisory only: still compiles clean otherwise.
        all.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact(DisplayName = "AKKASG044 does not fire when the serializer's only messages come from a referenced assembly (Decision 19 coverage)")]
    public void SerializerHasNoMessages_does_not_fire_when_messages_are_only_upstream()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace Placement.Case2b.AssemblyA;

            public interface IOrders { }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrders;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using Placement.Case2b.AssemblyA;

            namespace Placement.Case2b.AssemblyB;

            [AkkaSerializer<IOrders>("orders", 140003)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "Placement.Case2b.A");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);

        result.AllDiagnostics.Should().NotContain(d => d.Id == "AKKASG044");
    }

    [Fact(DisplayName = "AKKASG031 (extended): a LOCAL serializer binding the same protocol as an UPSTREAM serializer collides, reported at the local serializer's own attribute")]
    public void DuplicateProtocolBinding_extends_across_assemblies()
    {
        const string sourceA = """
            #nullable enable
            using System;
            using System.Buffers;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace Placement.Case3.AssemblyA;

            public interface IMessage { }

            // Hand-written, not partial -- see the identical comment in
            // ProtocolOwnedUpstream_reports_at_the_local_type_declaration above.
            [AkkaSerializer<IMessage>("core", 140004)]
            public sealed class CoreSerializer : AkkaSerializer
            {
                public CoreSerializer(ExtendedActorSystem system) : base(system) { }
                public override int Identifier => 140004;
                public override string Manifest(object obj) => throw new NotSupportedException();
                public override int Serialize(object obj, IBufferWriter<byte> writer) => throw new NotSupportedException();
                public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => throw new NotSupportedException();
            }
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using Placement.Case3.AssemblyA;

            namespace Placement.Case3.AssemblyB;

            [AkkaSerializer<IMessage>("other", 140005)]
            public sealed partial class OtherSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "Placement.Case3.A");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);
        var all = result.AllDiagnostics;

        var diagnostic = all.FirstOrDefault(d => d.Id == "AKKASG031");
        diagnostic.Should().NotBeNull();
        diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(null).Should().Contain("IMessage");
        diagnostic.GetMessage(null).Should().Contain("OtherSerializer");
        diagnostic.GetMessage(null).Should().Contain("CoreSerializer");
        diagnostic.GetMessage(null).Should().Contain("Placement.Case3.A");

        diagnostic.Location.Should().NotBe(Location.None);
    }

    [Fact(DisplayName = "Rule 4: the runtime 'unsupported generated serializer type' exception names both the value's own assembly and the generating serializer's assembly")]
    public void UnsupportedTypeExceptionText_names_both_assemblies()
    {
        const string source = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace Placement.Case4;

            public interface IMessage { }

            [AkkaSerializable(Manifest = "known-v1")]
            public sealed record Known([property: AkkaField(1)] int Value) : IMessage;

            [AkkaSerializer<IMessage>("known", 140006)]
            public sealed partial class KnownSerializer : AkkaSerializer
            { public static partial SerializerRegistration CreateRegistration(); }
            """;

        var result = GeneratorTestHarness.Run(source);
        var generated = string.Join(Environment.NewLine, result.RunResult.GeneratedTrees.Select(tree => tree.ToString()));

        generated.Should().Contain("obj.GetType().Assembly.GetName().Name");
        generated.Should().Contain("This serializer was generated in assembly");
    }
}
