//-----------------------------------------------------------------------
// <copyright file="V2PortSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using VerifyXunit;
using Xunit;
using Xunit.Sdk;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit;

/// <summary>One message of a port's corpus and the manifest the legacy serializer gives it.</summary>
/// <param name="Message">A deterministic message (no clock, no random values), or the golden bytes drift.</param>
/// <param name="Manifest">The legacy manifest token. V2 reuses it unless <see cref="V2Manifest"/> explicitly differs.</param>
/// <param name="Name">Unique in the corpus. Names the snapshot files.</param>
public sealed record V2PortCase(object Message, string Manifest, string Name)
{
    /// <summary>
    /// Explicit V2 token for a new id whose legacy serializer had no manifest. Null preserves
    /// strict legacy-token parity; native V2 serializers still require a nonempty token.
    /// </summary>
    public string? V2Manifest { get; init; }

    /// <summary>
    /// Optional complete domain assertion for messages whose nested equality omits wire fields or
    /// compares collection identities. Receives expected then actual, after the exact runtime-type
    /// check. Null retains the shared equality/structural checks.
    /// </summary>
    public Action<object, object>? AssertEquivalent { get; init; }

    /// <summary>
    /// A generic shape intentionally requiring dynamic type loading. The off gate must reject
    /// both writes and reads with an explicit feature-switch reason, including warmed codecs.
    /// False retains the strict positive round-trip checks.
    /// </summary>
    public bool RequiresDynamicTypeLoading { get; init; }
}

/// <summary>
/// The spec a V2 serializer port derives from: a legacy serializer gets a V2 twin at id + 40 (inside 40-79) with
/// the same manifests unless an empty legacy token requires an explicit new native token. Supply the two serializers
/// and a corpus; the tests come with the base class. The golden checks
/// are Verify snapshots in a <c>Snapshots</c> folder next to the derived spec's source file (see the README).
/// Both serializers must be registered the way a deployed node has them (a module's table, or a setup), with no bindings on the V2 row.
/// </summary>
public abstract class V2PortSpec : AkkaSpec
{
    private const string DynamicTypeLoadingSwitch = "Akka.DynamicTypeLoading";
    private static readonly MethodInfo StructuralAssert = typeof(V2PortSpec).GetMethod(nameof(AssertStructurally),
        BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("Structural assertion method is missing.");

    private readonly string _sourceFile;

    /// <param name="output">Forwarded to <see cref="AkkaSpec"/> so actor system logs reach the test output.</param>
    /// <param name="config">The system's config.</param>
    /// <param name="sourceFile">Filled in by the compiler with the derived spec's source path. Leave it out.</param>
    protected V2PortSpec(ITestOutputHelper output, Config? config = null, [CallerFilePath] string sourceFile = "") : base(output, config)
    {
        _sourceFile = sourceFile;
    }

    /// <param name="setup">The system's setup, for ports that register serializers through a <see cref="SerializationSetup"/>.</param>
    /// <param name="output">Forwarded to <see cref="AkkaSpec"/>.</param>
    /// <param name="sourceFile">Filled in by the compiler with the derived spec's source path. Leave it out.</param>
    protected V2PortSpec(ActorSystemSetup setup, ITestOutputHelper output, [CallerFilePath] string sourceFile = "") : base(setup, output)
    {
        _sourceFile = sourceFile;
    }

    /// <summary>Builds the legacy serializer.</summary>
    protected abstract Serializer CreateLegacy(ExtendedActorSystem system);

    /// <summary>Builds the V2 serializer. Called many times; build a fresh one.</summary>
    protected abstract SerializerV2 CreateV2(ExtendedActorSystem system);

    /// <summary>The corpus: every manifest the legacy serializer writes, edge values included.</summary>
    protected abstract IReadOnlyList<V2PortCase> Cases { get; }

    /// <summary>Starts the snapshot file names: <c>Snapshots/{SnapshotPrefix}.{case}.legacy.verified.txt</c>. Defaults to the spec's type name.</summary>
    protected virtual string SnapshotPrefix => GetType().Name;

    private ExtendedActorSystem Node => (ExtendedActorSystem)Sys;

    [Fact(DisplayName = "Should_UseReservedIdBlock_When_PortIsDeclared")]
    public void Should_UseReservedIdBlock_When_PortIsDeclared()
    {
        var legacyId = CreateLegacy(Node).Identifier;

        var v2Id = CreateV2(Node).Identifier;

        v2Id.Should().Be(legacyId + 40);
        v2Id.Should().BeInRange(40, 79);
    }

    [Fact(DisplayName = "Should_ReuseLegacyManifests_When_V2SerializesCorpus")]
    public void Should_ReuseLegacyManifests_When_V2SerializesCorpus()
    {
        var legacy = CreateLegacy(Node);
        var v2 = CreateV2(Node);

        foreach (var c in Cases)
        {
            legacy.Manifest(c.Message).Should().Be(c.Manifest, "legacy manifest of {0}", c.Name);
            v2.Manifest(c.Message).Should().Be(c.V2Manifest ?? c.Manifest, "declared V2 manifest of {0}", c.Name);
        }
    }

    [Fact(DisplayName = "Should_RoundTripEveryManifest_When_V2Serializes")]
    public void Should_RoundTripEveryManifest_When_V2Serializes()
    {
        var v2 = CreateV2(Node);

        foreach (var c in Cases)
            AssertV2RoundTrip(v2, c);
    }

    [Fact(DisplayName = "Should_RoundTripEveryManifest_When_LegacySerializes")]
    public void Should_RoundTripEveryManifest_When_LegacySerializes()
    {
        var legacy = CreateLegacy(Node);

        foreach (var c in Cases)
            AssertSame(c, legacy.FromBinary(legacy.ToBinary(c.Message), c.Manifest), "legacy round trip");
    }

    [Fact(DisplayName = "Should_DecodeBothIds_When_NodeHoldsBothSerializers")]
    public void Should_DecodeBothIds_When_NodeHoldsBothSerializers()
    {
        var legacy = CreateLegacy(Node);
        var v2 = CreateV2(Node);

        foreach (var c in Cases)
        {
            AssertSame(c, Sys.Serialization.Deserialize(legacy.ToBinary(c.Message), legacy.Identifier, c.Manifest), "legacy id");
            AssertSame(c, Sys.Serialization.Deserialize(v2.ToBinary(c.Message), v2.Identifier, c.V2Manifest ?? c.Manifest), "V2 id");
        }
    }

    [Fact(DisplayName = "Should_KeepLegacyBinding_When_V2RowIsReadOnly")]
    public void Should_KeepLegacyBinding_When_V2RowIsReadOnly()
    {
        var legacyId = CreateLegacy(Node).Identifier;

        foreach (var c in Cases)
        {
            Sys.Serialization.FindSerializerFor(c.Message).Identifier.Should().Be(legacyId, "{0} stays bound to legacy", c.Name);
            Sys.Serialization.FindSerializerForType(c.Message.GetType()).Identifier.Should().Be(legacyId, "{0} stays bound to legacy", c.Name);
        }
    }

    [Fact(DisplayName = "Should_ResolveV2Id_When_DynamicTypeLoadingIsOff")]
    public void Should_ResolveV2Id_When_DynamicTypeLoadingIsOff()
        => AssertDynamicTypeLoadingGate(Cases, () => CreateV2(Node), () => new Akka.Serialization.Serialization(Node));

    internal static void AssertDynamicTypeLoadingGate(IReadOnlyList<V2PortCase> cases,
        Func<SerializerV2> createV2, Func<Akka.Serialization.Serialization> createRegistry)
    {
        // a process-wide switch: the spec class belongs in a collection that doesn't run beside others (see the README)
        var hadSwitch = AppContext.TryGetSwitch(DynamicTypeLoadingSwitch, out var previous);
        var encoded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        SerializerV2? warm = null;
        try
        {
            foreach (var sample in cases)
            {
                if (!sample.RequiresDynamicTypeLoading)
                    continue;
                AppContext.SetSwitch(DynamicTypeLoadingSwitch, true);
                warm ??= createV2();
                var bytes = warm.ToBinary(sample.Message);
                AssertSame(sample, warm.FromBinary(bytes, sample.V2Manifest ?? sample.Manifest), "warm enabled round trip");
                encoded.Add(sample.Name, bytes);
            }
            AppContext.SetSwitch(DynamicTypeLoadingSwitch, false);
            var serialization = createRegistry();
            var v2 = createV2();

            foreach (var c in cases)
            {
                if (c.RequiresDynamicTypeLoading)
                {
                    AssertDynamicTypeLoadingBoundary(c, warm!, v2, serialization, encoded[c.Name]);
                    continue;
                }
                AssertSame(c, serialization.Deserialize(v2.ToBinary(c.Message), v2.Identifier, c.V2Manifest ?? c.Manifest), "dynamic type loading off");
                AssertV2RoundTrip(v2, c);
            }
        }
        finally
        {
            AppContext.SetSwitch(DynamicTypeLoadingSwitch, !hadSwitch || previous); // unset means enabled
        }
    }

    private static void AssertDynamicTypeLoadingBoundary(V2PortCase sample, SerializerV2 warm, SerializerV2 cold,
        Akka.Serialization.Serialization registered, byte[] bytes)
    {
        var manifest = sample.V2Manifest ?? sample.Manifest;
        foreach (var codec in new[] { warm, cold })
        {
            RequireReason(() => codec.ToBinary(sample.Message));
            var writer = new ArrayBufferWriter<byte>();
            RequireReason(() => codec.Serialize(sample.Message, writer));
            Assert.Equal(0, writer.WrittenCount);
            RequireReason(() => codec.FromBinary(bytes, manifest));
            RequireReason(() => codec.Deserialize(new ReadOnlySequence<byte>(bytes), manifest));
        }
        RequireReason(() => registered.Deserialize(bytes, warm.Identifier, manifest));

        static void RequireReason(Action operation)
        {
            var error = Assert.Throws<SerializationException>(operation);
            Assert.Contains("Akka.DynamicTypeLoading", error.Message);
        }
    }

    [Fact(DisplayName = "Should_MatchGoldenV2Bytes_When_V2Serializes")]
    public Task Should_MatchGoldenV2Bytes_When_V2Serializes()
    {
        var v2 = CreateV2(Node);

        return VerifyAll("v2", v2.Identifier, c => v2.ToBinary(c.Message), c => c.V2Manifest ?? c.Manifest);
    }

    [Fact(DisplayName = "Should_MatchGoldenLegacyBytes_When_LegacySerializes")]
    public Task Should_MatchGoldenLegacyBytes_When_LegacySerializes()
    {
        var legacy = CreateLegacy(Node);

        return VerifyAll("legacy", legacy.Identifier, c => legacy.ToBinary(c.Message), c => c.Manifest);
    }

    /// <summary>
    /// Verifies one hex dump per case as <c>{prefix}.{case}.{kind}</c>. Every case runs before the test fails, so a first
    /// run writes all the <c>.received.txt</c> files at once.
    /// </summary>
    private async Task VerifyAll(string kind, int serializerId, Func<V2PortCase, byte[]> serialize,
        Func<V2PortCase, string> manifest)
    {
        var failures = new List<string>();

        foreach (var c in Cases)
        {
            var hexDump = HexDumpFormatter.Format(c.Name, HexDumpFormatter.FriendlyTypeName(c.Message.GetType()), manifest(c), serializerId, serialize(c));

            try
            {
                await Verifier.Verify(hexDump, sourceFile: _sourceFile)
                    .UseDirectory("Snapshots")
                    .UseFileName($"{SnapshotPrefix}.{c.Name}.{kind}");
            }
            catch (Exception e) // Verify's mismatch exception is internal
            {
                failures.Add($"{c.Name} ({kind}): {e.Message}");
            }
        }

        if (failures.Count > 0)
            throw new XunitException(string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    private static void AssertV2RoundTrip(SerializerV2 v2, V2PortCase c)
    {
        var bytes = v2.ToBinary(c.Message);
        var manifest = c.V2Manifest ?? c.Manifest;
        AssertSame(c, v2.FromBinary(bytes, manifest), "V2 FromBinary");
        AssertSame(c, v2.Deserialize(new ReadOnlySequence<byte>(bytes), manifest), "V2 Deserialize");

        var writer = new ArrayBufferWriter<byte>();
        v2.Serialize(c.Message, writer).Should().Be(writer.WrittenCount, "{0}: Serialize reports what it wrote", c.Name);
        writer.WrittenSpan.ToArray().Should().Equal(bytes, "{0}: Serialize and ToBinary write the same bytes", c.Name);

        var hint = v2.SizeHint(c.Message);
        if (hint != SerializerV2.UnknownSize)
            hint.Should().Be(bytes.Length, "{0}: SizeHint is exact", c.Name);
    }

    /// <summary>Asserts <paramref name="actual"/> equals the corpus message, by <c>Equals</c> or else structurally.</summary>
    internal static void AssertSame(V2PortCase c, object? actual, string stage)
    {
        if (actual is null || actual.GetType() != c.Message.GetType())
        {
            actual.Should().BeOfType(c.Message.GetType(), "{0}: {1}", stage, c.Name);
            return;
        }
        if (c.AssertEquivalent is { } assertEquivalent)
        {
            assertEquivalent(c.Message, actual!);
            return;
        }
        if (!Equals(c.Message, actual))
        {
            // FA 5 only exposes ComparingByMembers<T>. This JIT-only test dispatch changes the
            // root message strategy while preserving nested actor refs, CRDTs and byte equality.
            try
            {
                StructuralAssert.MakeGenericMethod(c.Message.GetType()).Invoke(null, [c, actual, stage]);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            }
        }
    }

    private static void AssertStructurally<T>(V2PortCase c, object? actual, string stage)
        => actual.Should().BeEquivalentTo(c.Message,
            o => o.RespectingRuntimeTypes().ComparingByMembers<T>(), "{0}: {1}", stage, c.Name);
}
