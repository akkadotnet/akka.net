//-----------------------------------------------------------------------
// <copyright file="PersistenceMessageSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Persistence;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Same-byte persistence envelopes: legacy adapter versus native fixed-shape buffers.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class PersistenceMessageSerializerBenchmarks
{
    private ExtendedActorSystem _system = null!;
    private PersistenceMessageSerializer _legacy = null!;
    private SerializerV1Adapter _adapter = null!;
    private SerializerV2 _native = null!;
    private object _message = null!;
    private byte[] _bytes = null!;
    private string _manifest = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    [Params("Persistent", "AtomicWrite", "Delivery", "StateChange")]
    public string Shape { get; set; } = "Persistent";

    [GlobalSetup]
    public void Setup()
    {
        _system = (ExtendedActorSystem)ActorSystem.Create("persistence-message-bench");
        _legacy = new PersistenceMessageSerializer(_system);
        _adapter = new SerializerV1Adapter(_system, _legacy);
        _native = (SerializerV2)_system.Serialization.FindSerializerForType(typeof(Persistent));
        _message = Shape switch
        {
            "Persistent" => new Persistent(new string('x', 128), 17, "pid", writerGuid: "writer", timestamp: 123456),
            "AtomicWrite" => new AtomicWrite(System.Collections.Immutable.ImmutableList.Create<IPersistentRepresentation>(
                new Persistent("one", 1, "pid", timestamp: 1000), new Persistent("two", 2, "pid", timestamp: 2000))),
            "Delivery" => new AtLeastOnceDeliverySnapshot(42, new[] { new UnconfirmedDelivery(41,
                ActorPath.Parse("akka.tcp://delivery-fixture@127.0.0.1:2552/user/service"), new string('x', 128)) }),
            "StateChange" => new Akka.Persistence.Fsm.PersistentFSM.StateChangeEvent("state", TimeSpan.FromSeconds(10)),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _manifest = _legacy.Manifest(_message);
        _bytes = LegacySerialize();
        if (_legacy.Identifier != 7 || _native.Identifier != 7 || _native.Manifest(_message) != _manifest ||
            !ReferenceEquals(_system.Serialization.GetSerializerById(7), _native) ||
            !ReferenceEquals(_system.Serialization.FindSerializerFor(_message), _native) ||
            !NativeSerialize().AsSpan().SequenceEqual(_bytes))
            throw new InvalidOperationException("Native persistence benchmark changed id, manifest or historical bytes.");
        ValidateMessage(LegacyDeserialize());
        ValidateMessage(NativeDeserialize());
        ValidateMessage(_system.Serialization.Deserialize(_bytes, 7, _manifest));
        ValidateWriter(LegacyBufferWrite());
        ValidateWriter(NativeBufferWrite());
        Console.WriteLine($"{Shape}: legacy=native={_bytes.Length}B");
    }

    private void ValidateWriter(int count)
    {
        Require(count == _buffer.WrittenCount && _buffer.WrittenSpan.SequenceEqual(_bytes),
            "writer byte count and historical bytes");
    }

    private void ValidateMessage(object decoded)
    {
        Require(decoded.GetType() == _message.GetType(), "root runtime type");
        switch (_message)
        {
            case Persistent expectedPersistent:
                ValidatePersistent(expectedPersistent, (Persistent)decoded);
                break;
            case AtomicWrite expectedWrite:
                var write = (AtomicWrite)decoded;
                var expectedPayload = (IImmutableList<IPersistentRepresentation>)expectedWrite.Payload;
                var payload = (IImmutableList<IPersistentRepresentation>)write.Payload;
                Require(write.Size == expectedWrite.Size && write.PersistenceId == expectedWrite.PersistenceId
                    && write.LowestSequenceNr == expectedWrite.LowestSequenceNr
                    && write.HighestSequenceNr == expectedWrite.HighestSequenceNr
                    && Equals(write.Sender, expectedWrite.Sender) && payload.Count == expectedPayload.Count,
                    "atomic write fields");
                for (var i = 0; i < expectedPayload.Count; i++)
                    ValidatePersistent(expectedPayload[i], payload[i]);
                break;
            case AtLeastOnceDeliverySnapshot expectedDelivery:
                var delivery = (AtLeastOnceDeliverySnapshot)decoded;
                Require(delivery.CurrentDeliveryId == expectedDelivery.CurrentDeliveryId
                    && delivery.UnconfirmedDeliveries.Length == expectedDelivery.UnconfirmedDeliveries.Length,
                    "delivery snapshot fields");
                for (var i = 0; i < expectedDelivery.UnconfirmedDeliveries.Length; i++)
                {
                    var expectedItem = expectedDelivery.UnconfirmedDeliveries[i];
                    var item = delivery.UnconfirmedDeliveries[i];
                    Require(item.DeliveryId == expectedItem.DeliveryId && item.Destination.Equals(expectedItem.Destination)
                        && Equals(item.Message, expectedItem.Message), "ordered unconfirmed delivery fields");
                }
                break;
            case Akka.Persistence.Fsm.PersistentFSM.StateChangeEvent expectedChange:
                var change = (Akka.Persistence.Fsm.PersistentFSM.StateChangeEvent)decoded;
                Require(change.StateIdentifier == expectedChange.StateIdentifier && change.Timeout == expectedChange.Timeout,
                    "state-change fields");
                break;
            default:
                throw new InvalidOperationException($"No persistence benchmark validator for [{Shape}].");
        }
    }

    private void ValidatePersistent(IPersistentRepresentation expected, IPersistentRepresentation actual)
    {
        // The legacy reader resolves an absent sender through the provider, rather than retaining null.
        var sender = _system.Provider.ResolveActorRef(Akka.Serialization.Serialization.SerializedActorPath(expected.Sender));
        Require(actual.SequenceNr == expected.SequenceNr && actual.PersistenceId == expected.PersistenceId
            && actual.Manifest == expected.Manifest && actual.IsDeleted == expected.IsDeleted
            && actual.Timestamp == expected.Timestamp && actual.WriterGuid == expected.WriterGuid
            && Equals(actual.Payload, expected.Payload) && Equals(actual.Sender, sender), "persistent representation fields");
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition)
            throw new InvalidOperationException($"Persistence benchmark setup failed: {contract}.");
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));

    [Benchmark]
    public byte[] LegacySerialize() => _legacy.ToBinary(_message);

    [Benchmark]
    public byte[] NativeSerialize() => _native.ToBinary(_message);

    [Benchmark]
    public int LegacyBufferWrite() => Write(_adapter);

    [Benchmark]
    public int NativeBufferWrite() => Write(_native);

    [Benchmark]
    public object LegacyDeserialize() => _legacy.FromBinary(_bytes, _message.GetType());

    [Benchmark]
    public object NativeDeserialize() => _native.Deserialize(new ReadOnlySequence<byte>(_bytes), _manifest);

    private int Write(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_message, _buffer);
    }
}
