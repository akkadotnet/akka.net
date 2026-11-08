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
using Akka.Persistence.Serialization;
using Akka.Serialization;
using SnapshotEnvelope = Akka.Persistence.Serialization.Snapshot;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.AOT.SerializationV2;

internal static class SnapshotProbe
{
    internal static async Task<int> RunAsync()
    {
        // Frozen pre-port protobuf bytes, legacy source blob
        // a6a1c8cf63dc23c275d705a592faa37586ff2f09 (upstream dev 2d6a06797).
        const string Manifest = "Akka.Persistence.Serialization.Snapshot, Akka.Persistence";
        (string Name, string Hex)[] samples =
        [
            ("bytes-empty", "0804"),
            ("bytes", "08041204000180ff"),
            ("null", ""),
            ("string-empty", "08111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
            ("string", "08111208736e617073686f741a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        ];
        var cases = 0;
        var nativeWrites = 0;
        var userPayloads = 0;
        var segmentedReads = 0;
        var rejected = 0;
        try
        {
            Require(!RuntimeFeature.IsDynamicCodeSupported, "Native execution is required.");
            Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
                "Dynamic type loading must be explicitly disabled.");
            foreach (var spelling in new[] { "default", "stripped", "versioned" })
            {
                var oldName = "Akka.Persistence.Serialization.PersistenceSnapshotSerializer, Akka.Persistence";
                if (spelling == "versioned")
                    oldName += ", Version=1.4.0.0, Culture=neutral, PublicKeyToken=null";
                var config = spelling == "default" ? Config.Empty
                    : ConfigurationFactory.ParseString($"akka.actor.serializers.akka-persistence-snapshot = \"{oldName}\"");
                var watchdog = new LogWatchdogFilter();
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new LogFilterSetup([watchdog]),
                    SerializationSetup.Create(system => ImmutableHashSet.Create(
                        SerializerDetails.Create("snapshot-user-v1", new UserV1Serializer(system), ImmutableHashSet.Create(typeof(UserV1))),
                        SerializerDetails.Create("snapshot-user-v2", new UserV2Serializer(system), ImmutableHashSet.Create(typeof(UserV2))))));
                var system = ActorSystem.Create($"snapshot-native-{spelling}", setup);
                try
                {
                    watchdog.ThrowIfAnyProblems(spelling, "startup");
                    var writer = system.Serialization.FindSerializerFor(new SnapshotEnvelope(null));
                    Require(writer.Identifier == 8 && writer is SerializerV2, $"{spelling}: native id8 default did not resolve.");
                    var native = (SerializerV2)writer;
                    var legacy = new PersistenceSnapshotSerializer((ExtendedActorSystem)system);
                    foreach (var sample in samples)
                    {
                        var expected = Convert.FromHexString(sample.Hex);
                        var snapshot = (SnapshotEnvelope)system.Serialization.Deserialize(expected, 8, Manifest);
                        var legacySnapshot = (SnapshotEnvelope)legacy.FromBinary(expected, typeof(SnapshotEnvelope));
                        Require(Same(snapshot, legacySnapshot), $"{sample.Name}: historical read differs.");
                        Require(writer.Manifest(snapshot) == Manifest && writer.ToBinary(snapshot).AsSpan().SequenceEqual(expected),
                            $"{sample.Name}: id8 bytes/manifest changed.");
                        var buffer = new ArrayBufferWriter<byte>();
                        Require(native.Serialize(snapshot, buffer) == expected.Length && buffer.WrittenCount == expected.Length,
                            $"{sample.Name}: wrong native byte count.");
                        Require(buffer.WrittenSpan.SequenceEqual(expected), $"{sample.Name}: native bytes changed.");
                        Require(Same((SnapshotEnvelope)native.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), Manifest), snapshot),
                            $"{sample.Name}: native read differs.");
                        Require(Same((SnapshotEnvelope)legacy.FromBinary(buffer.WrittenSpan.ToArray(), typeof(SnapshotEnvelope)), snapshot),
                            $"{sample.Name}: legacy cannot read native output.");
                        Require(Same((SnapshotEnvelope)native.Deserialize(Split(expected), Manifest), snapshot),
                            $"{sample.Name}: native segmented read differs.");
                        segmentedReads++;
                        cases++;
                        nativeWrites++;
                    }
                    foreach (var payload in new object[] { new UserV1("legacy-user"), new UserV2("native-user") })
                    {
                        var snapshot = new SnapshotEnvelope(payload);
                        var bytes = legacy.ToBinary(snapshot);
                        var buffer = new ArrayBufferWriter<byte>();
                        native.Serialize(snapshot, buffer);
                        Require(buffer.WrittenSpan.SequenceEqual(bytes), "User payload wrapper changed bytes.");
                        Require(Same((SnapshotEnvelope)native.Deserialize(new ReadOnlySequence<byte>(bytes), Manifest), snapshot),
                            "Registered user payload did not roundtrip.");
                        userPayloads++;
                    }
                    Reject(() => system.Serialization.Deserialize(Array.Empty<byte>(), 8, "unknown"));
                    Reject(() => system.Serialization.Deserialize(Array.Empty<byte>(), int.MaxValue, Manifest));
                    var untouched = new ArrayBufferWriter<byte>();
                    Reject(() => native.Serialize(new SnapshotEnvelope(new UnregisteredPayload()), untouched));
                    Require(untouched.WrittenCount == 0, "Unsupported native user payload partially wrote its envelope.");
                    rejected += 3;
                    watchdog.ThrowIfAnyProblems(spelling, "serialization");
                    Console.WriteLine($"[snapshot-native] spelling={spelling} writer=8 fixtures=5 user-payloads=2");
                }
                finally
                {
                    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                }
                watchdog.ThrowIfAnyProblems(spelling, "shutdown");
            }
            Require(cases == 15 && nativeWrites == 15 && segmentedReads == 15 && userPayloads == 6 && rejected == 9,
                $"Incomplete corpus: {cases}/{nativeWrites}/{userPayloads}/{rejected}.");
            Console.WriteLine("[snapshot-native] OK cases=15 native-writes=15 segmented-reads=15 user-payloads=6 rejected=9");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[snapshot-native] FAILED: {exception}");
            return 1;
        }
    }
    static ReadOnlySequence<byte> Split(byte[] bytes)
    {
        var offset = bytes.Length / 2;
        var first = new Segment(bytes.AsMemory(0, offset));
        var last = first.Append(bytes.AsMemory(offset));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }
    static bool Same(SnapshotEnvelope actual, SnapshotEnvelope expected) => expected.Data is byte[] bytes
        ? actual.Data is byte[] other && bytes.AsSpan().SequenceEqual(other) : Equals(actual.Data, expected.Data);
    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (SerializationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected rejection did not occur.");
    }
    sealed class UnregisteredPayload
    {
    }
    sealed record UserV1(string Value);
    sealed record UserV2(string Value);
    sealed class UserV1Serializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => 9722;
        public override string Manifest(object obj) => "u1";
        public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((UserV1)obj).Value);
        public override object FromBinary(byte[] bytes, string manifest) => manifest == "u1"
            ? new UserV1(Encoding.UTF8.GetString(bytes)) : throw new SerializationException("Unknown user manifest.");
    }
    sealed class UserV2Serializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 9723;
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
}
