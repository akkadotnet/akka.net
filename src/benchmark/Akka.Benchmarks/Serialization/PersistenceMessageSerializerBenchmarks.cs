//-----------------------------------------------------------------------
// <copyright file="PersistenceMessageSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
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
        _bytes = _legacy.ToBinary(_message);
        if (_native.Identifier != 7 || _native.Manifest(_message) != _manifest ||
            !_native.ToBinary(_message).AsSpan().SequenceEqual(_bytes))
            throw new InvalidOperationException("Native persistence benchmark changed id, manifest or historical bytes.");
        var back = _native.Deserialize(new ReadOnlySequence<byte>(_bytes), _manifest);
        var expected = _legacy.FromBinary(_bytes, _message.GetType());
        if (!_native.ToBinary(back).AsSpan().SequenceEqual(_legacy.ToBinary(expected)))
            throw new InvalidOperationException("Native persistence benchmark did not preserve decoded fields.");
        Console.WriteLine($"{Shape}: legacy=native={_bytes.Length}B");
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
