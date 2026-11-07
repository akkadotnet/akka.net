//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Metrics;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Compares actual off/on module bindings, including the nested selector bindings each mode selects.</summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class ClusterMetricsSerializerBenchmarks
{
    private ActorSystem _legacySystem = null!;
    private ActorSystem _v2System = null!;
    private Serializer _legacy = null!;
    private Serializer _v2 = null!;
    private SerializerV2 _legacyWriter = null!;
    private SerializerV2 _nativeWriter = null!;
    private object _message = null!;
    private string _manifest = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _v2Bytes = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new(4096);

    [Params("Gossip", "PoolDefault", "PoolMix", "Selector")]
    public string Kind { get; set; } = "Gossip";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = ActorSystem.Create("metrics-bench-off", Settings(false));
        _v2System = ActorSystem.Create("metrics-bench-on", Settings(true));
        _message = Kind switch
        {
            "Gossip" => CreateGossip(),
            "PoolDefault" => new AdaptiveLoadBalancingPool(),
            "PoolMix" => new AdaptiveLoadBalancingPool(new MixMetricsSelector(ImmutableArray.Create<CapacityMetricsSelector>(
                CpuMetricsSelector.Instance, MemoryMetricsSelector.Instance)), 7, routerDispatcher: "custom", usePoolDispatcher: true),
            "Selector" => CpuMetricsSelector.Instance,
            _ => throw new InvalidOperationException($"Unknown benchmark kind [{Kind}].")
        };
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _v2 = _v2System.Serialization.FindSerializerFor(_message);
        if (_legacy.Identifier != 10 || _v2.Identifier != 50)
            throw new InvalidOperationException("The benchmark must compare real legacy and V2 bindings.");
        _legacyWriter = new SerializerV1Adapter((ExtendedActorSystem)_legacySystem, _legacy);
        _nativeWriter = (SerializerV2)_v2;
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        _legacyBytes = _legacy.ToBinary(_message);
        _v2Bytes = _v2.ToBinary(_message);
        Console.WriteLine($"[metrics-wire] {Kind}: legacy={_legacyBytes.Length} V2={_v2Bytes.Length}");
    }

    private static Config Settings(bool enabled) => ConfigurationFactory.ParseString(
        $"akka.loglevel = OFF\nakka.actor.serialization-v2 = {(enabled ? "on" : "off")}")
        .WithFallback(ClusterMetrics.DefaultConfig());

    private static MetricsGossipEnvelope CreateGossip()
    {
        var nodes = Enumerable.Range(1, 3).Select(i => new NodeMetrics(new Address("akka", "metrics", "host" + i, 2552),
            4711 + i, new[]
            {
                new NodeMetrics.Types.Metric("cpu", 0.5, new NodeMetrics.Types.EWMA(0.4, 0.18)),
                new NodeMetrics.Types.Metric("memory", 123456789L, Option<NodeMetrics.Types.EWMA>.None),
                new NodeMetrics.Types.Metric("cores", 8, Option<NodeMetrics.Types.EWMA>.None),
                new NodeMetrics.Types.Metric("load", 0.75f, Option<NodeMetrics.Types.EWMA>.None)
            })).ToImmutableHashSet();
        return new MetricsGossipEnvelope(new Address("akka", "metrics", "host1", 2552), new MetricsGossip(nodes), true);
    }

    [Benchmark]
    public byte[] LegacyToBinary() => _legacy.ToBinary(_message);

    [Benchmark]
    public byte[] V2ToBinary() => _v2.ToBinary(_message);

    [Benchmark]
    public object LegacyFromBinary() => _legacy.FromBinary(_legacyBytes, _manifest);

    [Benchmark]
    public object V2FromBinary() => _v2.FromBinary(_v2Bytes, _manifest);

    [Benchmark]
    public int LegacyToWriter()
    {
        _buffer.Clear();
        return _legacyWriter.Serialize(_message, _buffer);
    }

    [Benchmark]
    public int V2ToWriter()
    {
        _buffer.Clear();
        return _nativeWriter.Serialize(_message, _buffer);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await Task.WhenAll(_legacySystem.Terminate(), _v2System.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
