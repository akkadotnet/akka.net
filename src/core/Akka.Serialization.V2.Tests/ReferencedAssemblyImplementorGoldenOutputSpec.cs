//-----------------------------------------------------------------------
// <copyright file="ReferencedAssemblyImplementorGoldenOutputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Golden-output gate for Decisions 19 and 21 (referenced-assembly protocol implementors, marked
/// union bases, and <c>ManifestPrefix</c> expansion over a closed set that spans an assembly
/// boundary; openspec/changes/messagepack-sourcegen-validation/design.md). Each case is pinned
/// against a checked-in baseline the same way <see cref="CrossAssemblyGoldenOutputSpec"/> and
/// <see cref="ClosedGenericExpansionGoldenOutputSpec"/> already pin Decisions 16 and 18.
/// </summary>
public sealed class ReferencedAssemblyImplementorGoldenOutputSpec
{
    private const string RegenEnvVar = "AKKA_GOLDEN_REGEN";

    [Fact(DisplayName = "Referenced-assembly implementor golden: a protocol implementor declared only in a referenced assembly gets a dispatch arm byte-identical to a checked-in baseline")]
    public void Should_EmitByteIdenticalOutput_ForReferencedAssemblyImplementor()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace ReferencedImplementorGolden.AssemblyA;

            public interface IOrders
            {
            }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrders;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using ReferencedImplementorGolden.AssemblyA;

            namespace ReferencedImplementorGolden.AssemblyB;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            public sealed record OrderCancelled([property: AkkaField(1)] string OrderId) : IOrders;

            [AkkaSerializer<IOrders>("referenced-implementor-golden", 190101)]
            public sealed partial class OrdersSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "ReferencedImplementorGoldenAssemblyA");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);

        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratorDiagnostics.Should().BeEmpty();

        AssertMatchesGoldenBaseline("OrdersSerializer.AkkaSerialization.g.cs", result);
    }

    [Fact(DisplayName = "Marked-union-base golden: a parameterless [AkkaUnion] discovers implementors on both sides of the assembly boundary, byte-identical to a checked-in baseline")]
    public void Should_EmitByteIdenticalOutput_ForMarkedUnionBaseAcrossTheBoundary()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace MarkedUnionGolden.AssemblyA;

            [AkkaUnion]
            public interface IDestination
            {
            }

            [AkkaSerializable(Manifest = "dest-comms-v1")]
            public sealed record CommsDestination([property: AkkaField(1)] string Host) : IDestination;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using MarkedUnionGolden.AssemblyA;

            namespace MarkedUnionGolden.AssemblyB;

            [AkkaSerializable(Manifest = "dest-tech-v1")]
            public sealed record TechDestination([property: AkkaField(1)] string Cluster) : IDestination;

            [AkkaSerializable(Manifest = "envelope-v1")]
            public sealed record Envelope([property: AkkaField(1)] IDestination Destination) : IEnvelopeProtocol;

            public interface IEnvelopeProtocol
            {
            }

            [AkkaSerializer<IEnvelopeProtocol>("marked-union-golden", 190102)]
            public sealed partial class EnvelopeSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "MarkedUnionGoldenAssemblyA");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);

        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratorDiagnostics.Should().BeEmpty();

        AssertMatchesGoldenBaseline("EnvelopeSerializer.AkkaSerialization.g.cs", result);
    }

    [Fact(DisplayName = "Prefix-expansion golden: a ManifestPrefix registration whose closed set spans the assembly boundary emits one construction per member, byte-identical to a checked-in baseline")]
    public void Should_EmitByteIdenticalOutput_ForPrefixExpansionAcrossTheBoundary()
    {
        const string sourceA = """
            #nullable enable
            using Akka.Serialization.V2;

            namespace PrefixExpansionGolden.AssemblyA;

            public interface ICommsMessage
            {
            }

            [AkkaSerializable(Manifest = "placed-v1")]
            public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : ICommsMessage;
            """;

        const string sourceB = """
            #nullable enable
            using Akka.Actor;
            using Akka.Serialization.V2;
            using PrefixExpansionGolden.AssemblyA;

            namespace PrefixExpansionGolden.AssemblyB;

            [AkkaSerializable(Manifest = "cancelled-v1")]
            public sealed record OrderCancelled([property: AkkaField(1)] string OrderId) : ICommsMessage;

            [AkkaSerializable]
            public sealed record Envelope<T>(
                [property: AkkaField(1)] T Message,
                [property: AkkaField(2)] string TraceId);

            [AkkaSerializer<ICommsMessage>("prefix-expansion-golden", 190103)]
            [AkkaSerializable<Envelope<ICommsMessage>>(ManifestPrefix = "env")]
            public sealed partial class CommsSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }
            """;

        var assemblyA = GeneratorTestHarness.CompileToReference(sourceA, "PrefixExpansionGoldenAssemblyA");
        var result = GeneratorTestHarness.Run(sourceB, assemblyA);

        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        // AKKASG042 (info) fires once for the expansion -- expected, not a failure.
        result.GeneratorDiagnostics.Should().OnlyContain(d => d.Id == "AKKASG042");

        AssertMatchesGoldenBaseline("CommsSerializer.AkkaSerialization.g.cs", result);
    }

    private static void AssertMatchesGoldenBaseline(string hintName, Harness.GeneratorRunResult result)
    {
        result.GeneratedSources.ContainsKey(hintName).Should().BeTrue($"the generator should have emitted [{hintName}]");
        var actual = result.GeneratedSources[hintName];

        var goldenDirectory = GetGoldenDirectory();
        var verifiedPath = Path.Combine(goldenDirectory, hintName + ".verified.txt");

        if (Environment.GetEnvironmentVariable(RegenEnvVar) == "1")
        {
            Directory.CreateDirectory(goldenDirectory);
            File.WriteAllText(verifiedPath, actual);
            return;
        }

        if (!File.Exists(verifiedPath))
        {
            throw new InvalidOperationException($"Missing baseline [{verifiedPath}]. Run once with {RegenEnvVar}=1 to capture it, then review and check it in.");
        }

        var expected = File.ReadAllText(verifiedPath);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            var receivedPath = Path.Combine(goldenDirectory, hintName + ".received.txt");
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"Generated output for [{hintName}] differs from baseline. Actual output written to [{receivedPath}] for diffing against [{verifiedPath}].");
        }
    }

    private static string GetGoldenDirectory([CallerFilePath] string sourceFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "GoldenOutput");
    }
}
