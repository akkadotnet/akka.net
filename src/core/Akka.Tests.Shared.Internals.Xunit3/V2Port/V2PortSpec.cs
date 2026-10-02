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
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using FluentAssertions;
using Xunit;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit;

/// <summary>One message of a port's corpus and the manifest the legacy serializer gives it.</summary>
/// <param name="Message">A deterministic message (no clock, no random values), or the golden bytes drift.</param>
/// <param name="Manifest">The legacy manifest token. The V2 serializer must reuse it.</param>
/// <param name="Name">Unique in the corpus. Names the golden file.</param>
public sealed record V2PortCase(object Message, string Manifest, string Name);

/// <summary>
/// The spec a V2 serializer port derives from: a legacy serializer gets a V2 twin at id + 40 (inside 40-79) with
/// the same manifests. Supply the two serializers, a corpus and a golden folder; the tests come with the base class.
/// Both serializers must be registered the way a deployed node has them (a module's table, or a setup), with no bindings on the V2 row.
/// </summary>
public abstract class V2PortSpec : AkkaSpec
{
    private const string DynamicTypeLoadingSwitch = "Akka.DynamicTypeLoading";

    /// <param name="output">Forwarded to <see cref="AkkaSpec"/> so actor system logs reach the test output.</param>
    /// <param name="config">The system's config.</param>
    protected V2PortSpec(ITestOutputHelper output, Config? config = null) : base(output, config)
    {
    }

    /// <param name="setup">The system's setup, for ports that register serializers through a <see cref="SerializationSetup"/>.</param>
    /// <param name="output">Forwarded to <see cref="AkkaSpec"/>.</param>
    protected V2PortSpec(ActorSystemSetup setup, ITestOutputHelper output) : base(setup, output)
    {
    }

    /// <summary>Builds the legacy serializer.</summary>
    protected abstract Serializer CreateLegacy(ExtendedActorSystem system);

    /// <summary>Builds the V2 serializer. Called many times; build a fresh one.</summary>
    protected abstract SerializerV2 CreateV2(ExtendedActorSystem system);

    /// <summary>The corpus: every manifest the legacy serializer writes, edge values included.</summary>
    protected abstract IReadOnlyList<V2PortCase> Cases { get; }

    /// <summary>The golden folder, <c>GoldenBytes.For("GoldenBytes/X")</c>. Files go in its <c>legacy</c> and <c>v2</c> sub-folders.</summary>
    protected abstract GoldenBytes Golden { get; }

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
            v2.Manifest(c.Message).Should().Be(c.Manifest, "V2 reuses the legacy manifest of {0}", c.Name);
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
            AssertSame(c, Sys.Serialization.Deserialize(v2.ToBinary(c.Message), v2.Identifier, c.Manifest), "V2 id");
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
    {
        // a process-wide switch: the spec class belongs in a collection that doesn't run beside others (see the README)
        var hadSwitch = AppContext.TryGetSwitch(DynamicTypeLoadingSwitch, out var previous);
        AppContext.SetSwitch(DynamicTypeLoadingSwitch, false);
        try
        {
            var serialization = new Akka.Serialization.Serialization(Node);
            var v2 = CreateV2(Node);

            foreach (var c in Cases)
            {
                AssertSame(c, serialization.Deserialize(v2.ToBinary(c.Message), v2.Identifier, c.Manifest), "dynamic type loading off");
                AssertV2RoundTrip(v2, c);
            }
        }
        finally
        {
            AppContext.SetSwitch(DynamicTypeLoadingSwitch, !hadSwitch || previous); // unset means enabled
        }
    }

    [Fact(DisplayName = "Should_MatchGoldenV2Bytes_When_V2Serializes")]
    public void Should_MatchGoldenV2Bytes_When_V2Serializes()
    {
        var v2 = CreateV2(Node);

        foreach (var c in Cases)
            Golden.Check("v2/" + c.Name, v2.ToBinary(c.Message));
    }

    [Fact(DisplayName = "Should_MatchGoldenLegacyBytes_When_LegacySerializes")]
    public void Should_MatchGoldenLegacyBytes_When_LegacySerializes()
    {
        var legacy = CreateLegacy(Node);

        foreach (var c in Cases)
            Golden.Check("legacy/" + c.Name, legacy.ToBinary(c.Message));
    }

    private static void AssertV2RoundTrip(SerializerV2 v2, V2PortCase c)
    {
        var bytes = v2.ToBinary(c.Message);
        AssertSame(c, v2.FromBinary(bytes, c.Manifest), "V2 FromBinary");
        AssertSame(c, v2.Deserialize(new ReadOnlySequence<byte>(bytes), c.Manifest), "V2 Deserialize");

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
        if (!Equals(c.Message, actual))
            actual.Should().BeEquivalentTo(c.Message, o => o.RespectingRuntimeTypes(), "{0}: {1}", stage, c.Name);
    }
}
