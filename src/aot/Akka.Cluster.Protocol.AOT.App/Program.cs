// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Cluster.Routing;
using Akka.Cluster.Serialization;
using Akka.Configuration;
using Akka.Event;
using Akka.Routing;
using Akka.Serialization;
using Akka.Serialization.V2;
using Google.Protobuf;
using MessagePack;

Require(!RuntimeFeature.IsDynamicCodeSupported, "Actual native execution is required.");
Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
    "Dynamic loading must be disabled.");
Require(FrozenFixture.All.Length == 20, "Incomplete frozen corpus.");
var cases = 0;
var buffers = 0;
var selections = 0;
var legacyWrites = 0;
var heartbeatWires = 0;
var rejected = 0;
foreach (var historical in new[] { false, true })
foreach (var mode in new[] { (Name: "off", Id: 5), (Name: "on", Id: 45), (Name: "pinned", Id: 5) })
{
    var watchdog = new LogWatchdogFilter();
    var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(Settings(mode.Name, historical)),
        new LogFilterSetup([watchdog]));
    var system = ActorSystem.Create("cluster-public-native", setup);
    try
    {
        // Ordinary public constructor anchors the module. Every private domain instance is
        // constructed by public id dispatch over an unchanged frozen protobuf record.
        var legacy = new ClusterMessageSerializer((ExtendedActorSystem)system);
        Require(legacy.Identifier == 5, "Legacy id changed.");
        var modern = system.Serialization.FindSerializerForType(typeof(UnboundMarker), "akka-cluster-v2") as SerializerV2
            ?? throw new InvalidOperationException("Native read-only alias is not registered.");
        Require(modern.Identifier == 45, "Native id changed.");
        watchdog.ThrowIfAnyProblems(mode.Name, "startup");
        foreach (var fixture in FrozenFixture.All.Where(item => item.Historical == historical))
        {
            var oldBytes = Convert.FromHexString(fixture.LegacyHex);
            var newBytes = Convert.FromHexString(fixture.V2Hex);
            var original = system.Serialization.Deserialize(oldBytes, 5, fixture.Manifest);
            Require(modern.Manifest(original) == fixture.Manifest, fixture.Name + ": native manifest changed.");
            Require(modern.ToBinary(original).AsSpan().SequenceEqual(newBytes), fixture.Name + ": frozen V2 writer bytes changed.");
            if (fixture.IsHeartbeat)
            {
                // Legacy heartbeat ToBinary/Manifest call Cluster.Get. Decode their complete
                // known wire shapes instead, without activating a native Cluster extension.
                CheckHeartbeatWire(fixture, oldBytes, newBytes);
                heartbeatWires++;
            }
            else
            {
                Require(legacy.ToBinary(original).AsSpan().SequenceEqual(oldBytes), fixture.Name + ": frozen legacy fields changed.");
                legacyWrites++;
            }
            CheckRestored(system.Serialization.Deserialize(newBytes, 45, fixture.Manifest));
            CheckRestored(modern.FromBinary(newBytes, fixture.Manifest));
            var buffer = new ArrayBufferWriter<byte>();
            Require(modern.Serialize(original, buffer) == newBytes.Length && buffer.WrittenCount == newBytes.Length,
                fixture.Name + ": native buffer byte count changed.");
            Require(buffer.WrittenSpan.SequenceEqual(newBytes), fixture.Name + ": native buffer differs from frozen bytes.");
            CheckRestored(modern.Deserialize(Segmented(buffer.WrittenMemory), fixture.Manifest));
            buffers++;
            var writer = system.Serialization.FindSerializerFor(original);
            Require(writer.Identifier == mode.Id, fixture.Name + ": off/on/two-binding pin selected the wrong writer.");
            // All native selected writes are exercised. For old heartbeat, writer selection
            // and id 5 reading are checked, but its Cluster-starting writer is intentionally
            // outside this local codec proof (see README).
            if (!fixture.IsHeartbeat || mode.Id == 45)
            {
                Require(Akka.Serialization.Serialization.ManifestFor(writer, original) == fixture.Manifest,
                    fixture.Name + ": selected writer manifest changed.");
                var selectedBytes = writer.ToBinary(original);
                Require(selectedBytes.AsSpan().SequenceEqual(mode.Id == 45 ? newBytes : oldBytes),
                    fixture.Name + ": selected writer bytes changed.");
                CheckRestored(system.Serialization.Deserialize(selectedBytes, writer.Identifier, fixture.Manifest));
            }
            selections++;
            cases++;

            void CheckRestored(object restored)
            {
                Require(restored.GetType() == original.GetType(), fixture.Name + ": domain runtime type changed.");
                Require(modern.ToBinary(restored).AsSpan().SequenceEqual(newBytes), fixture.Name + ": restored V2 fields changed.");
                if (fixture.IsHeartbeat)
                {
                    // Domain Equals compares address/UID AND sequence/time for both heartbeat
                    // classes. Public object dispatch does not require a private type reference.
                    Require(original.Equals(restored), fixture.Name + ": restored heartbeat state changed.");
                }
                else
                    Require(legacy.ToBinary(restored).AsSpan().SequenceEqual(oldBytes), fixture.Name + ": restored full legacy state changed.");
            }
        }
        MustReject(() => system.Serialization.Deserialize([], 5, "unknown"), legacy: true);
        MustReject(() => system.Serialization.Deserialize([], 45, "unknown"));
        MustReject(() => modern.Deserialize(Segmented(new byte[] { 0x80 }), "unknown"));
        rejected += 3;
        watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
    }
    finally
    {
        await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
    }
    watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
}
var customCases = 0;
foreach (var mode in new[] { (Name: "off", Id: 5), (Name: "on", Id: 45), (Name: "pinned", Id: 5) })
{
    PoolSerializer? nested = null;
    var watchdog = new LogWatchdogFilter();
    var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(Settings(mode.Name, false)),
        new LogFilterSetup([watchdog]), SerializationSetup.Create(node => ImmutableHashSet.Create(
            SerializerDetails.Create("consumer-pool", nested = new PoolSerializer(node), ImmutableHashSet.Create(typeof(RoundRobinPool))))));
    var system = ActorSystem.Create("cluster-public-native", setup);
    try
    {
        var legacy = new ClusterMessageSerializer((ExtendedActorSystem)system);
        var modern = (SerializerV2)system.Serialization.FindSerializerForType(typeof(UnboundMarker), "akka-cluster-v2");
        var message = new ClusterRouterPool(new RoundRobinPool(17), new ClusterRouterPoolSettings(23, 3, true, "backend"));
        var selected = system.Serialization.FindSerializerFor(message);
        Require(selected.Identifier == mode.Id, "Custom payload changed the outer selected writer.");
        Require(system.Serialization.FindSerializerFor(message.Local).Identifier == 777, "User nested codec was not selected.");
        foreach (var writer in new Serializer[] { legacy, modern })
        {
            var beforeWrites = nested!.Writes;
            var bytes = writer.ToBinary(message);
            Require(nested.Writes == beforeWrites + 1, "Outer writer bypassed the user nested codec.");
            var beforeReads = nested.Reads;
            var restored = (ClusterRouterPool)system.Serialization.Deserialize(bytes, writer.Identifier, writer.Manifest(message));
            Require(nested.Reads == beforeReads + 1, "Outer reader bypassed the user nested codec.");
            Require(restored.Local is RoundRobinPool { NrOfInstances: 17 } pool && pool.RouterDispatcher == message.Local.RouterDispatcher,
                "Custom nested pool state changed.");
            Require(restored.Settings.Equals(message.Settings), "Custom nested outer settings changed.");
            customCases++;
        }
        watchdog.ThrowIfAnyProblems(mode.Name, "custom serialization");
    }
    finally
    {
        await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
    }
    watchdog.ThrowIfAnyProblems(mode.Name, "custom shutdown");
}
Require(cases == 60 && buffers == 60 && selections == 60 && legacyWrites == 48 && heartbeatWires == 12 && rejected == 18 && customCases == 6,
    "Incomplete ordinary-consumer corpus.");
Console.WriteLine($"[cluster-public-native] OK frozen-cases={cases} native-buffer-segmented={buffers} writer-selections={selections} legacy-full-writes={legacyWrites} heartbeat-wire-cases={heartbeatWires} unknown-rejections={rejected} user-codecs={customCases}");

static Config Settings(string mode, bool historical)
{
    var config = $$"""
        akka.actor.serialization-v2 = {{(mode == "off" ? "off" : "on")}}
        akka.cluster.use-legacy-heartbeat-message = {{(historical ? "on" : "off")}}
        """;
    if (mode == "pinned")
        config += """

            akka.actor.serialization-bindings {
                "Akka.Cluster.IClusterMessage, Akka.Cluster" = akka-cluster
                "Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster" = akka-cluster
            }
            """;
    return ConfigurationFactory.ParseString(config);
}

static void CheckHeartbeatWire(FrozenFixture fixture, byte[] oldBytes, byte[] newBytes)
{
    var response = fixture.Name == "HeartbeatRsp";
    var expectedAddress = new Address("akka.tcp", "cluster", "node1", 2552);
    const long expectedUid = (1L << 40) + 17;
    using var protobuf = new CodedInputStream(oldBytes);
    if (fixture.Historical)
    {
        if (response)
            Require(ReadUnique(protobuf) == expectedUid, "Historical heartbeat UID changed.");
        else
            ReadAddress(protobuf);
    }
    else
    {
        Require(protobuf.ReadTag() == 10, "Heartbeat from field changed.");
        using var from = new CodedInputStream(protobuf.ReadBytes().ToByteArray());
        if (response)
            Require(ReadUnique(from) == expectedUid, "Heartbeat response UID changed.");
        else
            ReadAddress(from);
        Require(protobuf.ReadTag() == 16 && protobuf.ReadInt64() == 17, "Heartbeat sequence changed.");
        Require(protobuf.ReadTag() == 24 && (response ? protobuf.ReadInt64() : protobuf.ReadSInt64()) == 1234,
            "Heartbeat creation time changed.");
    }
    Require(protobuf.ReadTag() == 0, "Unexpected protobuf heartbeat fields.");
    var reader = new MessagePackReader(new ReadOnlySequence<byte>(newBytes));
    Require(reader.ReadMapHeader() == (fixture.Historical ? 1 : 3) && reader.ReadInt32() == 1,
        "Heartbeat MessagePack fields changed.");
    if (response)
        Require(reader.ReadMapHeader() == 2 && reader.ReadInt32() == 1, "Heartbeat unique address fields changed.");
    var address = new AddressFormatter().Read(ref reader);
    Require(address.Equals(expectedAddress), "Heartbeat MessagePack address changed.");
    if (response)
        Require(reader.ReadInt32() == 2 && reader.ReadInt64() == expectedUid, "Heartbeat MessagePack UID changed.");
    if (!fixture.Historical)
        Require(reader.ReadInt32() == 2 && reader.ReadInt64() == 17 && reader.ReadInt32() == 3 && reader.ReadInt64() == 1234,
            "Heartbeat MessagePack sequence/time changed.");
    Require(reader.End, "Unexpected MessagePack heartbeat fields.");
    return;

    long ReadUnique(CodedInputStream input)
    {
        Require(input.ReadTag() == 10, "Heartbeat unique address missing address.");
        using var addressInput = new CodedInputStream(input.ReadBytes().ToByteArray());
        ReadAddress(addressInput);
        Require(input.ReadTag() == 16, "Heartbeat unique address missing UID.");
        var uid = checked((long)input.ReadUInt64());
        Require(input.ReadTag() == 0, "Unexpected unique address fields.");
        return uid;
    }
    void ReadAddress(CodedInputStream input)
    {
        Require(input.ReadTag() == 10 && input.ReadString() == expectedAddress.System, "Heartbeat address system changed.");
        Require(input.ReadTag() == 18 && input.ReadString() == expectedAddress.Host, "Heartbeat address host changed.");
        Require(input.ReadTag() == 24 && input.ReadUInt32() == expectedAddress.Port, "Heartbeat address port changed.");
        Require(input.ReadTag() == 34 && input.ReadString() == expectedAddress.Protocol, "Heartbeat address protocol changed.");
        Require(input.ReadTag() == 0, "Unexpected address fields.");
    }
}

static void MustReject(Action read, bool legacy = false)
{
    try
    {
        read();
    }
    catch (SerializationException) when (!legacy)
    {
        return;
    }
    catch (ArgumentException) when (legacy)
    {
        return;
    }
    throw new InvalidOperationException("Unknown manifest was accepted.");
}

static ReadOnlySequence<byte> Segmented(ReadOnlyMemory<byte> bytes)
{
    var first = new Segment(bytes[..(bytes.Length / 2)]);
    var last = first.Append(bytes[(bytes.Length / 2)..]);
    return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class UnboundMarker;
internal sealed class Segment : ReadOnlySequenceSegment<byte>
{
    public Segment(ReadOnlyMemory<byte> memory)
    {
        Memory = memory;
    }

    public Segment Append(ReadOnlyMemory<byte> memory)
    {
        var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = next;
        return next;
    }

}

internal sealed class PoolSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
{
    public int Writes { get; private set; }
    public int Reads { get; private set; }
    public override int Identifier => 777;
    public override string Manifest(object obj) => "consumer-pool";
    public override byte[] ToBinary(object obj)
    {
        Writes++;
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, ((RoundRobinPool)obj).NrOfInstances);
        return bytes;
    }
    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (manifest != "consumer-pool" || bytes.Length != 4)
            throw new SerializationException("Unknown consumer pool wire shape.");
        Reads++;
        return new RoundRobinPool(BinaryPrimitives.ReadInt32LittleEndian(bytes));
    }
}
