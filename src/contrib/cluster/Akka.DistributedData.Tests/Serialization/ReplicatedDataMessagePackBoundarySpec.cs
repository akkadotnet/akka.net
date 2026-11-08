// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using MessagePack;
using Xunit;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ReplicatedDataMessagePackBoundarySpec : AkkaSpec
{
    private const string Switch = "Akka.DynamicTypeLoading";

    public ReplicatedDataMessagePackBoundarySpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Should_LeaveCallerOutputUntouched_When_KnownRegisterPayloadFailsAfterWriting")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_LeaveCallerOutputUntouched_When_KnownRegisterPayloadFailsAfterWriting(bool dynamicLoading)
    {
        var system = CreateSystem();
        InitializeLogger(system);
        var hadSwitch = AppContext.TryGetSwitch(Switch, out var previous);
        try
        {
            var payload = (PayloadSerializer)system.Serialization.GetSerializerById(777);
            payload.FailWrite = true;
            var codec = new ReplicatedDataMessagePackSerializer((ExtendedActorSystem)system);
            var value = new LWWRegister<string>(Owner(system), "value", 17);
            var writer = new ArrayBufferWriter<byte>();
            writer.Write(new byte[] { 0xaa, 0xbb });
            var before = writer.WrittenSpan.ToArray();
            var transport = Akka.Serialization.Serialization.CurrentTransportInformation;
            AppContext.SetSwitch(Switch, dynamicLoading);

            var error = Assert.Throws<SerializationException>(() => codec.Serialize(value, writer));

            Assert.Contains("late payload write", error.Message);
            Assert.Equal(1, payload.Writes);
            Assert.Equal(before, writer.WrittenSpan.ToArray());
            Assert.Same(transport, Akka.Serialization.Serialization.CurrentTransportInformation);
        }
        finally
        {
            AppContext.SetSwitch(Switch, !hadSwitch || previous);
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory(DisplayName = "Should_RejectUnsupportedGenericBeforePayloadCallbacks_When_ColdOrWarmReadersSeePayloadFirst")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RejectUnsupportedGenericBeforePayloadCallbacks_When_ColdOrWarmReadersSeePayloadFirst(bool warm)
    {
        var system = CreateSystem();
        var sender = CreateSystem();
        InitializeLogger(system);
        InitializeLogger(sender);
        var hadSwitch = AppContext.TryGetSwitch(Switch, out var previous);
        try
        {
            AppContext.SetSwitch(Switch, true);
            var payload = (PayloadSerializer)system.Serialization.GetSerializerById(777);
            var value = new LWWRegister<PayloadValue>(Owner(system), new PayloadValue("value"), 17);
            var codec = new ReplicatedDataMessagePackSerializer((ExtendedActorSystem)system);
            var bytes = new ReplicatedDataMessagePackSerializer((ExtendedActorSystem)sender).ToBinary(value);
            if (warm)
            {
                Assert.True(value.Equals(codec.FromBinary(bytes, "E")));
                Assert.True(value.Equals(system.Serialization.Deserialize(bytes, 51, "E")));
                codec.ToBinary(value);
            }
            bytes = PayloadFirst(bytes);
            payload.FailRead = true;
            payload.FailWrite = true;
            payload.Reads = 0;
            payload.Writes = 0;
            AppContext.SetSwitch(Switch, false);
            if (!warm)
                codec = new ReplicatedDataMessagePackSerializer((ExtendedActorSystem)system);

            Assert.Contains(Switch, Assert.Throws<SerializationException>(() => codec.FromBinary(bytes, "E")).Message);
            Assert.Contains(Switch, Assert.Throws<SerializationException>(() => system.Serialization.Deserialize(bytes, 51, "E")).Message);
            var writer = new ArrayBufferWriter<byte>();
            Assert.Contains(Switch, Assert.Throws<SerializationException>(() => codec.Serialize(value, writer)).Message);
            Assert.Contains(Switch, Assert.Throws<SerializationException>(() => codec.ToBinary(value)).Message);
            Assert.Equal(0, writer.WrittenCount);
            Assert.Equal(0, payload.Reads);
            Assert.Equal(0, payload.Writes);
        }
        finally
        {
            AppContext.SetSwitch(Switch, !hadSwitch || previous);
            await Task.WhenAll(system.Terminate(), sender.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static ActorSystem CreateSystem()
    {
        var config = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on")
            .WithFallback(ReplicatorMessagePackSerializerSpec.PortConfig);
        var serializers = SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
            "boundary-payload", new PayloadSerializer(system), ImmutableHashSet.Create(typeof(string), typeof(PayloadValue)))));
        return ActorSystem.Create("crdt-boundary", ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), serializers));
    }

    private static UniqueAddress Owner(ActorSystem system) => new(new Address(
        ((ExtendedActorSystem)system).Provider.DefaultAddress.Protocol, system.Name, "counter.example", 2552), 42);

    private static byte[] PayloadFirst(byte[] bytes)
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
        foreach (var field in fields.OrderBy(field => field.Key == 2 ? 0 : 1))
        {
            writer.Write(field.Key);
            writer.WriteRaw(field.Bytes);
        }
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private sealed record PayloadValue(string Value);

    private sealed class PayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public bool FailWrite { get; set; }
        public bool FailRead { get; set; }
        public int Writes { get; set; }
        public int Reads { get; set; }
        public override int Identifier => 777;
        public override string Manifest(object obj) => obj is string ? "s" : "p";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            Writes++;
            var bytes = Encoding.UTF8.GetBytes(obj is string text ? text : ((PayloadValue)obj).Value);
            writer.Write(bytes);
            if (FailWrite)
                throw new SerializationException("Intentional late payload write failure.");
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            Reads++;
            if (FailRead)
                throw new SerializationException("A guarded generic payload decoder must not run.");
            var text = Encoding.UTF8.GetString(bytes.ToArray());
            return manifest == "s" ? text : new PayloadValue(text);
        }
    }
}
