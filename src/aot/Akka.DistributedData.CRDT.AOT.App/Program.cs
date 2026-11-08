// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.DistributedData.CRDT.AOT.App;
using Akka.DistributedData.Serialization;
using Akka.Event;
using Akka.Serialization;
using MessagePack;

Proof.Require(!RuntimeFeature.IsDynamicCodeSupported, "Actual native executable required");
Proof.Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
    "Dynamic type loading must be explicitly disabled");
var casesRead = 0;
var nativeWrites = 0;
var segmentedReads = 0;
var historicalReads = 0;
var genericWritesRejected = 0;
var genericReadsRejected = 0;
var atomicControls = 0;
var guardedCallbacks = 0;
var ownershipControls = 0;
var unknownManifestControls = 0;
foreach (var mode in new[] { "off", "on", "pinned" })
{
    var pin = mode == "pinned" ? """
        akka.actor.serialization-bindings {
          "Akka.DistributedData.IReplicatedDataSerialization, Akka.DistributedData" = akka-replicated-data
        }
        """ : "";
    var config = ConfigurationFactory.ParseString($"""
        akka.actor.provider = local
        akka.actor.serialization-v2 = {(mode == "off" ? "off" : "on")}
        {pin}
        """).WithFallback(DistributedData.DefaultConfig());
    var setup = SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
        "public-crdt-payload", new PublicPayloadSerializer(system), ImmutableHashSet.Create(typeof(string), typeof(Payload)))));
    var watchdog = new LogWatchdogFilter();
    var system = ActorSystem.Create("crdt-public", ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config),
        setup, new LogFilterSetup([watchdog])));
    try
    {
        watchdog.ThrowIfAnyProblems(mode, "startup");
        var node = (ExtendedActorSystem)system;
        // Ordinary public constructor anchor; registration still comes from the real module table.
        _ = new ReplicatedDataSerializer(node);
        var legacy = system.Serialization.FindSerializerForType(typeof(LegacyAliasMarker), "akka-replicated-data");
        var reader = system.Serialization.FindSerializerForType(typeof(ModernAliasMarker), "akka-replicated-data-v2");
        Proof.Require(legacy.Identifier == 11 && reader.Identifier == 51, "Both registered alias readers retain their IDs");
        Proof.Require(reader is SerializerV2, "Registered id 51 must be a native buffer serializer");
        var modern = (SerializerV2)reader;
        var payload = (PublicPayloadSerializer)system.Serialization.FindSerializerForType(typeof(PayloadAliasMarker), "public-crdt-payload");
        Proof.Require(payload.Identifier == 777, "Application payload alias retains its ID");
        var corpus = PublicCases.Create(node, legacy);
        foreach (var item in corpus)
        {
            Console.WriteLine($"[crdt-public-native] mode={mode} reading={item.Name}");
            Proof.Require(system.Serialization.FindSerializerFor(item.Value).Identifier == (mode == "on" ? 51 : 11),
                "Actual module writer selection: " + item.Name);
            Proof.Require(modern.Manifest(item.Value) == item.Manifest, "Manifest: " + item.Name);
            var bytes = modern.ToBinary(item.Value);
            item.Check(modern.FromBinary(bytes, item.Manifest));
            item.Check(system.Serialization.Deserialize(bytes, 51, item.Manifest));
            casesRead++;
            var output = new ArrayBufferWriter<byte>();
            var count = modern.Serialize(item.Value, output);
            Proof.Require(count == output.WrittenCount && output.WrittenSpan.SequenceEqual(bytes), "Native buffer bytes/count: " + item.Name);
            nativeWrites++;
            var sequence = Proof.Segmented(output.WrittenMemory);
            Proof.Require(!sequence.IsSingleSegment, "Actual segmented input: " + item.Name);
            item.Check(modern.Deserialize(sequence, item.Manifest));
            segmentedReads++;
        }
        foreach (var record in FrozenRecords.All)
        {
            var bytes = Convert.FromHexString(record.Hex);
            if (record.Generic)
            {
                Proof.Reject(() => modern.FromBinary(bytes, record.Manifest));
                Proof.Reject(() => modern.Deserialize(Proof.Segmented(bytes), record.Manifest));
                Proof.Reject(() => system.Serialization.Deserialize(bytes, 51, record.Manifest));
                genericReadsRejected++;
            }
            else
            {
                Console.WriteLine($"[crdt-public-native] mode={mode} historical={record.Name}");
                var value = system.Serialization.Deserialize(bytes, 11, record.Manifest);
                Proof.Require(legacy.Manifest(value) == record.Manifest, "Historical runtime kind: " + record.Name);
                PublicCases.CheckHistorical(record, value, corpus, legacy, node);
                historicalReads++;
            }
        }
        payload.Reads = 0;
        payload.Writes = 0;
        const string unknownManifest = "not-a-crdt-manifest";
        Proof.Require(Proof.Capture(() => modern.Deserialize(Proof.Segmented(new byte[] { 0x80 }), unknownManifest))
            .Message.Contains(unknownManifest, StringComparison.Ordinal), "Unknown manifest rejects on the native reader");
        Proof.Require(Proof.Capture(() => system.Serialization.Deserialize(new byte[] { 0x80 }, 51, unknownManifest))
            .Message.Contains(unknownManifest, StringComparison.Ordinal), "Unknown manifest rejects through the registered id");
        unknownManifestControls += 2;
        foreach (var value in Unsupported(node))
        {
            Proof.Require(system.Serialization.FindSerializerFor(value).Identifier == (mode == "on" ? 51 : 11), "Unsupported-shape binding");
            var output = new ArrayBufferWriter<byte>();
            output.Write(new byte[] { 0xaa, 0xbb });
            Proof.Reject(() => modern.Serialize(value, output));
            Proof.Reject(() => modern.ToBinary(value));
            Proof.Require(output.WrittenSpan.SequenceEqual(new byte[] { 0xaa, 0xbb }), "Cold generic failure preserves prior output");
            Proof.Require(payload.Reads == 0 && payload.Writes == 0, "Cold generic guard precedes application callbacks");
            genericWritesRejected++;
        }

        var known = new LWWRegister<string>(PublicCases.Owner(node), "application-owned", 17);
        var knownBytes = modern.ToBinary(known);
        Proof.Require(payload.Writes == 1, "Registered application payload writer is required");
        var knownRoundTrip = (LWWRegister<string>)modern.Deserialize(Proof.Segmented(knownBytes), "E");
        Proof.Require(known.Equals(knownRoundTrip) && payload.Reads == 1, "Registered application payload reader is required");
        ownershipControls++;
        payload.Reads = 0;
        payload.Writes = 0;
        payload.FailRead = true;
        var unsupportedDescriptor = Proof.UnknownDescriptorWithPayloadFirst(knownBytes);
        Proof.Reject(() => modern.Deserialize(Proof.Segmented(unsupportedDescriptor), "E"));
        Proof.Reject(() => system.Serialization.Deserialize(unsupportedDescriptor, 51, "E"));
        Proof.Require(payload.Reads == 0, "Metadata preflight precedes payload-first application reads");
        guardedCallbacks += 2;
        payload.FailRead = false;
        payload.FailWrite = true;
        var atomic = new ArrayBufferWriter<byte>();
        atomic.Write(new byte[] { 0xaa, 0xbb });
        var failure = Proof.Capture(() => modern.Serialize(known, atomic));
        Proof.Require(failure.Message.Contains("late application payload", StringComparison.Ordinal) && payload.Writes == 1 &&
            atomic.WrittenSpan.SequenceEqual(new byte[] { 0xaa, 0xbb }), "Late nested payload failure preserves all prior caller output");
        atomicControls++;
        watchdog.ThrowIfAnyProblems(mode, "serialization");
    }
    finally
    {
        await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
    }
    watchdog.ThrowIfAnyProblems(mode, "shutdown");
}

Proof.Require(casesRead == 207 && nativeWrites == 207 && segmentedReads == 207 && historicalReads == 93 &&
    genericWritesRejected == 42 && genericReadsRejected == 75 && ownershipControls == 3 && atomicControls == 3 && guardedCallbacks == 6 && unknownManifestControls == 6,
    "Complete ordinary public consumer corpus");
Console.WriteLine($"[crdt-public-native] OK cases={casesRead} native-writes={nativeWrites} segmented-reads={segmentedReads} historical-reads={historicalReads} generic-writes-rejected={genericWritesRejected} generic-reads-rejected={genericReadsRejected} ownership={ownershipControls} atomic={atomicControls} guarded-callbacks={guardedCallbacks} unknown-manifests={unknownManifestControls}");

static object[] Unsupported(ExtendedActorSystem system)
{
    var owner = PublicCases.Owner(system);
    var value = new Payload("unsupported");
    return
    [
        GSet.Create(value),
        new ORSet<Payload>(ImmutableDictionary<Payload, VersionVector>.Empty.Add(value, VersionVector.Create(owner, 17)), VersionVector.Create(owner, 17)),
        new LWWRegister<Payload>(owner, value, 17),
        ORDictionary<string, GCounter>.Empty, LWWDictionary<string, string>.Empty,
        PNCounterDictionary<string>.Empty, ORMultiValueDictionary<string, string>.Empty,
        new GSetKey<Payload>("key"), new ORSetKey<Payload>("key"), new LWWRegisterKey<Payload>("key"),
        new PNCounterDictionaryKey<Payload>("key"), new ORDictionaryKey<string, GCounter>("key"),
        new LWWDictionaryKey<string, string>("key"), new ORMultiValueDictionaryKey<string, string>("key")
    ];
}

internal sealed class LegacyAliasMarker;
internal sealed class ModernAliasMarker;
internal sealed class PayloadAliasMarker;

internal static class Proof
{
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static SerializationException Capture(Action action)
    {
        try
        {
            action();
        }
        catch (SerializationException error)
        {
            return error;
        }
        throw new InvalidOperationException("Expected an explicit serialization rejection");
    }

    internal static void Reject(Action action) => Require(Capture(action).Message.Contains("Akka.DynamicTypeLoading", StringComparison.Ordinal),
        "Explicit generic switch reason");

    internal static ReadOnlySequence<byte> Segmented(ReadOnlyMemory<byte> bytes)
    {
        var first = new Segment(bytes[..(bytes.Length / 2)]);
        var last = first.Append(bytes[(bytes.Length / 2)..]);
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    // Negative control derived from a valid string register; this is not historical writer output.
    // Keep its real id-777 payload bytes, declare an unsupported type, and put payload first.
    internal static byte[] UnknownDescriptorWithPayloadFirst(byte[] bytes)
    {
        var sequence = new ReadOnlySequence<byte>(bytes);
        var reader = new MessagePackReader(sequence);
        var count = reader.ReadMapHeader();
        var fields = new List<(int Key, byte[] Bytes)>();
        for (var index = 0; index < count; index++)
        {
            var key = reader.ReadInt32();
            var start = reader.Position;
            reader.Skip();
            fields.Add((key, sequence.Slice(start, reader.Position).ToArray()));
        }
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output);
        writer.WriteMapHeader(count);
        foreach (var field in fields.OrderBy(item => item.Key == 2 ? 0 : 1))
        {
            writer.Write(field.Key);
            if (field.Key == 1)
            {
                writer.WriteMapHeader(2);
                writer.Write(1);
                writer.Write(4);
                writer.Write(2);
                writer.Write("unsupported.closed.payload, Missing");
            }
            else writer.WriteRaw(field.Bytes);
        }
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> bytes)
        {
            Memory = bytes;
        }

        public Segment Append(ReadOnlyMemory<byte> bytes)
        {
            var next = new Segment(bytes) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
