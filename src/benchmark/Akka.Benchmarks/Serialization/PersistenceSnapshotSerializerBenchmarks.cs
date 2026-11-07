//-----------------------------------------------------------------------
// <copyright file="PersistenceSnapshotSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Persistence.Serialization;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Same-byte snapshot envelope: legacy adapter versus native buffers, including unchanged byte[] boundaries.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class PersistenceSnapshotSerializerBenchmarks
{
    private ExtendedActorSystem _system = null!;
    private PersistenceSnapshotSerializer _legacy = null!;
    private SerializerV1Adapter _adapter = null!;
    private SerializerV2 _native = null!;
    private Snapshot _snapshot = null!;
    private byte[] _bytes = null!;
    private string _manifest = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    [Params("String", "StringEmpty", "Bytes", "Null")]
    public string Shape { get; set; } = "String";

    [GlobalSetup]
    public void Setup()
    {
        _system = (ExtendedActorSystem)ActorSystem.Create("snapshot-serializer-bench");
        _legacy = new PersistenceSnapshotSerializer(_system);
        _adapter = new SerializerV1Adapter(_system, _legacy);
        _native = (SerializerV2)_system.Serialization.FindSerializerForType(typeof(Snapshot));
        _snapshot = Shape switch
        {
            "String" => new Snapshot(new string('x', 128)),
            "StringEmpty" => new Snapshot(string.Empty),
            "Bytes" => new Snapshot(Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray()),
            "Null" => new Snapshot(null),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _manifest = _legacy.Manifest(_snapshot);
        _bytes = _legacy.ToBinary(_snapshot);
        if (_native.Identifier != 8 || _native.Manifest(_snapshot) != _manifest ||
            !_native.ToBinary(_snapshot).AsSpan().SequenceEqual(_bytes))
            throw new InvalidOperationException("Native snapshot benchmark changed id, manifest or historical bytes.");
        var back = (Snapshot)_native.Deserialize(new ReadOnlySequence<byte>(_bytes), _manifest);
        if (_snapshot.Data is byte[] expected ? back.Data is not byte[] actual || !expected.AsSpan().SequenceEqual(actual)
            : !Equals(_snapshot.Data, back.Data))
            throw new InvalidOperationException("Native snapshot benchmark did not roundtrip its corpus.");
        Console.WriteLine($"{Shape}: legacy=native={_bytes.Length}B");
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));

    [Benchmark]
    public byte[] LegacySerialize() => _legacy.ToBinary(_snapshot);

    [Benchmark]
    public byte[] NativeSerialize() => _native.ToBinary(_snapshot);

    [Benchmark]
    public int LegacyBufferWrite() => Write(_adapter);

    [Benchmark]
    public int NativeBufferWrite() => Write(_native);

    [Benchmark]
    public object LegacyDeserialize() => _legacy.FromBinary(_bytes, typeof(Snapshot));

    [Benchmark]
    public object NativeDeserialize() => _native.Deserialize(new ReadOnlySequence<byte>(_bytes), _manifest);

    private int Write(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_snapshot, _buffer);
    }
}
