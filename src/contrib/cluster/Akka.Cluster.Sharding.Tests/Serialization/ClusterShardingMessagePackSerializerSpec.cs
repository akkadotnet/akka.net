// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Sharding.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Google.Protobuf;
using VerifyXunit;
using Xunit;

namespace Akka.Cluster.Sharding.Tests;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterShardingMessagePackSerializerSpec : V2PortSpec
{
    private readonly ITestOutputHelper _output;
    protected override IReadOnlyList<V2PortCase> Cases { get; }
    private static Config Settings => ConfigurationFactory.ParseString("""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
        akka.remote.dot-netty.tcp.port = 0
        """);

    public ClusterShardingMessagePackSerializerSpec(ITestOutputHelper output) : base(output, Settings)
    {
        _output = output;
        Cases = ShardingWireCases.Create(name => ((ExtendedActorSystem)Sys).Provider.ResolveActorRef(ShardingWireCases.Path(name)))
            .Select(item => new V2PortCase(item.Message, item.Manifest, item.Name)
            {
                AssertEquivalent = item.Message is ShardingEnvelope or ShardCoordinator.CoordinatorState or ClusterShardingStats or ShardRegionStats or CurrentShardRegionState
                    ? AssertDomainFields : null
            }).ToArray();
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ClusterShardingMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ClusterShardingMessagePackSerializer(system);

    [Fact(DisplayName = "Should_ReadBothFormats_When_GlobalToggleAndUserPinDiffer")]
    public async Task Should_ReadBothFormats_When_GlobalToggleAndUserPinDiffer()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(Settings);
        foreach (var legacy in new[] { Settings, MixedBindingPair.BindTo("akka-sharding", typeof(IClusterShardingSerializable)).WithFallback(enabled) })
        {
            await using var pair = MixedBindingPair.CreateWithSameSystemName("sharding-mixed", enabled, legacy);
            InitializeLogger(pair.V2Node);
            InitializeLogger(pair.LegacyNode);
            pair.AssertRoundTripsBothWays(Cases, 53, 13);
        }
    }

    [Theory(DisplayName = "Should_ReadHistoricalSingleEntityEvents_When_StoredManifestsUseOldTokens")]
    [InlineData(true, "CB", "LegacyEntityStarted")]
    [InlineData(false, "CD", "LegacyEntityStopped")]
    public async Task Should_ReadHistoricalSingleEntityEvents_When_StoredManifestsUseOldTokens(bool started, string manifest, string name)
    {
        var legacy = CreateLegacy((ExtendedActorSystem)Sys);
        var restored = legacy.FromBinary(ReadSnapshot(name), manifest);
        AssertSingleEntity(restored, started);

        var codec = new ClusterShardingMessagePackCodec((ExtendedActorSystem)Sys);
        var wire = started ? (IShardingWireMessage)new ShardingOldEntityStartedWire("e1") : new ShardingOldEntityStoppedWire("e1");
        var bytes = codec.ToBinary(wire);
        restored = Sys.Serialization.Deserialize(bytes, 53, manifest);
        AssertSingleEntity(restored, started);
        await VerifyAlias(name, restored.GetType(), manifest, bytes);
    }

    [Fact(DisplayName = "Should_RebuildRegionListsAndOmitRuntimeFlag_When_CoordinatorStateRoundTrips")]
    public void Should_RebuildRegionListsAndOmitRuntimeFlag_When_CoordinatorStateRoundTrips()
    {
        var node = (ExtendedActorSystem)Sys;
        var first = node.Provider.ResolveActorRef(ShardingWireCases.Path("first"));
        var idle = node.Provider.ResolveActorRef(ShardingWireCases.Path("idle"));
        var proxy = node.Provider.ResolveActorRef(ShardingWireCases.Path("proxy"));
        var state = new ShardCoordinator.CoordinatorState(
            ImmutableDictionary<string, IActorRef>.Empty.Add("s2", first).Add("s1", first),
            ImmutableDictionary<IActorRef, IImmutableList<string>>.Empty.Add(first, ImmutableList.Create("stale"))
                .Add(idle, ImmutableList<string>.Empty), ImmutableHashSet.Create(proxy), ImmutableHashSet.Create("waiting"), true);
        foreach (var serializer in new[] { CreateLegacy(node), CreateV2(node) })
        {
            var restored = (ShardCoordinator.CoordinatorState)serializer.FromBinary(serializer.ToBinary(state), "AA");
            restored.Shards.Should().BeEquivalentTo(state.Shards);
            restored.Regions.Keys.Should().BeEquivalentTo(state.Regions.Keys);
            restored.Regions[first].Should().BeEquivalentTo(new[] { "s1", "s2" });
            restored.Regions[idle].Should().BeEmpty();
            restored.RegionProxies.Should().BeEquivalentTo(state.RegionProxies);
            restored.UnallocatedShards.Should().BeEquivalentTo(state.UnallocatedShards);
            restored.RememberEntities.Should().BeFalse();
        }
    }

    [Fact(DisplayName = "Should_PreserveAllNestedStatistics_When_SeveralRegionsAndShardsRespond")]
    public void Should_PreserveAllNestedStatistics_When_SeveralRegionsAndShardsRespond()
    {
        var first = new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("s2", int.MaxValue).Add("s1", 0),
            ImmutableHashSet.Create("f1", "f2"));
        var second = new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("s3", 17), ImmutableHashSet<string>.Empty);
        var message = new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty
            .Add(ShardingWireCases.Owner, first).Add(new Address("akka", "wire", "node2", 2553), second));
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        AssertDomainFields(message, serializer.FromBinary(serializer.ToBinary(message), "CS"));
        var shards = new CurrentShardRegionState(ImmutableHashSet.Create(
            new ShardState("s1", ImmutableHashSet.Create("e1", "e2")), new ShardState("s2", ImmutableHashSet.Create("e3"))),
            ImmutableHashSet.Create("f1", "f2"));
        AssertDomainFields(shards, serializer.FromBinary(serializer.ToBinary(shards), "FE"));
    }

    [Fact(DisplayName = "Should_UseAmbientTransportAddress_When_ActorReferencesAreSerialized")]
    public async Task Should_UseAmbientTransportAddress_When_ActorReferencesAreSerialized()
    {
        var actor = Sys.ActorOf(Props.Empty, "local-region");
        var node = (ExtendedActorSystem)Sys;
        var before = Akka.Serialization.Serialization.CurrentTransportInformation;
        try
        {
            var path = actor.Path.ToSerializationFormatWithAddress(node.Provider.DefaultAddress);
            foreach (var serializer in new[] { CreateLegacy(node), CreateV2(node) })
            {
                var message = new ShardCoordinator.Register(actor);
                var bytes = Akka.Serialization.Serialization.WithTransport(node, () => serializer.ToBinary(message));
                Encoding.UTF8.GetString(bytes).Should().Contain(path);
                ((ShardCoordinator.Register)serializer.FromBinary(bytes, "BA")).ShardRegion.Should().BeSameAs(actor);
                if (serializer is SerializerV2 v2)
                {
                    var buffer = new ArrayBufferWriter<byte>();
                    Akka.Serialization.Serialization.WithTransport(node, () => v2.Serialize(message, buffer)).Should().Be(buffer.WrittenCount);
                    buffer.WrittenSpan.ToArray().Should().Equal(bytes);
                }
                Akka.Serialization.Serialization.CurrentTransportInformation.Should().BeSameAs(before);
            }
        }
        finally
        {
            await WatchAsync(actor);
            Sys.Stop(actor);
            await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(5));
        }
    }

    [Fact(DisplayName = "Should_MatchLegacyReferenceNormalization_When_ActorRefsAreNullOrSentinels")]
    public async Task Should_MatchLegacyReferenceNormalization_When_ActorRefsAreNullOrSentinels()
    {
        (Func<IActorRef?, object> Create, Func<object, IActorRef> Ref)[] forms =
        [
            (value => new ShardCoordinator.Register(value!), message => ((ShardCoordinator.Register)message).ShardRegion),
            (value => new ShardCoordinator.RegisterProxy(value!), message => ((ShardCoordinator.RegisterProxy)message).ShardRegionProxy),
            (value => new ShardCoordinator.RegisterAck(value!), message => ((ShardCoordinator.RegisterAck)message).Coordinator),
            (value => new ShardCoordinator.ShardRegionRegistered(value!), message => ((ShardCoordinator.ShardRegionRegistered)message).Region),
            (value => new ShardCoordinator.ShardRegionProxyRegistered(value!), message => ((ShardCoordinator.ShardRegionProxyRegistered)message).RegionProxy),
            (value => new ShardCoordinator.ShardRegionTerminated(value!), message => ((ShardCoordinator.ShardRegionTerminated)message).Region),
            (value => new ShardCoordinator.ShardRegionProxyTerminated(value!), message => ((ShardCoordinator.ShardRegionProxyTerminated)message).RegionProxy),
            (value => new ShardCoordinator.GracefulShutdownRequest(value!), message => ((ShardCoordinator.GracefulShutdownRequest)message).ShardRegion),
            (value => new ShardCoordinator.RegionStopped(value!), message => ((ShardCoordinator.RegionStopped)message).ShardRegion),
            (value => new ShardCoordinator.ShardHomeAllocated("s1", value!), message => ((ShardCoordinator.ShardHomeAllocated)message).Region),
            (value => new ShardCoordinator.ShardHome("s1", value!), message => ((ShardCoordinator.ShardHome)message).Ref)
        ];
        await using var local = new Akka.TestKit.Xunit.TestKit("akka.actor.provider = local", "sharding-local-refs", _output);
        var remote = ((ExtendedActorSystem)Sys).Provider.ResolveActorRef(ShardingWireCases.Path("region"));
        foreach (var receiver in new[] { Sys, local.Sys })
        {
            var node = (ExtendedActorSystem)receiver;
            var legacy = CreateLegacy(node);
            var native = CreateV2(node);
            foreach (var value in new[] { ActorRefs.NoSender, ActorRefs.Nobody, remote })
            foreach (var form in forms)
            {
                var message = form.Create(value);
                object? expected = null;
                object? actual = null;
                var expectedError = Record.Exception(() => expected = legacy.FromBinary(legacy.ToBinary(message), legacy.Manifest(message)));
                var actualError = Record.Exception(() => actual = native.FromBinary(native.ToBinary(message), native.Manifest(message)));
                if (expectedError is not null)
                {
                    actualError.Should().NotBeNull().And.BeOfType(expectedError.GetType());
                    continue;
                }
                actualError.Should().BeNull(message.GetType().Name);
                form.Ref(actual!).Should().Be(form.Ref(expected!), message.GetType().Name);
                if (value is null) form.Ref(actual!).Should().BeSameAs(node.Provider.ResolveActorRef(string.Empty));
            }

            var proto = new Akka.Cluster.Sharding.Serialization.Proto.Msg.CoordinatorState();
            proto.Regions.Add("");
            proto.RegionProxies.Add("");
            proto.Shards.Add(new Akka.Cluster.Sharding.Serialization.Proto.Msg.CoordinatorState.Types.ShardEntry
            {
                ShardId = "s1", RegionRef = ""
            });
            var codec = new ClusterShardingMessagePackCodec(node);
            var wire = new ShardingCoordinatorWire([new ShardingAllocationWire("s1", null)], [null], [null], []);
            object? oldState = null;
            object? newState = null;
            var oldError = Record.Exception(() => oldState = legacy.FromBinary(proto.ToByteArray(), "AA"));
            var newError = Record.Exception(() => newState = native.FromBinary(codec.ToBinary(wire), "AA"));
            if (oldError is not null) newError.Should().NotBeNull().And.BeOfType(oldError.GetType());
            else
            {
                newError.Should().BeNull();
                AssertDomainFields(oldState!, newState!);
            }
        }
    }

    [Fact(DisplayName = "Should_UseTheRegisteredPayloadSerializer_When_ShardingEnvelopeWrapsACustomMessage")]
    public async Task Should_UseTheRegisteredPayloadSerializer_When_ShardingEnvelopeWrapsACustomMessage()
    {
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.serialization-v2 = on
            akka.actor.serializers.custom-sharding = "{{typeof(PayloadSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings { "{{typeof(Payload).AssemblyQualifiedName}}" = custom-sharding }
            """);
        await using var kit = new Akka.TestKit.Xunit.TestKit(config, "sharding-custom-payload", _output);
        var payload = new Payload(17);
        var registered = (PayloadSerializer)kit.Sys.Serialization.FindSerializerFor(payload);
        var envelope = new ShardingEnvelope("e1", payload);
        var serializer = kit.Sys.Serialization.FindSerializerFor(envelope);
        serializer.Identifier.Should().Be(53);
        var restored = (ShardingEnvelope)serializer.FromBinary(serializer.ToBinary(envelope), "a");
        restored.EntityId.Should().Be("e1");
        restored.Message.Should().BeOfType<Payload>().Which.Value.Should().Be(17);
        registered.Writes.Should().BeGreaterThan(0);
        registered.Reads.Should().Be(1);
    }

    [Fact(DisplayName = "Should_PreserveBinaryPayload_When_ShardingEnvelopeUsesTheByteArraySerializer")]
    public void Should_PreserveBinaryPayload_When_ShardingEnvelopeUsesTheByteArraySerializer()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var payload = new byte[] { 0, 1, 127, 255 };
        var restored = (ShardingEnvelope)serializer.FromBinary(serializer.ToBinary(new ShardingEnvelope("e1", payload)), "a");
        restored.EntityId.Should().Be("e1");
        restored.Message.Should().BeOfType<byte[]>().Which.Should().Equal(payload);
    }

    [Fact(DisplayName = "Should_SkipFutureFields_When_AnotherWriterAddsANestedUnknownValue")]
    public void Should_SkipFutureFields_When_AnotherWriterAddsANestedUnknownValue()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        foreach (var item in Cases)
        {
            var bytes = serializer.ToBinary(item.Message);
            bytes[0].Should().BeInRange(0x80, 0x8e);
            var extended = new byte[bytes.Length + 3];
            bytes.CopyTo(extended, 0);
            extended[0]++;
            extended[^3] = 99;
            extended[^2] = 0x91;
            extended[^1] = 0xc0;
            var restored = serializer.FromBinary(extended, item.Manifest);
            restored.GetType().Should().Be(item.Message.GetType());
            if (item.AssertEquivalent is { } assertEquivalent) assertEquivalent(item.Message, restored);
            else if (!Equals(item.Message, restored)) restored.Should().BeEquivalentTo(item.Message, options => options.RespectingRuntimeTypes());
        }
    }

    [Fact(DisplayName = "Should_RejectUnknownInputs_When_ShardingCodecIsCalledDirectly")]
    public void Should_RejectUnknownInputs_When_ShardingCodecIsCalledDirectly()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Assert.Throws<ArgumentException>(() => serializer.Manifest(new object()));
        Assert.Throws<ArgumentException>(() => serializer.ToBinary(new object()));
        Assert.Throws<SerializationException>(() => serializer.FromBinary([], "unknown"));
    }

    private static void AssertSingleEntity(object actual, bool started)
    {
        if (started) actual.Should().BeOfType<EventSourcedRememberEntitiesShardStore.EntitiesStarted>().Which.Entities.Should().BeEquivalentTo(new[] { "e1" });
        else actual.Should().BeOfType<EventSourcedRememberEntitiesShardStore.EntitiesStopped>().Which.Entities.Should().BeEquivalentTo(new[] { "e1" });
    }

    private static void AssertDomainFields(object expected, object actual)
    {
        actual.GetType().Should().Be(expected.GetType());
        switch (expected)
        {
            case ShardingEnvelope envelope:
                var restoredEnvelope = (ShardingEnvelope)actual;
                restoredEnvelope.EntityId.Should().Be(envelope.EntityId);
                restoredEnvelope.Message.Should().Be(envelope.Message);
                break;
            case ShardCoordinator.CoordinatorState state:
                var restored = (ShardCoordinator.CoordinatorState)actual;
                restored.Shards.Should().BeEquivalentTo(state.Shards);
                restored.Regions.Keys.Should().BeEquivalentTo(state.Regions.Keys);
                foreach (var pair in state.Regions)
                    restored.Regions[pair.Key].Should().BeEquivalentTo(state.Shards.Where(shard => shard.Value.Equals(pair.Key)).Select(shard => shard.Key));
                restored.RegionProxies.Should().BeEquivalentTo(state.RegionProxies);
                restored.UnallocatedShards.Should().BeEquivalentTo(state.UnallocatedShards);
                restored.RememberEntities.Should().BeFalse();
                break;
            case ShardRegionStats stats:
                var other = (ShardRegionStats)actual;
                other.Stats.Should().BeEquivalentTo(stats.Stats);
                other.Failed.Should().BeEquivalentTo(stats.Failed);
                break;
            case ClusterShardingStats cluster:
                var c = (ClusterShardingStats)actual;
                c.Regions.Keys.Should().BeEquivalentTo(cluster.Regions.Keys);
                foreach (var pair in cluster.Regions) AssertDomainFields(pair.Value, c.Regions[pair.Key]);
                break;
            case CurrentShardRegionState region:
                var r = (CurrentShardRegionState)actual;
                r.Failed.Should().BeEquivalentTo(region.Failed);
                r.Shards.Count.Should().Be(region.Shards.Count);
                foreach (var shard in region.Shards)
                    r.Shards.Single(other => other.ShardId == shard.ShardId).EntityIds.Should().BeEquivalentTo(shard.EntityIds);
                break;
        }
    }

    private static byte[] ReadSnapshot(string name, [CallerFilePath] string file = "") => File.ReadLines(Path.Combine(Path.GetDirectoryName(file)!, "Snapshots",
            $"ClusterShardingMessagePackSerializerSpec.{name}.legacy.verified.txt"))
        .Where(line => line.Length > 6 && line[4] == ' ' && int.TryParse(line.AsSpan(0, 4), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out _))
        .SelectMany(line => line.Split('|')[0][6..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Select(hex => Convert.ToByte(hex, 16)).ToArray();

    private static async Task VerifyAlias(string name, Type type, string manifest, byte[] bytes, [CallerFilePath] string file = "")
    {
        await Verifier.Verify(HexDumpFormatter.Format(name, HexDumpFormatter.FriendlyTypeName(type), manifest, 53, bytes), sourceFile: file)
            .UseDirectory("Snapshots").UseFileName($"ClusterShardingMessagePackSerializerSpec.{name}.v2");
    }

    public sealed record Payload(int Value);
    public sealed class PayloadSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public override int Identifier => 777;
        public override string Manifest(object obj) => "payload";
        public override byte[] ToBinary(object obj)
        {
            Writes++;
            return BitConverter.GetBytes(((Payload)obj).Value);
        }
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Reads++;
            return new Payload(BitConverter.ToInt32(bytes));
        }
    }
}
