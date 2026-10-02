//-----------------------------------------------------------------------
// <copyright file="V2PortSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Xunit;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit;

/// <summary>
/// The spec a V2 serializer port derives from. It declares the port (<see cref="CreatePort"/>) and where its golden
/// files live; the base class supplies the tests, one per check in <see cref="V2PortSpecs"/>:
/// manifest parity, V2 and legacy round trips, both ids decoding on one node, the binding staying legacy, the V2 id
/// resolving with dynamic type loading off, and the golden V2 and legacy bytes.
/// </summary>
/// <example>
/// <code>
/// public sealed class XPortSpec : V2PortSpec
/// {
///     private static readonly GoldenBytes Legacy = GoldenBytes.For("GoldenBytes/X", GoldenKind.Legacy);
///     private static readonly GoldenBytes V2 = GoldenBytes.For("GoldenBytes/X", GoldenKind.V2);
///
///     public XPortSpec(ITestOutputHelper output) : base(output) { }
///
///     protected override GoldenBytes LegacyGolden => Legacy;
///     protected override GoldenBytes V2Golden => V2;
///
///     protected override V2Port CreatePort(ExtendedActorSystem system) => new V2Port(
///         s => new XSerializer(s), s => new XMessagePackSerializer(s),
///         new[] { new V2PortCase(new Ping(1), "P") });
/// }
/// </code>
/// </example>
public abstract class V2PortSpec : AkkaSpec
{
    private V2Port? _port;

    /// <param name="output">The test output, forwarded to <see cref="AkkaSpec"/> so actor system logs reach it.</param>
    /// <param name="config">
    /// The system's config. It has to register the legacy and V2 serializers the way a deployed node has them: in a
    /// module's table (nothing to add) or under <c>akka.actor.serializers</c>.
    /// </param>
    protected V2PortSpec(ITestOutputHelper output, Config? config = null) : base(output, config)
    {
    }

    /// <param name="setup">The system's setup, for ports registered through a <see cref="SerializationSetup"/>.</param>
    /// <param name="output">The test output.</param>
    protected V2PortSpec(ActorSystemSetup setup, ITestOutputHelper output) : base(setup, output)
    {
    }

    /// <summary>Builds the port, once <see cref="AkkaSpec.Sys"/> exists. Corpus messages may hold real actor refs from it.</summary>
    protected abstract V2Port CreatePort(ExtendedActorSystem system);

    /// <summary>The checked-in bytes the legacy serializer wrote before the port: <c>GoldenBytes.For(..., GoldenKind.Legacy)</c>.</summary>
    protected abstract GoldenBytes LegacyGolden { get; }

    /// <summary>The checked-in bytes the V2 serializer writes: <c>GoldenBytes.For(..., GoldenKind.V2)</c>.</summary>
    protected abstract GoldenBytes V2Golden { get; }

    /// <summary>The port, built on first use.</summary>
    protected V2Port Port => _port ??= CreatePort((ExtendedActorSystem)Sys);

    [Fact(DisplayName = "Should_CoverEveryManifest_When_CorpusIsDeclared")]
    public void Should_CoverEveryManifest_When_CorpusIsDeclared() => V2PortSpecs.AssertCorpus(Port);

    [Fact(DisplayName = "Should_UseReservedIdBlock_When_PortIsDeclared")]
    public void Should_UseReservedIdBlock_When_PortIsDeclared() => V2PortSpecs.AssertIds(Sys, Port);

    [Fact(DisplayName = "Should_ReuseLegacyManifests_When_V2SerializesCorpus")]
    public void Should_ReuseLegacyManifests_When_V2SerializesCorpus() => V2PortSpecs.AssertManifestParity(Sys, Port);

    [Fact(DisplayName = "Should_RoundTripEveryManifest_When_V2Serializes")]
    public void Should_RoundTripEveryManifest_When_V2Serializes() => V2PortSpecs.AssertV2RoundTrips(Sys, Port);

    [Fact(DisplayName = "Should_RoundTripEveryManifest_When_LegacySerializes")]
    public void Should_RoundTripEveryManifest_When_LegacySerializes() => V2PortSpecs.AssertLegacyRoundTrips(Sys, Port);

    [Fact(DisplayName = "Should_DecodeBothIds_When_NodeHoldsBothSerializers")]
    public void Should_DecodeBothIds_When_NodeHoldsBothSerializers() => V2PortSpecs.AssertBothIdsDecode(Sys, Port);

    [Fact(DisplayName = "Should_ResolveExpectedBinding_When_FindingSerializerForCorpus")]
    public void Should_ResolveExpectedBinding_When_FindingSerializerForCorpus() => V2PortSpecs.AssertBinding(Sys, Port);

    [Fact(DisplayName = "Should_ResolveV2Id_When_DynamicTypeLoadingIsOff")]
    public void Should_ResolveV2Id_When_DynamicTypeLoadingIsOff() => V2PortSpecs.AssertV2ResolvesWithDynamicTypeLoadingOff(Sys, Port);

    [Fact(DisplayName = "Should_MatchGoldenV2Bytes_When_V2Serializes")]
    public void Should_MatchGoldenV2Bytes_When_V2Serializes() => V2PortSpecs.AssertV2BytesMatchGolden(Sys, Port, V2Golden);

    [Fact(DisplayName = "Should_MatchGoldenLegacyBytes_When_LegacySerializes")]
    public void Should_MatchGoldenLegacyBytes_When_LegacySerializes()
        => V2PortSpecs.AssertLegacyBytesMatchGolden(
            Sys, Port.CreateLegacy((ExtendedActorSystem)Sys), Port.Cases, LegacyGolden, Port.CompareLegacyBytes);

    [Fact(DisplayName = "Should_DecodeGoldenLegacyBytes_When_V2CapableNodeReads")]
    public void Should_DecodeGoldenLegacyBytes_When_V2CapableNodeReads() => V2PortSpecs.AssertNodeDecodesLegacyGolden(Sys, Port, LegacyGolden);
}
