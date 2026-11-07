// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.DistributedData.Internal;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using VerifyXunit;
using Xunit;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Tests.Serialization;

public sealed class ReplicatedDataCanonicalSerializationSpec : AkkaSpec
{
    public ReplicatedDataCanonicalSerializationSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_RetainHistoricalNestedCrdtBytes_When_LegacyProtobufWrites")]
    [InlineData("gset")]
    [InlineData("orset")]
    [InlineData("register")]
    [InlineData("dictionary")]
    public async Task Should_RetainHistoricalNestedCrdtBytes_When_LegacyProtobufWrites(string name)
    {
        var node = ActorSystem.Create(CanonicalCrdtSetup.SystemName, CanonicalCrdtSetup.Create(false));
        InitializeLogger(node);
        try
        {
            var message = CanonicalCrdtSetup.Case(name);
            var serializer = node.Serialization.GetSerializerById(11);
            var manifest = serializer.Manifest(message);
            var bytes = serializer.ToBinary(message);
            CanonicalCrdtSetup.AssertEquivalent(message, serializer.FromBinary(bytes, manifest));
            await Verifier.Verify(HexDumpFormatter.Format(name, HexDumpFormatter.FriendlyTypeName(message.GetType()), manifest, 11, bytes))
                .UseDirectory("Snapshots").UseFileName($"{nameof(ReplicatedDataCanonicalSerializationSpec)}.{name}.legacy");
        }
        finally
        {
            await node.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory(DisplayName = "Should_KeepNestedProtobufBytes_When_FutureCrdtWriterIsSelected")]
    [InlineData("module")]
    [InlineData("typed")]
    [InlineData("legacy-alias")]
    public async Task Should_KeepNestedProtobufBytes_When_FutureCrdtWriterIsSelected(string binding)
    {
        var oldNode = ActorSystem.Create(CanonicalCrdtSetup.SystemName, CanonicalCrdtSetup.Create(false, binding));
        var newNode = ActorSystem.Create(CanonicalCrdtSetup.SystemName, CanonicalCrdtSetup.Create(true, binding));
        InitializeLogger(oldNode);
        InitializeLogger(newNode);
        try
        {
            var expectedInnerId = binding == "typed" ? 777 : binding == "legacy-alias" ? 778 : 11;
            var counter = CanonicalCrdtSetup.Counter(7);
            Assert.Equal(expectedInnerId, oldNode.Serialization.FindSerializerFor(counter).Identifier);
            Assert.Equal(binding == "typed" ? 777 : 51, newNode.Serialization.FindSerializerFor(counter).Identifier);
            Assert.Equal(expectedInnerId, new SerializationSupport((ExtendedActorSystem)newNode).OtherMessageToProto(counter).SerializerId);

            var oldLegacy = oldNode.Serialization.GetSerializerById(11);
            var newLegacy = newNode.Serialization.GetSerializerById(11);
            foreach (var name in new[] { "gset", "orset", "register", "dictionary" })
            {
                var message = CanonicalCrdtSetup.Case(name);
                Assert.Equal(51, newNode.Serialization.FindSerializerFor(message).Identifier);
                var historical = oldLegacy.ToBinary(message);
                var current = newLegacy.ToBinary(message);
                Assert.Equal(historical, current);
                CanonicalCrdtSetup.AssertEquivalent(message, oldLegacy.FromBinary(current, oldLegacy.Manifest(message)));
            }

            var durable = new DurableDataEnvelope(CanonicalCrdtSetup.Register(7));
            var oldStoreSerializer = oldNode.Serialization.GetSerializerForAlias("akka-data-replication");
            var newStoreSerializer = newNode.Serialization.GetSerializerForAlias("akka-data-replication");
            Assert.Equal(oldStoreSerializer.ToBinary(durable), newStoreSerializer.ToBinary(durable));

            // The future network codec keeps the selected CRDT serializer. The protobuf-only
            // canonical boundary must not change the owning actor system's write bindings.
            Assert.Equal(binding == "typed" ? 777 : 51, newNode.Serialization.FindSerializerFor(counter).Identifier);
        }
        finally
        {
            await Task.WhenAll(oldNode.Terminate(), newNode.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory(DisplayName = "Should_KeepDigestBytesAndStateSensitivity_When_FutureCrdtWriterIsSelected")]
    [InlineData("module")]
    [InlineData("typed")]
    [InlineData("legacy-alias")]
    public async Task Should_KeepDigestBytesAndStateSensitivity_When_FutureCrdtWriterIsSelected(string binding)
    {
        var oldNode = ActorSystem.Create(CanonicalCrdtSetup.SystemName, CanonicalCrdtSetup.Create(false, binding));
        var newNode = ActorSystem.Create(CanonicalCrdtSetup.SystemName, CanonicalCrdtSetup.Create(true, binding));
        InitializeLogger(oldNode);
        InitializeLogger(newNode);
        try
        {
            var state = new DataEnvelope(CanonicalCrdtSetup.Register(7));
            var changed = new DataEnvelope(CanonicalCrdtSetup.Register(8));
            Assert.Equal(51, newNode.Serialization.FindSerializerFor(state.Data).Identifier);
            Assert.Equal(52, newNode.Serialization.FindSerializerFor(state).Identifier);
            var oldDigestSerializer = Replicator.FindDigestSerializer(oldNode);
            var newDigestSerializer = Replicator.FindDigestSerializer(newNode);
            Assert.Equal(12, newDigestSerializer.Identifier);
            var historicalDigestBytes = oldDigestSerializer.ToBinary(state);
            var currentDigestBytes = newDigestSerializer.ToBinary(state);
            Assert.Equal(SHA1.HashData(historicalDigestBytes), SHA1.HashData(currentDigestBytes));
            Assert.Equal(historicalDigestBytes, currentDigestBytes);
            Assert.False(SHA1.HashData(currentDigestBytes).SequenceEqual(SHA1.HashData(newDigestSerializer.ToBinary(changed))));

            var network = newNode.Serialization.FindSerializerFor(state);
            var before = CanonicalCrdtSetup.FutureWrites(newNode);
            var bytes = network.ToBinary(state);
            Assert.True(CanonicalCrdtSetup.FutureWrites(newNode) > before);
            Assert.Equal(state, network.FromBinary(bytes, network.Manifest(state)));
        }
        finally
        {
            await Task.WhenAll(oldNode.Terminate(), newNode.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}

/// <summary>
/// Reserved-future-id stand-in, not a CRDT port. Setup precedence is independent of the global
/// toggle, so this fixture deliberately supplies its marker binding only for the on phase.
/// Both phases register its reader. Real module Supersedes coverage must replace this stand-in
/// when id 51 ships; its deliberately different bytes expose the nested protobuf regression now.
/// </summary>
internal static class CanonicalCrdtSetup
{
    internal const string SystemName = "crdt-canonical";
    private static readonly UniqueAddress Node = new(new Address("akka.tcp", SystemName, "counter.example", 2552), 42);

    internal static GCounter Counter(ulong value) => GCounter.Empty.Increment(Node, value);
    internal static LWWRegister<GCounter> Register(ulong value) => new(Node, Counter(value), 1234567);
    internal static int FutureWrites(ActorSystem system) => ((ReservedCrdtSerializer)system.Serialization.GetSerializerById(51)).Writes;

    internal static void AssertStoredCrdtIds(byte[] record, string binding)
    {
        var data = Akka.DistributedData.Serialization.Proto.Msg.DurableDataEnvelope.Parser.ParseFrom(record).Data;
        Assert.Equal(binding == "legacy-alias" ? 778 : 11, data.SerializerId);
        var registerBytes = data.EnclosedMessage.ToByteArray();
        if (binding == "legacy-alias")
        {
            var prefix = Encoding.UTF8.GetBytes("stand-in-778:");
            Assert.Equal(prefix, registerBytes.Take(prefix.Length));
            registerBytes = registerBytes.Skip(prefix.Length).ToArray();
        }
        var register = Akka.DistributedData.Serialization.Proto.Msg.LWWRegister.Parser.ParseFrom(registerBytes);
        Assert.Equal(binding == "typed" ? 777 : binding == "legacy-alias" ? 778 : 11, register.State.SerializerId);
    }

    internal static object Case(string name) => name switch
    {
        "gset" => GSet<GCounter>.Empty.Add(Counter(7)),
        "orset" => ORSet<GCounter>.Empty.Add(Node, Counter(7)),
        "register" => Register(7),
        "dictionary" => ORDictionary<string, GCounter>.Empty.SetItem(Node, "count", Counter(7)),
        _ => throw new ArgumentException("Unknown canonical CRDT case", nameof(name))
    };

    internal static void AssertEquivalent(object expected, object actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        if (expected is GSet<GCounter> set)
        {
            // GCounter's legacy dictionary-identity hash makes set equality unsuitable after
            // reconstruction. Compare the fixed singleton's complete per-node state instead.
            var result = Assert.IsType<GSet<GCounter>>(actual);
            Assert.Equal(set.Elements.Count, result.Elements.Count);
            Assert.Equal(Assert.Single(set.Elements).State, Assert.Single(result.Elements).State);
        }
        else
            Assert.True(expected.Equals(actual));
    }

    internal static ActorSystemSetup Create(bool enabled, string binding = "module", Config? extra = null)
    {
        var custom = binding == "typed" ? ConfigurationFactory.ParseString($$"""
            akka.actor.serializers.custom-counter = "{{typeof(TypedCounterSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings {
              "Akka.DistributedData.GCounter, Akka.DistributedData" = custom-counter
            }
            """) : binding == "legacy-alias" ? ConfigurationFactory.ParseString($$"""
            akka.actor.serializers.akka-replicated-data = "{{typeof(LegacyAliasSerializer).AssemblyQualifiedName}}"
            """) : Config.Empty;
        var config = ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(enabled ? "on" : "off")}")
            .WithFallback(extra ?? Config.Empty).WithFallback(custom)
            .WithFallback(ReplicatorMessagePackSerializerSpec.PortConfig);
        var setup = SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
            "reserved-crdt-v2-stand-in", new ReservedCrdtSerializer(system), enabled
                ? ImmutableHashSet.Create(typeof(IReplicatedDataSerialization)) : ImmutableHashSet<Type>.Empty)));
        return ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), setup);
    }

    private sealed class ReservedCrdtSerializer(ExtendedActorSystem system) : PrefixedCrdtSerializer(system, 51)
    {
    }

    public sealed class TypedCounterSerializer(ExtendedActorSystem system) : PrefixedCrdtSerializer(system, 777)
    {
    }

    public sealed class LegacyAliasSerializer(ExtendedActorSystem system) : PrefixedCrdtSerializer(system, 778)
    {
    }

    public abstract class PrefixedCrdtSerializer(ExtendedActorSystem system, int identifier) : SerializerWithStringManifest(system)
    {
        private readonly ReplicatedDataSerializer _legacy = new(system);
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes($"stand-in-{identifier}:");
        private int _writes;
        public int Writes => Volatile.Read(ref _writes);
        public override int Identifier => identifier;
        public override string Manifest(object o) => _legacy.Manifest(o);
        public override byte[] ToBinary(object obj)
        {
            Interlocked.Increment(ref _writes);
            return _prefix.Concat(_legacy.ToBinary(obj)).ToArray();
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (!bytes.Take(_prefix.Length).SequenceEqual(_prefix))
                throw new SerializationException("CRDT test serializer prefix is missing.");
            return _legacy.FromBinary(bytes.Skip(_prefix.Length).ToArray(), manifest);
        }
    }
}
