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
using Akka.Persistence;
using Akka.Persistence.Fsm;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using Akka.Util;

namespace Akka.AOT.SerializationV2;

internal static class PersistenceMessageProbe
{
    internal static async Task<int> RunAsync()
    {
        // Frozen pre-port protobuf bytes, legacy source blob
        // d87af7aaac58d405302174cab9ab64438d682c70 (upstream dev 2d6a06797).
        (string Name, string Manifest, string Hex, bool Generic)[] samples =
        [
            ("atomic-pair", "Akka.Persistence.AtomicWrite, Akka.Persistence", "0a420a2e081112036f6e651a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696210011a037069646a0677726974657270d00f0a420a2e0811120374776f1a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696210021a037069646a0677726974657270a01f", false),
            ("atomic-single", "Akka.Persistence.AtomicWrite, Akka.Persistence", "0a420a2e081112036f6e651a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696210011a037069646a0677726974657270d00f", false),
            ("delivery-empty", "Akka.Persistence.AtLeastOnceDeliverySnapshot, Akka.Persistence", "", false),
            ("delivery", "Akka.Persistence.AtLeastOnceDeliverySnapshot, Akka.Persistence", "08ffffffffffffffff7f127008171237616b6b612e7463703a2f2f64656c69766572792d66697874757265403132372e302e302e313a323535322f757365722f736572766963651a330811120864656c69766572791a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", false),
            ("fsm-snapshot-no-timeout", "Akka.Persistence.Fsm.PersistentFSM+PersistentFSMSnapshot`1[[System.String, System.Private.CoreLib]], Akka.Persistence", "0a057374617465122f08111204646174611a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", true),
            ("fsm-snapshot", "Akka.Persistence.Fsm.PersistentFSM+PersistentFSMSnapshot`1[[System.String, System.Private.CoreLib]], Akka.Persistence", "0a057374617465122f08111204646174611a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962187b", true),
            ("persistent-empty", "Akka.Persistence.Persistent, Akka.Persistence", "0a020804", false),
            ("persistent-null-negative", "Akka.Persistence.Persistent, Akka.Persistence", "0a0010ffffffffffffffffff011a086e65676174697665", false),
            ("persistent", "Akka.Persistence.Persistent, Akka.Persistence", "0a32081112077061796c6f61641a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696210111a0370696420016207616461707465726a067772697465727080ebd5c8a7b5b104", false),
            ("state-change", "Akka.Persistence.Fsm.PersistentFSM+StateChangeEvent, Akka.Persistence", "0a057374617465107b", false),
            ("state-negative-timeout", "Akka.Persistence.Fsm.PersistentFSM+StateChangeEvent, Akka.Persistence", "0a05737461746510ffffffffffffffffff01", false),
            ("state-no-timeout", "Akka.Persistence.Fsm.PersistentFSM+StateChangeEvent, Akka.Persistence", "0a057374617465", false),
            ("state-zero-timeout", "Akka.Persistence.Fsm.PersistentFSM+StateChangeEvent, Akka.Persistence", "0a057374617465", false),
        ];
        var cases = 0;
        var writes = 0;
        var segments = 0;
        var aliases = 0;
        var payloads = 0;
        var rejected = 0;
        try
        {
            Require(!RuntimeFeature.IsDynamicCodeSupported, "Native execution required.");
            Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var enabled) && !enabled, "Explicit dynamic loading false required.");
            foreach (var spelling in new[] { "default", "stripped", "versioned" })
            {
                var oldName = "Akka.Persistence.Serialization.PersistenceMessageSerializer, Akka.Persistence";
                if (spelling == "versioned") oldName += ", Version=1.4.0.0, Culture=neutral, PublicKeyToken=null";
                var config = spelling == "default" ? Config.Empty
                    : ConfigurationFactory.ParseString($"akka.actor.serializers.akka-persistence-message = \"{oldName}\"");
                var watchdog = new LogWatchdogFilter();
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new LogFilterSetup([watchdog]),
                    SerializationSetup.Create(system => ImmutableHashSet.Create(
                        SerializerDetails.Create("user-v1", new UserV1Serializer(system), ImmutableHashSet.Create(typeof(UserV1))),
                        SerializerDetails.Create("user-v2", new UserV2Serializer(system), ImmutableHashSet.Create(typeof(UserV2))))));
                var system = ActorSystem.Create($"message-native-{spelling}", setup);
                try
                {
                    watchdog.ThrowIfAnyProblems(spelling, "startup");
                    var writer = system.Serialization.FindSerializerForType(typeof(Persistent));
                    Require(writer.Identifier == 7 && writer is SerializerV2, "Native id7 did not resolve.");
                    var native = (SerializerV2)writer;
                    var legacy = new PersistenceMessageSerializer((ExtendedActorSystem)system);
                    foreach (var sample in samples)
                    {
                        var original = Original(sample.Name);
                        var bytes = Convert.FromHexString(sample.Hex);
                        if (sample.Generic)
                        {
                            var untouched = new ArrayBufferWriter<byte>();
                            RejectFsm(() => native.Serialize(original, untouched));
                            RejectFsm(() => native.ToBinary(original));
                            RejectFsm(() => native.Manifest(original));
                            RejectFsm(() => native.Deserialize(new ReadOnlySequence<byte>(bytes), sample.Manifest));
                            Require(untouched.WrittenCount == 0, "Unsupported generic FSM partially wrote an envelope.");
                            rejected += 4;
                            continue;
                        }
                        Require(native.Manifest(original) == sample.Manifest, $"{sample.Name}: changed manifest.");
                        Require(native.ToBinary(original).AsSpan().SequenceEqual(bytes), $"{sample.Name}: changed historical bytes.");
                        var buffer = new ArrayBufferWriter<byte>();
                        Require(native.Serialize(original, buffer) == bytes.Length && buffer.WrittenCount == bytes.Length,
                            $"{sample.Name}: wrong native write count.");
                        Require(buffer.WrittenSpan.SequenceEqual(bytes), $"{sample.Name}: native bytes differ.");
                        var expected = legacy.FromBinary(bytes, sample.Manifest);
                        Same(system.Serialization.Deserialize(bytes, 7, sample.Manifest), expected, legacy);
                        Same(native.Deserialize(new ReadOnlySequence<byte>(bytes), sample.Manifest), expected, legacy);
                        Same(native.Deserialize(Split(bytes), sample.Manifest), expected, legacy);
                        Same(legacy.FromBinary(buffer.WrittenSpan.ToArray(), sample.Manifest), expected, legacy);
                        cases++; writes++; segments++;
                    }
                    var persistent = new Persistent("alias-data", 1, "pid", timestamp: 123456);
                    var aliasBytes = legacy.ToBinary(persistent);
                    foreach (var manifest in new[] { string.Empty, typeof(IPersistentRepresentation).TypeQualifiedName() })
                    {
                        Same(native.Deserialize(new ReadOnlySequence<byte>(aliasBytes), manifest), legacy.FromBinary(aliasBytes, (Type)null!), legacy);
                        aliases++;
                    }
                    foreach (var payload in new object[] { new UserV1("v1"), new UserV2("v2") })
                    {
                        var envelope = new Persistent(payload, 1, "pid", timestamp: 123456);
                        var bytes = legacy.ToBinary(envelope);
                        var buffer = new ArrayBufferWriter<byte>();
                        native.Serialize(envelope, buffer);
                        Require(buffer.WrittenSpan.SequenceEqual(bytes), "User codec envelope bytes differ.");
                        var back = (Persistent)native.Deserialize(new ReadOnlySequence<byte>(bytes), legacy.Manifest(envelope));
                        Require(Equals(back.Payload, payload) && back.Timestamp == 123456, "User codec fields differ.");
                        payloads++;
                    }
                    Reject(() => native.Deserialize(ReadOnlySequence<byte>.Empty, "unknown"));
                    Reject(() => system.Serialization.Deserialize(Array.Empty<byte>(), int.MaxValue, "unknown"));
                    var noPartial = new ArrayBufferWriter<byte>();
                    Reject(() => native.Serialize(new Persistent(new UnregisteredPayload()), noPartial));
                    Require(noPartial.WrittenCount == 0, "Unsupported user payload partially wrote envelope.");
                    rejected += 3;
                    watchdog.ThrowIfAnyProblems(spelling, "serialization");
                    Console.WriteLine($"[message-native] spelling={spelling} writer=7 fixed=11 generic-rejections=8 user-payloads=2");
                }
                finally
                {
                    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                }
                watchdog.ThrowIfAnyProblems(spelling, "shutdown");
            }
            Require(cases == 33 && writes == 33 && segments == 33 && aliases == 6 && payloads == 6 && rejected == 33,
                $"Incomplete corpus: {cases}/{writes}/{segments}/{aliases}/{payloads}/{rejected}.");
            Console.WriteLine("[message-native] OK cases=33 native-writes=33 segmented-reads=33 alias-reads=6 user-payloads=6 rejected=33");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[message-native] FAILED: {exception}");
            return 1;
        }
    }
    static void Same(object actual, object expected, PersistenceMessageSerializer legacy)
    {
        Require(actual.GetType() == expected.GetType(), "Decoded envelope types differ.");
        Require(legacy.ToBinary(actual).AsSpan().SequenceEqual(legacy.ToBinary(expected)), "Decoded envelope fields differ.");
        // Nonpositive timestamps are omitted by the old writer. Check the decoded value as
        // well, so re-encoding cannot hide a wrong negative timestamp in the native reader.
        if (actual is IPersistentRepresentation persistent)
            Require(persistent.Timestamp == ((IPersistentRepresentation)expected).Timestamp, "Decoded timestamp differs.");
        // The old writer omits an explicit zero timeout. Verify the decoded nullable value
        // directly so re-encoding cannot hide an incorrect zero instead of the legacy null.
        if (actual is PersistentFSM.StateChangeEvent state)
            Require(state.Timeout == ((PersistentFSM.StateChangeEvent)expected).Timeout, "Decoded state timeout differs.");
        if (actual is AtomicWrite atomic)
        {
            var expectedEvents = (IEnumerable<IPersistentRepresentation>)((AtomicWrite)expected).Payload;
            foreach (var pair in ((IEnumerable<IPersistentRepresentation>)atomic.Payload).Zip(expectedEvents))
                Require(pair.First.Timestamp == pair.Second.Timestamp, "Decoded atomic timestamp differs.");
        }
    }
    static object Original(string name) => name switch
    {
        "persistent" => new Persistent("payload", 17, "pid", "adapter", true, ActorRefs.NoSender, "writer", 1234567890123456),
        "persistent-empty" => new Persistent(Array.Empty<byte>()),
        "persistent-null-negative" => new Persistent(null, -1, "negative", timestamp: -1),
        "atomic-single" => new AtomicWrite(new Persistent("one", 1, "pid", writerGuid: "writer", timestamp: 1000)),
        "atomic-pair" => new AtomicWrite(ImmutableList.Create<IPersistentRepresentation>(
            new Persistent("one", 1, "pid", writerGuid: "writer", timestamp: 1000), new Persistent("two", 2, "pid", writerGuid: "writer", timestamp: 2000))),
        "delivery-empty" => new AtLeastOnceDeliverySnapshot(0, Array.Empty<UnconfirmedDelivery>()),
        "delivery" => new AtLeastOnceDeliverySnapshot(long.MaxValue, new[] { new UnconfirmedDelivery(23,
            ActorPath.Parse("akka.tcp://delivery-fixture@127.0.0.1:2552/user/service"), "delivery") }),
        "state-change" => new PersistentFSM.StateChangeEvent("state", TimeSpan.FromTicks(1234567)),
        "state-no-timeout" => new PersistentFSM.StateChangeEvent("state", null),
        "state-zero-timeout" => new PersistentFSM.StateChangeEvent("state", TimeSpan.Zero),
        "state-negative-timeout" => new PersistentFSM.StateChangeEvent("state", TimeSpan.FromMilliseconds(-1)),
        "fsm-snapshot" => new PersistentFSM.PersistentFSMSnapshot<string>("state", "data", TimeSpan.FromTicks(1234567)),
        "fsm-snapshot-no-timeout" => new PersistentFSM.PersistentFSMSnapshot<string>("state", "data", null),
        _ => throw new InvalidOperationException($"Unknown fixture [{name}].")
    };
    static ReadOnlySequence<byte> Split(byte[] bytes)
    {
        var offset = bytes.Length / 2;
        var first = new Segment(bytes.AsMemory(0, offset));
        var last = first.Append(bytes.AsMemory(offset));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    static void Reject(Action action)
    {
        try { action(); }
        catch (SerializationException) { return; }
        throw new InvalidOperationException("Expected rejection did not occur.");
    }
    static void RejectFsm(Action action)
    {
        try
        {
            action();
        }
        catch (SerializationException error) when (error.Message.Contains("PersistentFSMSnapshot<T>") &&
            error.Message.Contains("Akka.DynamicTypeLoading"))
        {
            return;
        }
        throw new InvalidOperationException("Expected the explicit native generic FSM rejection.");
    }
    sealed class UnregisteredPayload { }
    sealed record UserV1(string Value);
    sealed record UserV2(string Value);
    sealed class UserV1Serializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => 9833;
        public override string Manifest(object obj) => "u1";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UserV1)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => manifest == "u1"
            ? new UserV1(Encoding.UTF8.GetString(bytes)) : throw new SerializationException("Unknown user manifest.");
    }
    sealed class UserV2Serializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9834;
        public override string Manifest(object obj) => "u2";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((UserV2)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => manifest == "u2"
            ? new UserV2(Encoding.UTF8.GetString(bytes.ToArray())) : throw new SerializationException("Unknown user manifest.");
    }
    sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) { Memory = memory; }
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
