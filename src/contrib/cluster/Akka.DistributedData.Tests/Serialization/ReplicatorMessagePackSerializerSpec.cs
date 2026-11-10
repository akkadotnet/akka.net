// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.Internal;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Google.Protobuf;
using Xunit;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Tests.Serialization;

/// <summary>
/// All legacy manifests, including optional fields and pruning/delta metadata. The shared port
/// kit also round-trips the entire corpus with dynamic type loading disabled on the JIT.
/// Nested CRDTs remain on id 11; those JIT checks do not establish their generic Native AOT support.
/// </summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ReplicatorMessagePackSerializerSpec : V2PortSpec
{
    // Legacy protocol maps enumerate hash buckets. A deterministic fixture comparer keeps their
    // original writer bytes reproducible across processes without changing the legacy serializer.
    private static readonly IEqualityComparer<string> StableKeys = new StableKeyComparer();

    internal static readonly Config PortConfig = ConfigurationFactory.ParseString("""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.port = 0
        """).WithFallback(DistributedData.DefaultConfig());

    public ReplicatorMessagePackSerializerSpec(ITestOutputHelper output) : base(output, PortConfig)
    {
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ReplicatorMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ReplicatorMessagePackSerializer(system);
    protected override IReadOnlyList<V2PortCase> Cases => Corpus(Sys.Name, ((ExtendedActorSystem)Sys).Provider);

    [Fact(DisplayName = "Should_ExchangeEveryManifest_When_NodesUseOpposingBindingsAndTheSameSystemName")]
    public async Task Should_ExchangeEveryManifest_When_NodesUseOpposingBindingsAndTheSameSystemName()
    {
        const string name = "ddata-mixed";
        var v2 = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(PortConfig);
        var legacy = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on")
            .WithFallback(MixedBindingPair.BindTo("akka-data-replication", typeof(IReplicatorMessage)))
            .WithFallback(PortConfig);
        await using var pair = MixedBindingPair.CreateWithSameSystemName(name, v2, legacy);
        pair.AssertRoundTripsBothWays(Corpus(name, ((ExtendedActorSystem)pair.V2Node).Provider), 52, 12);
    }

    internal static IReadOnlyList<V2PortCase> Corpus(string systemName, IActorRefProvider provider)
    {
        var first = Node(systemName, "first.example", 1L << 40);
        var second = Node(systemName, "second.example", long.MinValue + 7);
        var third = Node(systemName, "third.example", 3);
        var key = new GCounterKey("counter");
        var counter = GCounter.Empty.Increment(first, 17).Increment(second, 2);
        var envelope = new DataEnvelope(counter);
        var pruning = ImmutableDictionary<UniqueAddress, IPruningState>.Empty
            .Add(first, new PruningPerformed(new DateTime(123456)))
            .Add(third, new PruningInitialized(second, first.Address));
        var metadataEnvelope = new DataEnvelope(counter, pruning,
            VersionVector.Create(ImmutableDictionary<UniqueAddress, long>.Empty.Add(first, 4).Add(second, 2)));
        var subscriber = provider.ResolveActorRef("akka.tcp://subscriber-system@subscriber.example:2552/user/subscriber#17");
        return new V2PortCase[]
        {
            new(new Get(key, ReadLocal.Instance), "A", "get-local"),
            new(new Get(key, new ReadFrom(2, TimeSpan.FromSeconds(2)), "ctx"), "A", "get-from"),
            new(new Get(key, new ReadAll(TimeSpan.FromSeconds(2))), "A", "get-all"),
            new(new Get(key, new ReadMajority(TimeSpan.FromSeconds(2), 3)), "A", "get-majority"),
            new(new Get(key, new ReadMajorityPlus(TimeSpan.FromSeconds(2), 2, 3), "ctx"), "A", "get-majority-plus"),
            new(new GetSuccess(key, null, counter), "B", "get-success"),
            new(new GetSuccess(key, "ctx", counter), "B", "get-success-request"),
            new(new NotFound(key, "ctx"), "C", "not-found"),
            new(new GetFailure(key, null), "D", "get-failure"),
            new(new Subscribe(key, subscriber), "E", "subscribe"),
            new(new Unsubscribe(key, subscriber), "F", "unsubscribe"),
            new(new Changed(key, counter), "G", "changed"),
            new(envelope, "H", "data-envelope"),
            new(metadataEnvelope, "H", "data-envelope-metadata"),
            new(new Write("counter", envelope, first), "I", "write"),
            new(new Write("counter", envelope, null), "I", "write-no-node"),
            new(WriteAck.Instance, "J", "write-ack"),
            new(new Read("counter", first), "K", "read"),
            new(new Read("counter", null), "K", "read-no-node"),
            new(new ReadResult(envelope), "L", "read-result"),
            new(new ReadResult(null), "L", "read-result-empty"),
            new(new Internal.Status(ImmutableDictionary<string, ByteString>.Empty.WithComparers(StableKeys)
                .Add("a", ByteString.CopyFromUtf8("first"))
                .Add("b", ByteString.CopyFromUtf8("second")), 3, 10, 17, 19), "M", "status"),
            new(new Internal.Status(ImmutableDictionary<string, ByteString>.Empty.WithComparers(StableKeys), 0, 1, null, null), "M", "status-empty"),
            new(new Gossip(ImmutableDictionary<string, DataEnvelope>.Empty.WithComparers(StableKeys)
                .Add("a", envelope).Add("b", metadataEnvelope), true, 17, 19), "N", "gossip"),
            new(new Gossip(ImmutableDictionary<string, DataEnvelope>.Empty.WithComparers(StableKeys), false, null, 19), "N", "gossip-empty"),
            new(WriteNack.Instance, "O", "write-nack"),
            new(new DurableDataEnvelope(counter), "P", "durable-envelope"),
            new(new DeltaPropagation(first, true, ImmutableDictionary<string, Delta>.Empty.WithComparers(StableKeys)
                .Add("a", new Delta(envelope, 1, 1)).Add("b", new Delta(metadataEnvelope, 3, 5))), "Q", "delta-propagation"),
            new(DeltaNack.Instance, "R", "delta-nack")
        };
    }

    [Fact(DisplayName = "Should_KeepGossipCompressed_When_UsingV2")]
    public void Should_KeepGossipCompressed_When_UsingV2()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var gossip = Cases.Single(c => c.Name == "gossip");
        var bytes = serializer.ToBinary(gossip.Message);
        Assert.Equal(0x1f, bytes[0]);
        Assert.Equal(0x8b, bytes[1]);
        using var compressed = new MemoryStream(bytes);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var payload = new MemoryStream();
        gzip.CopyTo(payload);
        Assert.NotEmpty(payload.ToArray());
        serializer.FromBinary(bytes, gossip.Manifest).Should().BeEquivalentTo(gossip.Message,
            options => options.RespectingRuntimeTypes().ComparingByMembers<Gossip>());
    }

    [Fact(DisplayName = "Should_ProduceTheSameDigest_When_UnorderedEnvelopeMetadataHasDifferentInsertionOrder")]
    public void Should_ProduceTheSameDigest_When_UnorderedEnvelopeMetadataHasDifferentInsertionOrder()
    {
        var first = Node(Sys.Name, "first.example", 1L << 40);
        var second = Node(Sys.Name, "second.example", long.MinValue + 7);
        var removed = Node(Sys.Name, "removed.example", 3);
        var counter = GCounter.Empty.Increment(first, 2).Increment(second, 5);
        var seen = new[] { first.Address, second.Address };
        var a = new DataEnvelope(counter,
            ImmutableDictionary<UniqueAddress, IPruningState>.Empty
                .Add(first, new PruningPerformed(new DateTime(123456)))
                .Add(removed, new PruningInitialized(second, seen)),
            VersionVector.Create(ImmutableDictionary<UniqueAddress, long>.Empty.Add(first, 2).Add(second, 4)));
        var b = new DataEnvelope(counter,
            ImmutableDictionary<UniqueAddress, IPruningState>.Empty
                .Add(removed, new PruningInitialized(second, seen.Reverse().ToArray()))
                .Add(first, new PruningPerformed(new DateTime(123456))),
            VersionVector.Create(ImmutableDictionary<UniqueAddress, long>.Empty.Add(second, 4).Add(first, 2)));
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Assert.Equal(serializer.ToBinary(a), serializer.ToBinary(b));
        Assert.Equal(SHA1.HashData(serializer.ToBinary(a)), SHA1.HashData(serializer.ToBinary(b)));
    }

    [Fact(DisplayName = "Should_KeepOnlyPerformedPruning_When_DurableEnvelopeRoundTrips")]
    public void Should_KeepOnlyPerformedPruning_When_DurableEnvelopeRoundTrips()
    {
        var first = Node(Sys.Name, "first.example", 1);
        var second = Node(Sys.Name, "second.example", 2);
        var envelope = new DataEnvelope(GCounter.Empty.Increment(first, 7),
            ImmutableDictionary<UniqueAddress, IPruningState>.Empty
                .Add(first, new PruningPerformed(new DateTime(123456)))
                .Add(second, new PruningInitialized(first, second.Address)),
            VersionVector.Create(first, 2));
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var result = Assert.IsType<DurableDataEnvelope>(serializer.FromBinary(
            serializer.ToBinary(new DurableDataEnvelope(envelope)), "P"));
        Assert.Equal(envelope.Data, result.Data);
        Assert.Single(result.DataEnvelope.Pruning);
        Assert.IsType<PruningPerformed>(result.DataEnvelope.Pruning[first]);
        Assert.True(result.DataEnvelope.DeltaVersions.IsEmpty);
    }

    [Fact(DisplayName = "Should_PreserveTheOriginalWriter_When_GlobalV2IsOff")]
    public void Should_PreserveTheOriginalWriter_When_GlobalV2IsOff()
    {
        foreach (var sample in Cases)
            Assert.Equal(12, Sys.Serialization.FindSerializerFor(sample.Message).Identifier);
        Assert.Equal(11, Sys.Serialization.FindSerializerFor(GCounter.Empty).Identifier);
    }

    internal static UniqueAddress Node(string systemName, string host, long uid)
        => new(new Address("akka.tcp", systemName, host, 2552), uid);

    private sealed class StableKeyComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

        public int GetHashCode(string value)
        {
            var hash = 17;
            foreach (var character in value)
                hash = unchecked(hash * 31 + character);
            return hash;
        }
    }
}
