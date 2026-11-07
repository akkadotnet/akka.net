// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.Serialization;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterMessagePackSerializerSpec : ClusterV2PortSpec
{
    private readonly ITestOutputHelper _output;
    public ClusterMessagePackSerializerSpec(ITestOutputHelper output) : base(output, false)
    {
        _output = output;
    }

    protected override IReadOnlyList<V2PortCase> Cases { get; } = ClusterWireCases.Create()
        .Select(item => new V2PortCase(item.Message, item.Manifest, item.Name)
        {
            AssertEquivalent = item.Message is GossipEnvelope or InternalClusterAction.Welcome ? AssertGossipMessage : null
        }).ToArray();

    [Fact(DisplayName = "Should_PreserveEveryGossipField_When_MembershipHasAllStatesAndReachability")]
    public void Should_PreserveEveryGossipField_When_MembershipHasAllStatesAndReachability()
    {
        var members = new[] { MemberStatus.Joining, MemberStatus.Up, MemberStatus.WeaklyUp,
            MemberStatus.Leaving, MemberStatus.Exiting, MemberStatus.Down }
            .Select((status, index) => Member.Create(new UniqueAddress(new Address("akka", "cluster", "node" + index, 2552),
                    (1L << 40) + index), 7 + index, status,
                index == 0 ? ImmutableHashSet<string>.Empty : ImmutableHashSet.Create("backend", "role" + index),
                AppVersion.Create("1.2." + index))).ToImmutableSortedSet(Member.Ordering);
        var addresses = members.Select(member => member.UniqueAddress).ToArray();
        var records = ImmutableList.Create(
            new Reachability.Record(addresses[0], addresses[1], Reachability.ReachabilityStatus.Reachable, 11),
            new Reachability.Record(addresses[0], addresses[2], Reachability.ReachabilityStatus.Unreachable, 13),
            new Reachability.Record(addresses[1], addresses[3], Reachability.ReachabilityStatus.Terminated, 17));
        var reachability = new Reachability(records, ImmutableDictionary<UniqueAddress, long>.Empty
            .Add(addresses[0], 23).Add(addresses[1], 29).Add(addresses[2], 31));
        var clock = VectorClock.Create(ImmutableSortedDictionary<VectorClock.Node, long>.Empty
            .Add(VectorClock.Node.FromHash("hash-a"), long.MaxValue).Add(VectorClock.Node.FromHash("hash-b"), 3));
        var gossip = new Gossip(members, new GossipOverview(addresses.Take(2).ToImmutableHashSet(), reachability), clock,
            ImmutableDictionary<UniqueAddress, long>.Empty.Add(new UniqueAddress(addresses[1].Address, 91), 1234)
                .Add(new UniqueAddress(addresses[1].Address, 92), 5678));
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        foreach (var message in new object[] { new InternalClusterAction.Welcome(addresses[0], gossip),
                     new GossipEnvelope(addresses[0], addresses[1], gossip) })
        {
            var restored = serializer.FromBinary(serializer.ToBinary(message), serializer.Manifest(message));
            AssertGossip(gossip, restored is GossipEnvelope envelope ? envelope.Gossip : ((InternalClusterAction.Welcome)restored).Gossip);
        }
    }

    [Fact(DisplayName = "Should_PreserveGossipFields_When_ReadingEveryCapturedShape")]
    public void Should_PreserveGossipFields_When_ReadingEveryCapturedShape()
    {
        foreach (var serializer in new[] { CreateLegacy((ExtendedActorSystem)Sys), CreateV2((ExtendedActorSystem)Sys) })
        {
            foreach (var item in Cases)
            {
                var restored = serializer.FromBinary(serializer.ToBinary(item.Message), item.Manifest);
                if (item.Message is GossipEnvelope envelope)
                {
                    var other = (GossipEnvelope)restored;
                    other.From.Should().Be(envelope.From);
                    other.To.Should().Be(envelope.To);
                    other.Deadline.Should().BeNull();
                    AssertGossip(envelope.Gossip, other.Gossip);
                }
                if (item.Message is InternalClusterAction.Welcome welcome)
                {
                    ((InternalClusterAction.Welcome)restored).From.Should().Be(welcome.From);
                    AssertGossip(welcome.Gossip, ((InternalClusterAction.Welcome)restored).Gossip);
                }
            }
        }
    }

    [Fact(DisplayName = "Should_ReadBothFormats_When_GlobalToggleAndUserPinsDiffer")]
    public async Task Should_ReadBothFormats_When_GlobalToggleAndUserPinsDiffer()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(Settings(false));
        foreach (var legacyConfig in new[] { Settings(false),
                     MixedBindingPair.BindTo("akka-cluster", typeof(IClusterMessage), typeof(ClusterRouterPool)).WithFallback(enabled) })
        {
            await using var pair = MixedBindingPair.CreateWithSameSystemName("cluster-mixed", enabled, legacyConfig);
            InitializeLogger(pair.V2Node);
            InitializeLogger(pair.LegacyNode);
            pair.AssertRoundTripsBothWays(Cases, 45, 5);
        }
    }

    [Fact(DisplayName = "Should_SkipUnknownFields_When_AFutureWriterAddsAnEnvelopeField")]
    public void Should_SkipUnknownFields_When_AFutureWriterAddsAnEnvelopeField()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        foreach (var item in Cases)
        {
            var bytes = serializer.ToBinary(item.Message);
            bytes[0].Should().BeInRange(0x80, 0x8e);
            var extended = new byte[bytes.Length + 4];
            bytes.CopyTo(extended, 0);
            extended[0]++;
            extended[^4] = 99;
            extended[^3] = 0x81;
            extended[^2] = 1;
            extended[^1] = 0xc0;
            var restored = serializer.FromBinary(extended, item.Manifest);
            if (item.Message is GossipEnvelope envelope) AssertGossip(envelope.Gossip, ((GossipEnvelope)restored).Gossip);
            else if (item.Message is InternalClusterAction.Welcome welcome) AssertGossip(welcome.Gossip, ((InternalClusterAction.Welcome)restored).Gossip);
            else if (!Equals(restored, item.Message)) restored.Should().BeEquivalentTo(item.Message, options => options.RespectingRuntimeTypes());
        }
    }

    [Fact(DisplayName = "Should_UseRegisteredPoolSerializer_When_ClusterRouterContainsALocalPool")]
    public async Task Should_UseRegisteredPoolSerializer_When_ClusterRouterContainsALocalPool()
    {
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.serialization-v2 = on
            akka.actor.serializers.custom-pool = "{{typeof(PoolSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings { "{{typeof(RoundRobinPool).AssemblyQualifiedName}}" = custom-pool }
            """).WithFallback(Settings(false));
        await using var kit = new Akka.TestKit.Xunit.TestKit(config, "cluster-custom-pool", _output);
        var pool = new RoundRobinPool(17);
        var registered = (PoolSerializer)kit.Sys.Serialization.FindSerializerFor(pool);
        var message = new ClusterRouterPool(pool, new ClusterRouterPoolSettings(23, 3, true, "backend"));
        var serializer = kit.Sys.Serialization.FindSerializerFor(message);
        serializer.Identifier.Should().Be(45);
        var restored = (ClusterRouterPool)serializer.FromBinary(serializer.ToBinary(message), serializer.Manifest(message));
        restored.Local.Should().BeOfType<RoundRobinPool>().Which.NrOfInstances.Should().Be(17);
        restored.Settings.Should().Be(message.Settings);
        registered.Writes.Should().BeGreaterThan(0);
        registered.Reads.Should().Be(1);
    }

    [Fact(DisplayName = "Should_PreserveNumericStatusValues_When_LegacyAcceptsUnknownEnums")]
    public void Should_PreserveNumericStatusValues_When_LegacyAcceptsUnknownEnums()
    {
        var original = ClusterWireCases.CreateGossip();
        var members = original.Members.Select(member => Member.Create(member.UniqueAddress, member.UpNumber,
            (MemberStatus)999, member.Roles, member.AppVersion)).ToImmutableSortedSet(Member.Ordering);
        var reachability = new Reachability(original.Overview.Reachability.Records.Select(record =>
            new Reachability.Record(record.Observer, record.Subject, (Reachability.ReachabilityStatus)777, record.Version))
            .ToImmutableList(), original.Overview.Reachability.Versions);
        var gossip = new Gossip(members, original.Overview.Copy(reachability: reachability), original.Version, original.Tombstones);
        var message = new GossipEnvelope(ClusterWireCases.First, ClusterWireCases.Second, gossip);
        foreach (var serializer in new[] { CreateLegacy((ExtendedActorSystem)Sys), CreateV2((ExtendedActorSystem)Sys) })
        {
            var restored = (GossipEnvelope)serializer.FromBinary(serializer.ToBinary(message), serializer.Manifest(message));
            AssertGossip(gossip, restored.Gossip);
            restored.Gossip.Members.Should().OnlyContain(member => (int)member.Status == 999);
            restored.Gossip.Overview.Reachability.Records.Should().OnlyContain(record => (int)record.Status == 777);
        }
    }

    [Fact(DisplayName = "Should_NormalizeAbsentPorts_When_AnAddressUsesZeroOrNull")]
    public void Should_NormalizeAbsentPorts_When_AnAddressUsesZeroOrNull()
    {
        foreach (var port in new int?[] { 0, null })
        foreach (var serializer in new[] { CreateLegacy((ExtendedActorSystem)Sys), CreateV2((ExtendedActorSystem)Sys) })
        {
            var message = new ClusterUserAction.Leave(new Address("akka", "cluster", "node", port));
            var restored = (ClusterUserAction.Leave)serializer.FromBinary(serializer.ToBinary(message), serializer.Manifest(message));
            restored.Address.Protocol.Should().Be("akka");
            restored.Address.System.Should().Be("cluster");
            restored.Address.Host.Should().Be("node");
            restored.Address.Port.Should().BeNull();
        }
    }

    [Fact(DisplayName = "Should_RejectUnsupportedInputs_When_ClusterCodecIsCalledDirectly")]
    public void Should_RejectUnsupportedInputs_When_ClusterCodecIsCalledDirectly()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Assert.Throws<ArgumentException>(() => serializer.Manifest(new object()));
        Assert.Throws<ArgumentException>(() => serializer.ToBinary(new object()));
        Assert.Throws<SerializationException>(() => serializer.FromBinary([], "unknown"));
    }

    private static void AssertGossipMessage(object expected, object actual)
    {
        if (expected is GossipEnvelope envelope)
        {
            var restored = (GossipEnvelope)actual;
            restored.From.Should().Be(envelope.From);
            restored.To.Should().Be(envelope.To);
            restored.Deadline.Should().BeNull();
            AssertGossip(envelope.Gossip, restored.Gossip);
        }
        else
        {
            var welcome = (InternalClusterAction.Welcome)expected;
            var restored = (InternalClusterAction.Welcome)actual;
            restored.From.Should().Be(welcome.From);
            AssertGossip(welcome.Gossip, restored.Gossip);
        }
    }

    internal static void AssertGossip(Gossip expected, Gossip actual)
    {
        actual.Members.Count.Should().Be(expected.Members.Count);
        foreach (var member in expected.Members)
        {
            var restored = actual.Members.Single(other => other.UniqueAddress.Equals(member.UniqueAddress));
            restored.UniqueAddress.Uid.Should().Be(member.UniqueAddress.Uid);
            restored.UpNumber.Should().Be(member.UpNumber);
            restored.Status.Should().Be(member.Status);
            restored.Roles.Should().BeEquivalentTo(member.Roles);
            restored.AppVersion.Version.Should().Be(member.AppVersion.Version);
        }
        actual.Overview.Seen.Should().BeEquivalentTo(expected.Overview.Seen);
        actual.Overview.Reachability.Versions.Should().BeEquivalentTo(expected.Overview.Reachability.Versions);
        actual.Overview.Reachability.Records.Select(record => (record.Observer, record.Subject, record.Status, record.Version))
            .Should().BeEquivalentTo(expected.Overview.Reachability.Records.Select(record => (record.Observer, record.Subject, record.Status, record.Version)));
        actual.Version.Versions.Should().Equal(expected.Version.Versions);
        actual.Tombstones.Should().BeEquivalentTo(expected.Tombstones);
    }

    public sealed class PoolSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public override int Identifier => 777;
        public override string Manifest(object obj) => "pool";
        public override byte[] ToBinary(object obj)
        {
            Writes++;
            return BitConverter.GetBytes(((RoundRobinPool)obj).NrOfInstances);
        }
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Reads++;
            return new RoundRobinPool(BitConverter.ToInt32(bytes));
        }
    }
}

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterLegacyHeartbeatV2SerializerSpec : ClusterV2PortSpec
{
    public ClusterLegacyHeartbeatV2SerializerSpec(ITestOutputHelper output) : base(output, true)
    {
    }
    protected override IReadOnlyList<V2PortCase> Cases { get; } = ClusterWireCases.Create(true)
        .Where(item => item.Name is "Heartbeat" or "HeartbeatRsp")
        .Select(item => new V2PortCase(item.Message, item.Manifest, item.Name)).ToArray();

    [Fact(DisplayName = "Should_DropSequenceAndTime_When_LegacyHeartbeatModeIsEnabled")]
    public void Should_DropSequenceAndTime_When_LegacyHeartbeatModeIsEnabled()
    {
        foreach (var serializer in new[] { CreateLegacy((ExtendedActorSystem)Sys), CreateV2((ExtendedActorSystem)Sys) })
        {
            var heartbeat = new ClusterHeartbeatSender.Heartbeat(ClusterWireCases.First.Address, 17, 1234);
            var restored = (ClusterHeartbeatSender.Heartbeat)serializer.FromBinary(serializer.ToBinary(heartbeat), serializer.Manifest(heartbeat));
            serializer.Manifest(heartbeat).Should().Be(ClusterMessageSerializer.HeartBeatManifestPre1419);
            restored.SequenceNr.Should().Be(-1);
            restored.CreationTimeNanos.Should().Be(-1);
            var response = new ClusterHeartbeatSender.HeartbeatRsp(ClusterWireCases.First, 19, 5678);
            var restoredResponse = (ClusterHeartbeatSender.HeartbeatRsp)serializer.FromBinary(serializer.ToBinary(response), serializer.Manifest(response));
            serializer.Manifest(response).Should().Be(ClusterMessageSerializer.HeartBeatRspManifestPre1419);
            restoredResponse.SequenceNr.Should().Be(-1);
            restoredResponse.CreationTimeNanos.Should().Be(-1);
        }
    }
}

public abstract class ClusterV2PortSpec : V2PortSpec
{
    protected ClusterV2PortSpec(ITestOutputHelper output, bool legacyHeartbeat,
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") : base(output, Settings(legacyHeartbeat), sourceFile)
    {
    }
    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ClusterMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ClusterMessagePackSerializer(system);
    protected static Config Settings(bool legacyHeartbeat) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
        akka.remote.dot-netty.tcp.port = 0
        akka.cluster.use-legacy-heartbeat-message = {{(legacyHeartbeat ? "on" : "off")}}
        """);
}
