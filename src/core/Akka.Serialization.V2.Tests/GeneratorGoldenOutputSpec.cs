//-----------------------------------------------------------------------
// <copyright file="GeneratorGoldenOutputSpec.cs" company="Akka.NET Project">
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
using System.Runtime.CompilerServices;
using System.Text;
using Akka.Actor;
using Akka.Serialization.V2.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Golden-output gate for the generator's EMISSION layer: drives the generator over a corpus that
/// exercises every emission path (scalars, all ten collection shapes, immutable collections,
/// nullable variants, nested messages, closed generic registrations, type-level and field-level
/// unions, envelope payloads, formatters with both constructor kinds, hybrid
/// constructor+initializer reconstruction, keyword-named/adversarial properties, fieldless
/// messages, struct messages, manifest escaping, internal accessibility, and a global-namespace
/// serializer) and asserts the FULL generated text of every hint name is byte-identical to a
/// checked-in baseline under <c>GoldenOutput/</c>.
/// </summary>
/// <remarks>
/// The baselines were captured from the pre-refactor StringBuilder emitter and act as the proof
/// obligation for the CodeWriter migration: any refactor of the emission layer must keep this spec
/// green without regenerating the baselines. To (re)capture deliberately, run with the environment
/// variable <c>AKKA_GOLDEN_REGEN=1</c> and review the diff. On mismatch the actual output is
/// written next to the baseline as <c>*.received.txt</c> for diffing (Verify-style naming; the
/// comparison itself is a strict ordinal string equality, with no scrubbing or newline
/// normalization, because the gate exists to catch single-byte drift).
/// </remarks>
public sealed class GeneratorGoldenOutputSpec
{
    private const string RegenEnvVar = "AKKA_GOLDEN_REGEN";

    /// <summary>
    /// Main corpus (namespace <c>GoldenSample</c>): one big serializer covering every field-kind
    /// emission path, plus a second, internal serializer covering the 'internal' accessibility
    /// keyword path.
    /// </summary>
    private const string GoldenSource = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using Akka.Actor;
        using Akka.Serialization.V2;
        using MessagePack;

        namespace GoldenSample;

        public interface IProtocol
        {
        }

        // ---- hand-written formatters: one parameterless ctor (reference target), one
        // ExtendedActorSystem ctor (value-type target) ----

        public sealed record Foreign(string Value);

        public sealed class ForeignFormatter : IAkkaMessagePackFormatter<Foreign>
        {
            public void Write(ref MessagePackWriter writer, Foreign value) => writer.Write(value.Value);
            public Foreign Read(ref MessagePackReader reader) => new Foreign(reader.ReadString() ?? string.Empty);
            public int SizeOf(Foreign value) => Akka.Serialization.SerializerV2.UnknownSize;
        }

        public readonly record struct Temperature(double Celsius);

        public sealed class TemperatureFormatter : IAkkaMessagePackFormatter<Temperature>
        {
            public TemperatureFormatter(ExtendedActorSystem system)
            {
            }

            public void Write(ref MessagePackWriter writer, Temperature value) => writer.Write(value.Celsius);
            public Temperature Read(ref MessagePackReader reader) => new Temperature(reader.ReadDouble());
            public int SizeOf(Temperature value) => 9;
        }

        // ---- type-level union with class and struct members ----

        [AkkaUnion(typeof(OrderPlaced), typeof(OrderCancelled), typeof(OrderExpired))]
        public interface IOrderEvent
        {
        }

        [AkkaSerializable(Manifest = "order-placed-v1")]
        public sealed record OrderPlaced([property: AkkaField(1)] string OrderId) : IOrderEvent;

        [AkkaSerializable(Manifest = "order-cancelled-v1")]
        public sealed record OrderCancelled(
            [property: AkkaField(1)] string OrderId,
            [property: AkkaField(2)] string? Reason) : IOrderEvent;

        [AkkaSerializable(Manifest = "order-expired-v1")]
        public readonly record struct OrderExpired([property: AkkaField(1)] long ExpiredAtTicks) : IOrderEvent;

        // ---- nested (non-top-level) messages: reference and value shaped ----

        [AkkaSerializable]
        public sealed record Reading(
            [property: AkkaField(1)] string Sensor,
            [property: AkkaField(2)] double Value);

        [AkkaSerializable]
        public readonly record struct GeoPoint(
            [property: AkkaField(1)] double Lat,
            [property: AkkaField(2)] double Lon);

        // ---- generic definitions serialized via registered closed constructions ----

        [AkkaSerializable]
        public sealed record Wrapper<T>(
            [property: AkkaField(1)] string Id,
            [property: AkkaField(2)] T Payload) : IProtocol;

        [AkkaSerializable]
        public sealed record Pair<TA, TB>(
            [property: AkkaField(1)] TA First,
            [property: AkkaField(2)] TB Second);

        public enum Color
        {
            Red = 0,
            Green = 1,
            Blue = 2
        }

        public enum Priority : byte
        {
            Low = 0,
            High = 1
        }

        // ---- every scalar kind, nullable scalar variants, and keyword/adversarial names ----

        [AkkaSerializable(Manifest = "scalars-v1")]
        public sealed record ScalarMessage(
            [property: AkkaField(1)] string Name,
            [property: AkkaField(2)] byte[] Event,
            [property: AkkaField(3)] int Lock,
            [property: AkkaField(4)] long Params,
            [property: AkkaField(5)] bool FieldCount,
            [property: AkkaField(6)] double EntryIndex,
            [property: AkkaField(7)] decimal Amount,
            [property: AkkaField(8)] Guid Id,
            [property: AkkaField(9)] DateTime CreatedAt,
            [property: AkkaField(10)] DateTimeOffset UpdatedAt,
            [property: AkkaField(11)] IActorRef Sender,
            [property: AkkaField(12)] Color Color,
            [property: AkkaField(13)] Priority Priority,
            [property: AkkaField(14)] int? MaybeCount,
            [property: AkkaField(15)] Guid? MaybeId,
            [property: AkkaField(16)] Color? MaybeColor,
            [property: AkkaField(17)] string? MaybeName,
            [property: AkkaField(18)] string Foo,
            [property: AkkaField(19)] string HasFoo) : IProtocol;

        // ---- all ten collection shapes, nullable/nested/element-variant coverage ----

        [AkkaSerializable(Manifest = "collections-v1")]
        public sealed record CollectionMessage(
            [property: AkkaField(1)] int[] Codes,
            [property: AkkaField(2)] List<string> Names,
            [property: AkkaField(3)] IReadOnlyList<double> Samples,
            [property: AkkaField(4)] IReadOnlyCollection<Guid> Ids,
            [property: AkkaField(5)] Dictionary<string, string> Tags,
            [property: AkkaField(6)] IReadOnlyDictionary<int, Reading> ReadingsById,
            [property: AkkaField(7)] ImmutableArray<int> Points,
            [property: AkkaField(8)] ImmutableList<Reading> Readings,
            [property: AkkaField(9)] ImmutableHashSet<string> Labels,
            [property: AkkaField(10)] ImmutableDictionary<string, long> Counters,
            [property: AkkaField(11)] ImmutableArray<GeoPoint>? MaybeTrack,
            [property: AkkaField(12)] List<int>? MaybeNumbers,
            [property: AkkaField(13)] List<List<int>> Matrix,
            [property: AkkaField(14)] Dictionary<string, List<Reading>> History,
            [property: AkkaField(15)] List<Reading?> SparseReadings,
            [property: AkkaField(16)] List<GeoPoint> Track,
            [property: AkkaField(17)] List<GeoPoint?> SparseTrack,
            [property: AkkaField(18)] List<int?> SparseCodes,
            [property: AkkaField(19)] List<string?> SparseNames,
            [property: AkkaField(20)] int[][] Jagged,
            [property: AkkaField(21)] List<Color> Palette) : IProtocol;

        // ---- type-level union field (required and nullable) plus a field-level override that
        // forces a second helper for the same folded static type (numeric-suffix path) ----

        [AkkaSerializable(Manifest = "union-v1")]
        public sealed record UnionMessage(
            [property: AkkaField(1)] IOrderEvent Event,
            [property: AkkaField(2)] IOrderEvent? MaybeEvent,
            [property: AkkaField(3), AkkaUnion(typeof(OrderPlaced))] IOrderEvent Narrowed) : IProtocol;

        // ---- envelope payload (required and nullable) ----

        [AkkaSerializable(Manifest = "envelope-v1")]
        public sealed record EnvelopeMessage(
            [property: AkkaField(1)] string CorrelationId,
            [property: AkkaField(2), AkkaEnvelopePayload] object Payload,
            [property: AkkaField(3), AkkaEnvelopePayload] object? MaybePayload) : IProtocol;

        // ---- hybrid reconstruction: case-insensitive ctor matching, a keyword-named ctor
        // parameter, and leftover properties assigned via object initializer ----

        [AkkaSerializable(Manifest = "hybrid-v1")]
        public sealed class HybridMessage : IProtocol
        {
            public HybridMessage(string id, int count, string @event)
            {
                Id = id;
                Count = count;
                Event = @event;
            }

            [AkkaField(1)]
            public string Id { get; }

            [AkkaField(2)]
            public int Count { get; }

            [AkkaField(3)]
            public string Event { get; }

            [AkkaField(4)]
            public string? Note { get; init; }

            [AkkaField(5)]
            public GeoPoint Origin { get; set; }
        }

        // ---- nested object fields (reference/value, required/nullable), formatted fields
        // (reference/value, required/nullable), and a closed generic object field ----

        [AkkaSerializable(Manifest = "nested-v1")]
        public sealed record NestedMessage(
            [property: AkkaField(1)] Reading Primary,
            [property: AkkaField(2)] Reading? Secondary,
            [property: AkkaField(3)] GeoPoint Location,
            [property: AkkaField(4)] GeoPoint? MaybeLocation,
            [property: AkkaField(5)] Foreign Foreign,
            [property: AkkaField(6)] Foreign? MaybeForeign,
            [property: AkkaField(7)] Temperature Temperature,
            [property: AkkaField(8)] Temperature? MaybeTemperature,
            [property: AkkaField(9)] Wrapper<int> WrappedCount) : IProtocol;

        // ---- deliberately fieldless ----

        [AkkaSerializable(Manifest = "ping-v1", AllowEmpty = true)]
        public sealed record Ping : IProtocol;

        // ---- struct top-level message ----

        [AkkaSerializable(Manifest = "struct-msg-v1")]
        public readonly record struct StructMessage(
            [property: AkkaField(1)] int A,
            [property: AkkaField(2)] string B) : IProtocol;

        // ---- manifest requiring string escaping in emitted literals ----

        [AkkaSerializable(Manifest = "esc-\"quote\"-\\-v1")]
        public sealed record EscapedManifestMessage([property: AkkaField(1)] string Text) : IProtocol;

        [AkkaSerializer<IProtocol>("golden-sample", 150101)]
        [AkkaSerializerFormatter<Foreign, ForeignFormatter>]
        [AkkaSerializerFormatter<Temperature, TemperatureFormatter>]
        [AkkaSerializable<Wrapper<int>>(Manifest = "wrapper-int-v1")]
        [AkkaSerializable<Wrapper<GeoPoint>>(Manifest = "wrapper-geo-v1")]
        [AkkaSerializable<Wrapper<Pair<int, string>>>(Manifest = "wrapper-pair-v1")]
        [AkkaSerializable<Pair<int, string>>]
        public sealed partial class GoldenSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }

        // ---- second serializer: internal accessibility keyword path ----

        public interface IMiniProtocol
        {
        }

        [AkkaSerializable(Manifest = "mini-v1")]
        public sealed record MiniMessage([property: AkkaField(1)] string Text) : IMiniProtocol;

        [AkkaSerializer<IMiniProtocol>("golden-mini", 150102)]
        internal sealed partial class MiniSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    /// <summary>
    /// Second syntax tree, GLOBAL namespace: covers the namespace-less emission branch (no
    /// <c>namespace ...;</c> line in the generated file).
    /// </summary>
    private const string GlobalNamespaceSource = """
        #nullable enable
        using Akka.Serialization.V2;

        public interface IRootProtocol
        {
        }

        [AkkaSerializable(Manifest = "root-v1")]
        public sealed record RootMessage([property: AkkaField(1)] string Value) : IRootProtocol;

        [AkkaSerializer<IRootProtocol>("golden-root", 150103)]
        public sealed partial class RootSerializer : AkkaSerializer
        {
            public static partial SerializerRegistration CreateRegistration();
        }
        """;

    private static readonly string[] ExpectedHintNames =
    {
        "GoldenSerializer.AkkaSerialization.g.cs",
        "MiniSerializer.AkkaSerialization.g.cs",
        "RootSerializer.AkkaSerialization.g.cs"
    };

    [Fact(DisplayName = "Generator output should be byte-identical to the golden baselines")]
    public void Should_EmitByteIdenticalOutput_When_RunOverGoldenCorpus()
    {
        var runResult = RunGenerator(out _);

        runResult.Diagnostics.Should().BeEmpty("the golden corpus is designed to be diagnostics-clean");

        var generatedByHintName = runResult.GeneratedTrees
            .ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);
        generatedByHintName.Keys.Should().BeEquivalentTo(ExpectedHintNames);

        var goldenDirectory = GetGoldenDirectory();
        var regenerate = Environment.GetEnvironmentVariable(RegenEnvVar) == "1";
        var failures = new StringBuilder();

        foreach (var hintName in ExpectedHintNames)
        {
            var actual = generatedByHintName[hintName];
            var verifiedPath = Path.Combine(goldenDirectory, hintName + ".verified.txt");

            if (regenerate)
            {
                Directory.CreateDirectory(goldenDirectory);
                File.WriteAllText(verifiedPath, actual);
                continue;
            }

            if (!File.Exists(verifiedPath))
            {
                failures.AppendLine($"Missing baseline [{verifiedPath}]. Run once with {RegenEnvVar}=1 to capture it, then review and check it in.");
                continue;
            }

            var expected = File.ReadAllText(verifiedPath);
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                continue;

            var receivedPath = Path.Combine(goldenDirectory, hintName + ".received.txt");
            File.WriteAllText(receivedPath, actual);
            failures.AppendLine($"Generated output for [{hintName}] differs from baseline. {DescribeFirstDifference(expected, actual)} Actual output written to [{receivedPath}] for diffing against [{verifiedPath}].");
        }

        failures.Length.Should().Be(0, $"every generated file must match its golden baseline byte-for-byte:{Environment.NewLine}{failures}");
    }

    [Fact(DisplayName = "Golden corpus should generate cleanly and the generated output should compile without errors")]
    public void Should_CompileCleanly_When_GeneratedFromGoldenCorpus()
    {
        var runResult = RunGenerator(out var outputCompilation);

        runResult.Diagnostics.Should().BeEmpty("the golden corpus is designed to be diagnostics-clean");
        runResult.GeneratedTrees.Should().HaveCount(ExpectedHintNames.Length);

        var errors = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        errors.Should().BeEmpty("the corpus plus the generated serializers must form a valid compilation");
    }

    private static GeneratorDriverRunResult RunGenerator(out Compilation outputCompilation)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var compilation = CreateCompilation(
            CSharpSyntaxTree.ParseText(GoldenSource, parseOptions, path: "GoldenSample.cs"),
            CSharpSyntaxTree.ParseText(GlobalNamespaceSource, parseOptions, path: "GlobalSample.cs"));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new AkkaSerializerGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _);
        return driver.GetRunResult();
    }

    private static string DescribeFirstDifference(string expected, string actual)
    {
        var length = Math.Min(expected.Length, actual.Length);
        var index = 0;
        while (index < length && expected[index] == actual[index])
            index++;

        if (index == length && expected.Length == actual.Length)
            return "Contents differ but no differing index was found (unexpected).";

        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (expected.Length > i && expected[i] == '\n')
                line++;
        }

        string Snippet(string text) =>
            text.Substring(Math.Max(0, index - 40), Math.Min(80, text.Length - Math.Max(0, index - 40)))
                .Replace("\r", "\\r").Replace("\n", "\\n");

        return $"First difference at char index {index} (around line {line}): expected [...{Snippet(expected)}...] but got [...{Snippet(actual)}...].";
    }

    private static string GetGoldenDirectory([CallerFilePath] string sourceFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "GoldenOutput");
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        return CSharpCompilation.Create(
            "AkkaSerializationGeneratorGolden",
            trees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path)) ?? Enumerable.Empty<MetadataReference>();

        var explicitAssemblies = new[]
        {
            typeof(ActorSystem).Assembly,
            typeof(AkkaSerializerAttribute<>).Assembly,
            typeof(SerializerV2).Assembly,
            typeof(global::MessagePack.MessagePackWriter).Assembly,
            typeof(ImmutableHashSet<>).Assembly,
            Assembly.GetExecutingAssembly()
        };

        return trustedAssemblies.Concat(explicitAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)))
            .GroupBy(reference => reference.Display)
            .Select(group => group.First());
    }
}
