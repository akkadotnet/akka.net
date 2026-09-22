//-----------------------------------------------------------------------
// <copyright file="ClosedGenericExpansionGoldenOutputSpec.cs" company="Akka.NET Project">
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
/// Golden-output gate for Decision 18 (openspec/changes/messagepack-sourcegen-validation/design.md,
/// "Closed-Set Expansion And Adoption On The Serializer"). <see cref="GeneratedClosedGenericExpansionSpec"/>
/// and <see cref="ClosedGenericExpansionDiagnosticsSpec"/> already prove the round-trip behavior and
/// the diagnostics; this spec pins the FULL generated text for four shapes the design calls out by
/// name, so any future drift in emission is caught here: a closed construction with a manifest
/// derived by the Decision 18 formula, a protocol-typed field resolved as an implicit union, a
/// nested construction (a fixed type argument that is itself a separately-registered generic
/// construction), and a concrete type adopted from a referenced assembly.
/// </summary>
public sealed class ClosedGenericExpansionGoldenOutputSpec
{
    private const string RegenEnvVar = "AKKA_GOLDEN_REGEN";
    private const string LocalHintName = "ExpansionGoldenSerializer.AkkaSerialization.g.cs";
    private const string CrossAssemblyHintName = "ExpansionGoldenCrossAssemblySerializer.AkkaSerialization.g.cs";

    // Local corpus: covers three of the four required shapes in one compilation.
    //   - ManifestPrefix expansion with a derived manifest: Envelope<Cassette>/Envelope<Notice>.
    //   - The literal construction's protocol-interface field as an implicit union: Envelope<IComms>.Message.
    //   - A nested construction: Pair<IComms, Envelope<Cassette>>'s second argument is a FIXED,
    //     separately-registered generic construction, not itself a closed set.
    private const string LocalSource = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;

        namespace ExpansionGolden;

        public interface IComms
        {
        }

        [AkkaSerializable(Manifest = "cassette-v1")]
        public sealed record Cassette([property: AkkaField(1)] int Layer) : IComms;

        [AkkaSerializable(Manifest = "notice-v1")]
        public sealed record Notice([property: AkkaField(1)] string Text) : IComms;

        [AkkaSerializable]
        public sealed record Envelope<T>(
            [property: AkkaField(1)] T Message,
            [property: AkkaField(2)] string TraceId);

        [AkkaSerializable]
        public sealed record Pair<TFirst, TSecond>(
            [property: AkkaField(1)] TFirst First,
            [property: AkkaField(2)] TSecond Second);

        [AkkaSerializer<IComms>("expansion-golden", 190101)]
        [AkkaSerializable<Envelope<IComms>>(ManifestPrefix = "env", Manifest = "env-any")]
        [AkkaSerializable<Envelope<Cassette>>(Manifest = "env-cassette-v1")]
        [AkkaSerializable<Pair<IComms, Envelope<Cassette>>>(ManifestPrefix = "pair")]
        public sealed partial class ExpansionGoldenSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    // Assembly A: Wrapper<T> mirrors the customer's own generic envelope (it implements no
    // protocol, exactly like Envelope<T> in the design record's own example) plus an ordinary
    // concrete type, AuditStamp, that implements no protocol either.
    private const string CrossAssemblySourceA = """
        #nullable enable
        using Akka.Serialization.V2;

        namespace ExpansionGoldenCrossAssembly.AssemblyA;

        [AkkaSerializable]
        public sealed record Wrapper<T>([property: AkkaField(1)] T Payload);

        [AkkaSerializable(Manifest = "audit-v1")]
        public sealed record AuditStamp([property: AkkaField(1)] string ActorPath);
        """;

    // Assembly B: adopts AuditStamp -- a concrete, non-generic, non-protocol type declared in a
    // referenced assembly -- with an overriding Manifest, plus registers Wrapper<int> the same way
    // Decision 16's own cross-assembly golden spec does, so this corpus stays focused on the ONE new
    // shape (a referenced-assembly concrete adoption) instead of duplicating that spec's coverage.
    private const string CrossAssemblySourceB = """
        #nullable enable
        using Akka.Actor;
        using Akka.Serialization.V2;
        using ExpansionGoldenCrossAssembly.AssemblyA;

        namespace ExpansionGoldenCrossAssembly.AssemblyB;

        public interface IComms
        {
        }

        [AkkaSerializable(Manifest = "ping-v1")]
        public sealed record Ping([property: AkkaField(1)] int Value) : IComms;

        [AkkaSerializer<IComms>("expansion-golden-cross-assembly", 190102)]
        [AkkaSerializable<AuditStamp>(Manifest = "audit-override-v1")]
        public sealed partial class ExpansionGoldenCrossAssemblySerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    [Fact(DisplayName = "Closed-set expansion golden: a derived manifest, an implicit protocol union, and a nested construction emit output byte-identical to a checked-in baseline")]
    public void Should_EmitByteIdenticalOutput_ForLocalExpansionCorpus()
    {
        var result = GeneratorTestHarness.Run(LocalSource);

        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "the expansion golden corpus is designed to compile cleanly");
        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "the expansion golden corpus is designed to be error-free (AKKASG042's info count diagnostic is expected and is not an error)");

        AssertMatchesGoldenBaseline(LocalHintName, result);
    }

    [Fact(DisplayName = "Closed-set expansion golden: adopting a concrete, non-generic type from a referenced assembly emits output byte-identical to a checked-in baseline")]
    public void Should_EmitByteIdenticalOutput_ForCrossAssemblyAdoption()
    {
        var assemblyA = GeneratorTestHarness.CompileToReference(CrossAssemblySourceA, "ExpansionGoldenCrossAssemblyA");
        var result = GeneratorTestHarness.Run(CrossAssemblySourceB, assemblyA);

        result.CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "the cross-assembly adoption golden corpus is designed to compile cleanly");
        result.GeneratorDiagnostics.Should().BeEmpty("the cross-assembly adoption golden corpus is designed to be diagnostics-clean");

        AssertMatchesGoldenBaseline(CrossAssemblyHintName, result);
    }

    private static void AssertMatchesGoldenBaseline(string hintName, Akka.Serialization.V2.Tests.Harness.GeneratorRunResult result)
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
