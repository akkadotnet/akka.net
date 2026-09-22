//-----------------------------------------------------------------------
// <copyright file="CrossAssemblyGoldenOutputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Golden-output gate for Decision 16 (schemas from referenced-assembly metadata,
/// openspec/changes/messagepack-sourcegen-validation/design.md): a nested field type, a union
/// member type, and a generic definition closed locally, each declared in a referenced assembly
/// instead of the serializer's own compilation. <see cref="CrossAssemblyBaselineSpec"/> already
/// proves these cases compile clean and emit the expected helper names; this spec proves the FULL
/// generated text is what Decision 16 promises: byte-identical to what the same declarations would
/// produce locally, except for the namespace, and pinned against a checked-in baseline so any future
/// drift in emission is caught here too.
/// </summary>
public sealed class CrossAssemblyGoldenOutputSpec
{
    private const string RegenEnvVar = "AKKA_GOLDEN_REGEN";
    private const string HintName = "CrossAssemblyGoldenSerializer.AkkaSerialization.g.cs";

    // Assembly A: a nested field type (Money), a type-level union with two members (Placed,
    // Cancelled), and a generic definition (Wrapper<T>) -- Decision 16's three referenced-assembly
    // uses. The generator never runs over this compilation; A only supplies types (see
    // GeneratorTestHarness.CompileToReference).
    private const string AssemblySourceTemplate = """
        #nullable enable
        using Akka.Serialization.V2;

        namespace CrossAssemblyGolden.{0};

        [AkkaSerializable]
        public sealed record Money([property: AkkaField(1)] long Cents);

        [AkkaUnion(typeof(Placed), typeof(Cancelled))]
        public interface IOrderEvent
        {{
        }}

        [AkkaSerializable(Manifest = "placed-v1")]
        public sealed record Placed([property: AkkaField(1)] string OrderId) : IOrderEvent;

        [AkkaSerializable(Manifest = "cancelled-v1")]
        public sealed record Cancelled([property: AkkaField(1)] string OrderId) : IOrderEvent;

        [AkkaSerializable]
        public sealed record Wrapper<T>([property: AkkaField(1)] string Id, [property: AkkaField(2)] T Payload);
        """;

    // Assembly B: one serializer that adopts all three referenced-assembly shapes -- Pay nests
    // Money, OrderNotice carries a union field over IOrderEvent, and Wrapper<int> is registered as
    // a closed generic construction (Decision 13's own existing mechanism, unaffected by Decision
    // 16, included here so the golden file also pins that this case keeps working alongside the two
    // new ones). {0} is the namespace the referenced types live in -- CrossAssemblyGolden.AssemblyA
    // for the real cross-assembly run, CrossAssemblyGolden.Local for the same-compilation comparison
    // run -- so the two runs are identical source text apart from that one token.
    private const string SerializerSourceTemplate = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;
        using CrossAssemblyGolden.{0};

        namespace CrossAssemblyGolden.AssemblyB;

        public interface IComms
        {{
        }}

        [AkkaSerializable(Manifest = "pay-v1")]
        public sealed record Pay([property: AkkaField(1)] Money Amount) : IComms;

        [AkkaSerializable(Manifest = "order-notice-v1")]
        public sealed record OrderNotice([property: AkkaField(1)] IOrderEvent Event) : IComms;

        [AkkaSerializable(Manifest = "holder-v1")]
        public sealed record Holder([property: AkkaField(1)] Wrapper<int> Count) : IComms;

        [AkkaSerializer<IComms>("cross-assembly-golden", 190001)]
        [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
        public sealed partial class CrossAssemblyGoldenSerializer : AkkaSerializer
        {{
            public static partial SerializerRegistration CreateRegistration();
        }}
        """;

    [Fact(DisplayName = "Cross-assembly golden: a nested field, a union member, and a closed generic from a referenced assembly emit output byte-identical to a checked-in baseline")]
    public void Should_EmitByteIdenticalOutput_When_TypesComeFromReferencedAssembly()
    {
        var assemblyA = GeneratorTestHarness.CompileToReference(
            string.Format(AssemblySourceTemplate, "AssemblyA"), "CrossAssemblyGoldenAssemblyA");
        var sourceB = string.Format(SerializerSourceTemplate, "AssemblyA");

        var result = GeneratorTestHarness.Run(sourceB, assemblyA);
        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "the cross-assembly golden corpus is designed to compile cleanly");
        result.GeneratorDiagnostics.Should().BeEmpty("the cross-assembly golden corpus is designed to be diagnostics-clean");

        result.GeneratedSources.ContainsKey(HintName).Should().BeTrue($"the generator should have emitted [{HintName}]");
        var actual = result.GeneratedSources[HintName];

        var goldenDirectory = GetGoldenDirectory();
        var verifiedPath = Path.Combine(goldenDirectory, HintName + ".verified.txt");

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
            var receivedPath = Path.Combine(goldenDirectory, HintName + ".received.txt");
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"Generated output for [{HintName}] differs from baseline. Actual output written to [{receivedPath}] for diffing against [{verifiedPath}].");
        }
    }

    [Fact(DisplayName = "Cross-assembly golden: the emitted nested/union/closed-generic helpers are byte-identical to what the same declarations produce locally, except for the namespace")]
    public void Should_MatchLocalDeclarationOutput_ExceptForNamespace()
    {
        var assemblyA = GeneratorTestHarness.CompileToReference(
            string.Format(AssemblySourceTemplate, "AssemblyA"), "CrossAssemblyGoldenAssemblyA2");
        var crossAssemblySource = string.Format(SerializerSourceTemplate, "AssemblyA");
        var crossAssemblyResult = GeneratorTestHarness.Run(crossAssemblySource, assemblyA);
        crossAssemblyResult.GeneratorDiagnostics.Should().BeEmpty();

        // Same types, same shapes, same manifests -- declared in the SAME compilation as the
        // serializer instead of a referenced one, under a different namespace. The generated helper
        // bodies (Write/Read/SizeOf for Money, Placed, Cancelled, and WrapperInt) must be identical
        // to the cross-assembly run once that one namespace token is normalized away: Decision 16's
        // whole premise is that a metadata-derived schema is indistinguishable from a local one.
        var localTypesSource = string.Format(AssemblySourceTemplate, "Local");
        var localSerializerSource = string.Format(SerializerSourceTemplate, "Local");
        var localResult = GeneratorTestHarness.Run(new[]
        {
            new SourceFile("Types.cs", localTypesSource),
            new SourceFile("Serializer.cs", localSerializerSource)
        });
        localResult.GeneratorDiagnostics.Should().BeEmpty();

        crossAssemblyResult.GeneratedSources.ContainsKey(HintName).Should().BeTrue($"the generator should have emitted [{HintName}]");
        localResult.GeneratedSources.ContainsKey(HintName).Should().BeTrue($"the generator should have emitted [{HintName}]");

        var crossAssemblyNormalized = crossAssemblyResult.GeneratedSources[HintName]
            .Replace("CrossAssemblyGolden.AssemblyA", "CrossAssemblyGolden.NAMESPACE_TOKEN", StringComparison.Ordinal);
        var localNormalized = localResult.GeneratedSources[HintName]
            .Replace("CrossAssemblyGolden.Local", "CrossAssemblyGolden.NAMESPACE_TOKEN", StringComparison.Ordinal);

        crossAssemblyNormalized.Should().Be(localNormalized,
            "a metadata-derived schema must generate byte-identical code to the same declaration made locally, once the namespace difference is normalized away");
    }

    private static string GetGoldenDirectory([CallerFilePath] string sourceFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "GoldenOutput");
    }
}
