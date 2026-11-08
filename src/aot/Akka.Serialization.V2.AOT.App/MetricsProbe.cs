// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Cluster.Metrics;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.Util;

using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Event;

namespace Akka.AOT.SerializationV2;

internal static class MetricsProbe
{
    internal static async Task<int> RunAsync()
    {
        Require(!RuntimeFeature.IsDynamicCodeSupported, "Native executable required");
        Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var enabled) && !enabled, "Dynamic loading must be disabled");
        var address = new Address("akka", "metrics", "a", 2552);
        var metrics = new[]
        {
            new NodeMetrics.Types.Metric("int", int.MaxValue, Option<NodeMetrics.Types.EWMA>.None),
            new NodeMetrics.Types.Metric("long", long.MaxValue, new NodeMetrics.Types.EWMA(100, 0.18)),
            new NodeMetrics.Types.Metric("float", float.MaxValue, Option<NodeMetrics.Types.EWMA>.None),
            new NodeMetrics.Types.Metric("double", double.MaxValue, Option<NodeMetrics.Types.EWMA>.None)
        };
        object[] corpus =
        [
            new MetricsGossipEnvelope(address, new MetricsGossip(ImmutableHashSet.Create(new NodeMetrics(address, 4711, metrics))), true),
            new AdaptiveLoadBalancingPool(),
            new AdaptiveLoadBalancingPool(CpuMetricsSelector.Instance, 3),
            new AdaptiveLoadBalancingPool(MemoryMetricsSelector.Instance, 7, routerDispatcher: "custom", usePoolDispatcher: true),
            MixMetricsSelector.Instance,
            new MixMetricsSelector(ImmutableArray<CapacityMetricsSelector>.Empty),
            CpuMetricsSelector.Instance,
            MemoryMetricsSelector.Instance
        ];
        string[] manifests = ["a", "b", "b", "b", "c", "c", "d", "e"];
        var cases = 0;
        var nativeWrites = 0;
        foreach (var mode in new[] { "off", "on", "pinned" })
        {
            var pin = mode == "pinned" ? """
                akka.actor.serialization-bindings {
                  "Akka.Cluster.Metrics.Serialization.MetricsGossipEnvelope, Akka.Cluster.Metrics" = akka-cluster-metrics
                  "Akka.Cluster.Metrics.AdaptiveLoadBalancingPool, Akka.Cluster.Metrics" = akka-cluster-metrics
                  "Akka.Cluster.Metrics.MixMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
                  "Akka.Cluster.Metrics.CpuMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
                  "Akka.Cluster.Metrics.MemoryMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
                }
                """ : "";
            var config = ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(mode == "off" ? "off" : "on")}\n{pin}")
                .WithFallback(ClusterMetrics.DefaultConfig());
            var watchdog = new LogWatchdogFilter();
            var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new LogFilterSetup([watchdog]));
            var system = ActorSystem.Create("metrics-native-" + mode, setup);
            try
            {
                watchdog.ThrowIfAnyProblems(mode, "startup");
                for (var i = 0; i < corpus.Length; i++)
                {
                    var message = corpus[i];
                    var writer = system.Serialization.FindSerializerFor(message);
                    Require(writer.Identifier == (mode == "on" ? 50 : 10), "Writer selection");
                    var bytes = writer.ToBinary(message);
                    var restored = system.Serialization.Deserialize(bytes, writer.Identifier, manifests[i]);
                    Compare(message, restored);
                    if (mode == "on")
                    {
                        var v2 = writer as SerializerV2 ?? throw new InvalidOperationException("Metrics id50 requires native buffer writes.");
                        var buffer = new ArrayBufferWriter<byte>();
                        Require(v2.Serialize(message, buffer) == buffer.WrittenCount, "Written count");
                        Require(buffer.WrittenSpan.SequenceEqual(bytes), "Native writer bytes");
                        Compare(message, v2.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), manifests[i]));
                        nativeWrites++;
                    }
                    cases++;
                }
            }
            finally
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            }
            watchdog.ThrowIfAnyProblems(mode, "shutdown");
        }
        Require(cases == 24 && nativeWrites == 8, "Complete corpus including all native writes");
        Console.WriteLine("[metrics-native] OK cases=24 native-writes=8");
        return cases;
    }
    static void Compare(object expected, object actual)
    {
        Require(actual.GetType() == expected.GetType(), "Runtime type");
        switch (expected)
        {
            case MetricsGossipEnvelope gossip:
                var restored = (MetricsGossipEnvelope)actual;
                Require(restored.FromAddress.Equals(gossip.FromAddress) && restored.Reply == gossip.Reply, "Gossip envelope");
                Require(restored.Gossip.Nodes.Count == gossip.Gossip.Nodes.Count, "Node count");
                foreach (var node in gossip.Gossip.Nodes)
                {
                    var found = restored.Gossip.NodeMetricsFor(node.Address).Value;
                    Require(found.Timestamp == node.Timestamp && found.Metrics.Count == node.Metrics.Count, "Node fields");
                    foreach (var metric in node.Metrics)
                    {
                        var value = found.Metrics.Single(m => m.Name == metric.Name);
                        Require(value.Value.Type == metric.Value.Type, "Numeric kind");
                        Require(metric.Value.Type is AnyNumber.NumberType.Int or AnyNumber.NumberType.Long
                            ? value.Value.LongValue == metric.Value.LongValue
                            : value.Value.DoubleValue == metric.Value.DoubleValue, "Numeric value");
                        Require(value.Average.HasValue == metric.Average.HasValue, "EWMA presence");
                        if (metric.Average.HasValue)
                            Require(value.Average.Value.Value == metric.Average.Value.Value && value.Average.Value.Alpha == metric.Average.Value.Alpha, "EWMA fields");
                    }
                }
                break;
            case AdaptiveLoadBalancingPool pool:
                var p = (AdaptiveLoadBalancingPool)actual;
                Require(p.NrOfInstances == pool.NrOfInstances && p.RouterDispatcher == pool.RouterDispatcher && p.UsePoolDispatcher == pool.UsePoolDispatcher, "Pool fields");
                Compare(pool.MetricsSelector, p.MetricsSelector);
                break;
            case MixMetricsSelector mix:
                var other = (MixMetricsSelector)actual;
                Require(other.Selectors.Length == mix.Selectors.Length, "Selector count");
                for (var i = 0; i < mix.Selectors.Length; i++) Compare(mix.Selectors[i], other.Selectors[i]);
                break;
            case CpuMetricsSelector:
                Require(ReferenceEquals(actual, CpuMetricsSelector.Instance), "CPU identity");
                break;
            case MemoryMetricsSelector:
                Require(ReferenceEquals(actual, MemoryMetricsSelector.Instance), "Memory identity");
                break;
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
