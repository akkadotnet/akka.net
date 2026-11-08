// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Akka.Cluster.Benchmarks.Serialization;

/// <summary>Managed codec comparison; generic map construction is deliberately outside the Native AOT subset.</summary>
[Config(typeof(MicroBenchmarkConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReplicatedDataMessagePackBenchmarks
{
    [Params("Counter", "ObservedSet", "NestedMap", "MapDelta")]
    public string Shape { get; set; } = "Counter";

    private ExtendedActorSystem _off = null!;
    private ExtendedActorSystem _on = null!;
    private object _value = null!;
    private Serializer _legacy = null!;
    private SerializerV1Adapter _adapter = null!;
    private SerializerV2 _native = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _nativeBytes = null!;
    private string _manifest = null!;
    private readonly ArrayBufferWriter<byte> _writer = new();

    [GlobalSetup]
    public void Setup()
    {
        _off = (ExtendedActorSystem)ActorSystem.Create("crdt-benchmark", ConfigurationFactory.ParseString("akka.actor.serialization-v2 = off"));
        _on = (ExtendedActorSystem)ActorSystem.Create("crdt-benchmark", ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on"));
        var nodes = Enumerable.Range(0, 4).Select(i => new UniqueAddress(
            new Address("akka", "crdt-benchmark", $"node-{i}.example", 2552 + i), (1L << 40) + i)).ToArray();
        var vector = nodes.Aggregate(VersionVector.Empty, (v, node) => v.Merge(VersionVector.Create(node, 100)));
        var dots = Enumerable.Range(0, 32).ToImmutableDictionary(i => $"key-{i:D2}",
            i => VersionVector.Create(nodes[i % nodes.Length], i + 1));
        var keys = new ORSet<string>(dots, vector);
        var counter = nodes.Aggregate(GCounter.Empty, (c, node) => c.Increment(node, 123456789));
        var values = dots.Keys.ToImmutableDictionary(key => key, _ => counter);
        var map = new ORDictionary<string, GCounter>(keys, values);
        var added = new ORSet<string>.AddDeltaOperation(keys);
        var delta = new ORDictionary<string, GCounter>.DeltaGroup(
            new ORDictionary<string, GCounter>.IDeltaOperation[]
            {
                new ORDictionary<string, GCounter>.UpdateDeltaOperation(added,
                    values.ToImmutableDictionary(kv => kv.Key, kv => (IReplicatedData)kv.Value))
            });
        _value = Shape switch
        {
            "Counter" => counter,
            "ObservedSet" => keys,
            "NestedMap" => map,
            "MapDelta" => delta,
            _ => throw new ArgumentOutOfRangeException(nameof(Shape))
        };
        _legacy = _off.Serialization.FindSerializerFor(_value);
        _native = _on.Serialization.FindSerializerFor(_value) as SerializerV2
            ?? throw new InvalidOperationException("The on binding did not select a native V2 codec.");
        if (_legacy.Identifier != 11 || _native.Identifier != 51 || _native is SerializerV1Adapter
            || _off.Serialization.FindSerializerFor(counter).Identifier != 11
            || _on.Serialization.FindSerializerFor(counter).Identifier != 51)
            throw new InvalidOperationException("Expected actual old/new module bindings, including nested counters.");
        _adapter = new SerializerV1Adapter(_off, _legacy);
        _manifest = _adapter.Manifest(_value);
        if (_native.Manifest(_value) != _manifest)
            throw new InvalidOperationException("Manifest mismatch.");
        _legacyBytes = _legacy.ToBinary(_value);
        _nativeBytes = _native.ToBinary(_value);
        Validate(_legacy.FromBinary(_legacyBytes, _manifest));
        Validate(_native.FromBinary(_nativeBytes, _manifest));
        Validate(_adapter.Deserialize(new ReadOnlySequence<byte>(_legacyBytes), _manifest));
        Validate(_native.Deserialize(new ReadOnlySequence<byte>(_nativeBytes), _manifest));
        ValidateBuffer(_adapter, _legacyBytes);
        ValidateBuffer(_native, _nativeBytes);
        Console.WriteLine($"[crdt-wire] {Shape}: legacy={_legacyBytes.Length}B generated={_nativeBytes.Length}B; ids=11/51");
    }

    private void Validate(object decoded)
    {
        // Legacy re-encoding compares retained causal state as well as user-visible values.
        if (decoded.GetType() != _value.GetType() || !decoded.Equals(_value)
            || !_legacy.ToBinary(decoded).AsSpan().SequenceEqual(_legacyBytes))
            throw new InvalidOperationException($"Lost value or causal state for {Shape}.");
    }

    private void ValidateBuffer(SerializerV2 codec, byte[] expected)
    {
        _writer.Clear();
        var count = codec.Serialize(_value, _writer);
        if (count != _writer.WrittenCount || !_writer.WrittenSpan.SequenceEqual(expected))
            throw new InvalidOperationException("Buffer output/count differs from byte-array output.");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("ArrayWrite")]
    public byte[] LegacyArrayWrite() => _legacy.ToBinary(_value);

    [Benchmark, BenchmarkCategory("ArrayWrite")]
    public byte[] GeneratedArrayWrite() => _native.ToBinary(_value);

    [Benchmark(Baseline = true), BenchmarkCategory("BufferWrite")]
    public int LegacyAdapterBufferWrite()
    {
        _writer.Clear();
        return _adapter.Serialize(_value, _writer);
    }

    [Benchmark, BenchmarkCategory("BufferWrite")]
    public int GeneratedBufferWrite()
    {
        _writer.Clear();
        return _native.Serialize(_value, _writer);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("SequenceRead")]
    public object LegacyAdapterRead() => _adapter.Deserialize(new ReadOnlySequence<byte>(_legacyBytes), _manifest);

    [Benchmark, BenchmarkCategory("SequenceRead")]
    public object GeneratedRead() => _native.Deserialize(new ReadOnlySequence<byte>(_nativeBytes), _manifest);

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _off.Terminate();
        await _on.Terminate();
    }
}
