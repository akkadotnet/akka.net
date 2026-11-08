// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;
using Akka.Remote.Serialization;
using Akka.Streams.Serialization;
using Google.Protobuf;
using AkkaSerialization = Akka.Serialization.Serialization;


namespace Akka.AOT.SerializationV2;

internal static class StreamsProbe
{
    internal static async Task<int> RunAsync()
    {
        var cases = 0;
        var nativeWrites = 0;
        var segmentedReads = 0;
        var rejected = 0;
        var userPayloads = 0;
        try
        {
            Require(!RuntimeFeature.IsDynamicCodeSupported, "A native executable is required.");
            Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
                "Dynamic type loading must be explicitly disabled.");
            foreach (var mode in new[] { (Name: "off", Id: 30), (Name: "on", Id: 70), (Name: "pinned", Id: 30) })
            {
                var settings = $"akka.actor.serialization-v2 = {(mode.Name == "off" ? "off" : "on")}";
                if (mode.Name == "pinned")
                    settings += """

                        akka.actor.serialization-bindings {
                          "Akka.Streams.Implementation.StreamRef.IStreamRefsProtocol, Akka.Streams" = akka-stream-ref
                        }
                        """;
                var watchdog = new LogWatchdogFilter();
                var system = ActorSystem.Create("streams-native-" + mode.Name,
                    ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(settings)),
                        new LogFilterSetup([watchdog]),
                        SerializationSetup.Create(owner => ImmutableHashSet.Create(
                            SerializerDetails.Create("stream-native-user-v1", new UserV1Serializer(owner), ImmutableHashSet.Create(typeof(UserV1))),
                            SerializerDetails.Create("stream-native-user-v2", new UserV2Serializer(owner), ImmutableHashSet.Create(typeof(UserV2)))))));
                try
                {
                    // The public legacy codec bootstraps private protocol shapes from frozen bytes.
                    // The executable uses its ordinary assembly identity and no reflective type access.
                    var legacy = new StreamRefSerializer((ExtendedActorSystem)system);
                    Require(legacy.Identifier == 30, "Legacy id changed.");
                    Require(new PrimitiveSerializers((ExtendedActorSystem)system, ConfigurationFactory.ParseString("use-legacy-behavior = on")).Identifier == 17, "Primitive payload codec missing.");
                    watchdog.ThrowIfAnyProblems(mode.Name, "startup");
                    foreach (var fixture in StreamProtocolFrozenFixtures.Cases)
                    {
                        var oldBytes = Convert.FromHexString(fixture.LegacyHex);
                        var newBytes = Convert.FromHexString(fixture.V2Hex);
                        var expected = system.Serialization.Deserialize(oldBytes, 30, fixture.Manifest);
                        var actual = system.Serialization.Deserialize(newBytes, 70, fixture.Manifest);
                        Compare(expected, actual, fixture.Manifest, legacy);
                        var writer = system.Serialization.FindSerializerFor(expected);
                        Require(writer.Identifier == mode.Id, "Incorrect selected writer: " + fixture.Name);
                        Require(AkkaSerialization.ManifestFor(writer, expected) == fixture.Manifest, "Manifest changed.");
                        var bytes = writer.ToBinary(expected);
                        Compare(expected, system.Serialization.Deserialize(bytes, writer.Identifier, fixture.Manifest), fixture.Manifest, legacy);
                        // Nobody canonically resolves to this system's deadLetters. All remaining
                        // records preserve the exact reviewed bytes, including nested primitive manifests.
                        if (fixture.Manifest != "G")
                            Require(bytes.AsSpan().SequenceEqual(mode.Id == 30 ? oldBytes : newBytes), "Frozen writer bytes differ.");
                        if (mode.Id == 70)
                        {
                            var modern = writer as SerializerV2 ?? throw new InvalidOperationException("Native writer expected.");
                            var buffer = new ArrayBufferWriter<byte>();
                            Require(modern.Serialize(expected, buffer) == bytes.Length && buffer.WrittenCount == bytes.Length, "Native size differs.");
                            Require(buffer.WrittenSpan.SequenceEqual(bytes), "Native bytes differ.");
                            Compare(expected, modern.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), fixture.Manifest), fixture.Manifest, legacy);
                            nativeWrites++;
                        }
                        for (var split = 0; split <= newBytes.Length; split++)
                            Compare(expected, system.Serialization.Deserialize(Split(newBytes, split), 70, fixture.Manifest), fixture.Manifest, legacy);
                        segmentedReads++;
                        cases++;
                    }
                    foreach (var payload in new object[] { new UserV1("one"), new UserV2("two") })
                    {
                        var payloadCodec = system.Serialization.FindSerializerFor(payload);
                        Require(payloadCodec.Identifier == (payload is UserV1 ? 70701 : 70702), "Incorrect user payload writer.");
                        var message = system.Serialization.Deserialize(LegacyNext(payloadCodec, payload), 30, "A");
                        var writer = system.Serialization.FindSerializerFor(message);
                        Require(writer.Identifier == mode.Id, "User payload changed the protocol writer.");
                        Compare(message, system.Serialization.Deserialize(writer.ToBinary(message), mode.Id, "A"), "A", legacy);
                        if (mode.Id == 70)
                        {
                            var modern = writer as SerializerV2 ?? throw new InvalidOperationException("User envelopes need native writes.");
                            var buffer = new ArrayBufferWriter<byte>();
                            Require(modern.Serialize(message, buffer) == buffer.WrittenCount, "User envelope byte count differs.");
                            Compare(message, modern.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "A"), "A", legacy);
                            nativeWrites++;
                        }
                        userPayloads++;
                    }

                    // The wire format of bare refs is unchanged; native reads still reject their
                    // runtime generic reconstruction. Neither E nor F belongs to the new codec.
                    var typeName = Encoding.UTF8.GetBytes("System.Int32, System.Private.CoreLib");
                    var eventType = new byte[] { 0x0a, checked((byte)typeName.Length) }.Concat(typeName).ToArray();
                    var bare = new byte[] { 0x0a, 0x00, 0x12, checked((byte)eventType.Length) }.Concat(eventType).ToArray();
                    foreach (var manifest in new[] { "E", "F" })
                    {
                        Reject(() => system.Serialization.Deserialize(bare, 30, manifest), "Akka.DynamicTypeLoading");
                        Reject(() => system.Serialization.Deserialize(new byte[] { 0x80 }, 70, manifest), null);
                        rejected += 2;
                    }
                    watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
                    Console.WriteLine($"[streams-native] mode={mode.Name} writer={mode.Id} cases=12 user-payloads=2 rejected=4");
                }
                finally
                {
                    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                }
                watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
            }
            Require(cases == 36 && nativeWrites == 14 && segmentedReads == 36 && userPayloads == 6 && rejected == 12, "Incomplete corpus.");
            Console.WriteLine("[streams-native] OK cases=36 native-writes=14 segmented-reads=36 user-payloads=6 rejected=12");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[streams-native] FAILED: " + error);
            return 1;
        }
    }

    // Construct the documented legacy protobuf envelope around independently registered user
    // codecs. This bootstraps the private domain type without accessing it or using reflection.
    static byte[] LegacyNext(Serializer payloadCodec, object payload)
    {
        using var payloadBytes = new MemoryStream();
        using (var output = new CodedOutputStream(payloadBytes, leaveOpen: true))
        {
            output.WriteTag(1, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(payloadCodec.ToBinary(payload)));
            output.WriteTag(2, WireFormat.WireType.Varint);
            output.WriteInt32(payloadCodec.Identifier);
            output.WriteTag(3, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFromUtf8(AkkaSerialization.ManifestFor(payloadCodec, payload)));
        }
        using var envelope = new MemoryStream();
        using (var output = new CodedOutputStream(envelope, leaveOpen: true))
        {
            output.WriteTag(1, WireFormat.WireType.Varint);
            output.WriteInt64(42);
            output.WriteTag(2, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(payloadBytes.ToArray()));
        }
        return envelope.ToArray();
    }

    static void Compare(object expected, object actual, string manifest, StreamRefSerializer legacy)
    {
        Require(expected.GetType() == actual.GetType(), "Protocol runtime class differs.");
        // Re-encode every member through the unchanged protobuf writer. No message Equals
        // implementation or internal-type reflection can hide a changed sequence/payload/ref.
        Require(legacy.Manifest(expected) == manifest && legacy.Manifest(actual) == manifest, "Runtime manifest differs.");
        Require(legacy.ToBinary(expected).AsSpan().SequenceEqual(legacy.ToBinary(actual)), "Protocol members differ.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Reject(Action action, string? expectedMessage)
    {
        try { action(); }
        catch (SerializationException error)
        {
            Require(expectedMessage is null || error.Message.Contains(expectedMessage, StringComparison.Ordinal), "Incorrect failure boundary: " + error.Message);
            return;
        }
        throw new InvalidOperationException("Expected serialization rejection.");
    }

    static ReadOnlySequence<byte> Split(byte[] bytes, int split)
    {
        var first = new Segment(bytes.AsMemory(0, split));
        var last = first.Append(bytes.AsMemory(split));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> nextMemory)
        {
            var next = new Segment(nextMemory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }


    sealed record UserV1(string Value);
    sealed record UserV2(string Value);

    sealed class UserV1Serializer(ExtendedActorSystem owner) : SerializerWithStringManifest(owner)
    {
        public override int Identifier => 70701;
        public override string Manifest(object obj) => "u1";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UserV1)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => manifest == "u1"
            ? new UserV1(Encoding.UTF8.GetString(bytes)) : throw new SerializationException("Unknown user V1 manifest.");
    }

    sealed class UserV2Serializer(ExtendedActorSystem owner) : SerializerV2(owner)
    {
        public override int Identifier => 70702;
        public override string Manifest(object obj) => "u2";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((UserV2)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => manifest == "u2"
            ? new UserV2(Encoding.UTF8.GetString(bytes.ToArray())) : throw new SerializationException("Unknown user V2 manifest.");
    }
}
