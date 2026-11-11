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
        Require(Akka.Serialization.Serialization.ManifestFor(_v2, _message) == _manifest, "manifest parity");
        _legacyBytes = LegacyToBinary();
        _v2Bytes = V2ToBinary();
        ValidateMessage(LegacyFromBinary(), "legacy timed reader");
        ValidateMessage(V2FromBinary(), "V2 timed reader");
        foreach (var system in new[] { _legacySystem, _v2System })
        {
            ValidateMessage(system.Serialization.Deserialize(_legacyBytes, 10, _manifest), "registered legacy reader");
            ValidateMessage(system.Serialization.Deserialize(_v2Bytes, 50, _manifest), "registered V2 reader");
        }
        ValidateWriter(LegacyToWriter(), _legacyBytes, "legacy timed writer");
        ValidateWriter(V2ToWriter(), _v2Bytes, "V2 timed writer");
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

    private void ValidateWriter(int count, byte[] expected, string contract)
    {
        Require(count == _buffer.WrittenCount, $"{contract}: reported byte count");
        Require(_buffer.WrittenSpan.SequenceEqual(expected), $"{contract}: array/buffer byte parity");
    }

    private void ValidateMessage(object decoded, string contract)
    {
        Require(decoded.GetType() == _message.GetType(), $"{contract}: root runtime type");
        switch (_message)
        {
            case MetricsGossipEnvelope expected:
                ValidateGossip(expected, (MetricsGossipEnvelope)decoded, contract);
                break;
            case AdaptiveLoadBalancingPool expected:
                var actual = (AdaptiveLoadBalancingPool)decoded;
                Require(actual.NrOfInstances == expected.NrOfInstances
                    && actual.RouterDispatcher == expected.RouterDispatcher
                    && actual.UsePoolDispatcher == expected.UsePoolDispatcher
                    && actual.Resizer is null
                    && actual.SupervisorStrategy.Equals(expected.SupervisorStrategy), $"{contract}: pool fields");
                ValidateSelector(expected.MetricsSelector, actual.MetricsSelector, contract);
                break;
            case CpuMetricsSelector:
                Require(ReferenceEquals(decoded, CpuMetricsSelector.Instance), $"{contract}: CPU singleton");
                break;
            default:
                throw new InvalidOperationException($"No benchmark validator for [{Kind}].");
        }
    }

    private static void ValidateSelector(IMetricsSelector expected, IMetricsSelector actual, string contract)
    {
        Require(actual.GetType() == expected.GetType(), $"{contract}: selector runtime type");
        if (expected is MixMetricsSelector expectedMix)
        {
            var actualMix = (MixMetricsSelector)actual;
            Require(actualMix.Selectors.Length == expectedMix.Selectors.Length, $"{contract}: selector count");
            for (var i = 0; i < expectedMix.Selectors.Length; i++)
                ValidateSelector(expectedMix.Selectors[i], actualMix.Selectors[i], $"{contract}: selector[{i}]");
        }
        else
        {
            Require(ReferenceEquals(actual, expected), $"{contract}: selector singleton");
        }
    }

    private static void ValidateGossip(MetricsGossipEnvelope expected, MetricsGossipEnvelope actual, string contract)
    {
        Require(actual.FromAddress.Equals(expected.FromAddress) && actual.Reply == expected.Reply,
            $"{contract}: gossip sender/reply");
        Require(actual.Gossip.Nodes.Count == expected.Gossip.Nodes.Count, $"{contract}: node count");
        foreach (var expectedNode in expected.Gossip.Nodes)
        {
            var actualNode = actual.Gossip.Nodes.Single(node => node.Address.Equals(expectedNode.Address));
            Require(actualNode.Timestamp == expectedNode.Timestamp, $"{contract}: node timestamp");
            Require(actualNode.Metrics.Count == expectedNode.Metrics.Count, $"{contract}: metric count");
            foreach (var expectedMetric in expectedNode.Metrics)
            {
                var actualMetric = actualNode.Metrics.Single(metric => metric.Name == expectedMetric.Name);
                Require(actualMetric.Value.Type == expectedMetric.Value.Type, $"{contract}: metric numeric kind");
                Require(expectedMetric.Value.Type is AnyNumber.NumberType.Int or AnyNumber.NumberType.Long
                    ? actualMetric.Value.LongValue == expectedMetric.Value.LongValue
                    : actualMetric.Value.DoubleValue == expectedMetric.Value.DoubleValue, $"{contract}: metric value");
                Require(actualMetric.Average.HasValue == expectedMetric.Average.HasValue, $"{contract}: EWMA presence");
                if (expectedMetric.Average.HasValue)
                    Require(actualMetric.Average.Value.Value == expectedMetric.Average.Value.Value
                        && actualMetric.Average.Value.Alpha == expectedMetric.Average.Value.Alpha, $"{contract}: EWMA state");
            }
        }
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition)
            throw new InvalidOperationException($"Metrics benchmark setup failed: {contract}.");
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
