//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsV2SerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Metrics.Tests;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterMetricsV2SerializerSpec : V2PortSpec
{
    private readonly ITestOutputHelper _output;

    public ClusterMetricsV2SerializerSpec(ITestOutputHelper output) : base(output, ClusterMetrics.DefaultConfig())
    {
        _output = output;
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new ClusterMetricsMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new ClusterMetricsMessagePackSerializer(system);
    protected override IReadOnlyList<V2PortCase> Cases { get; } = CreateCases();

    private static IReadOnlyList<V2PortCase> CreateCases()
    {
        var cases = new List<V2PortCase>
        {
            new(CpuMetricsSelector.Instance, "d", "cpu"),
            new(MemoryMetricsSelector.Instance, "e", "memory"),
            new(MixMetricsSelector.Instance, "c", "mix"),
            new(new MixMetricsSelector(ImmutableArray<CapacityMetricsSelector>.Empty), "c", "mix-empty"),
            new(new AdaptiveLoadBalancingPool(), "b", "pool-default"),
            new(new AdaptiveLoadBalancingPool(CpuMetricsSelector.Instance, 3), "b", "pool-cpu"),
            new(new AdaptiveLoadBalancingPool(MemoryMetricsSelector.Instance, 7, routerDispatcher: "custom", usePoolDispatcher: true),
                "b", "pool-memory"),
            new(new AdaptiveLoadBalancingPool(new MixMetricsSelector(ImmutableArray.Create<CapacityMetricsSelector>(
                MemoryMetricsSelector.Instance, CpuMetricsSelector.Instance)), 4), "b", "pool-mix"),
            new(new MetricsGossipEnvelope(new Address("akka", "metrics", "a", 2552), MetricsGossip.Empty, false),
                "a", "gossip-empty")
        };
        // One metric per golden record avoids random hash-set iteration order in the unchanged legacy writer.
        foreach (var (name, value) in new (string, AnyNumber)[]
                 { ("int", int.MaxValue), ("long", long.MaxValue), ("float", float.MaxValue), ("double", double.MaxValue), ("zero", 0) })
        {
            var address = new Address("akka", "metrics", "a", 2552);
            var metric = new NodeMetrics.Types.Metric(name, value, new NodeMetrics.Types.EWMA(100, 0.18));
            cases.Add(new V2PortCase(new MetricsGossipEnvelope(address,
                new MetricsGossip(ImmutableHashSet.Create(new NodeMetrics(address, 4711, new[] { metric }))), true), "a", "gossip-" + name));
        }
        return cases;
    }

    [Fact(DisplayName = "Should_ExchangeBothFormats_When_MetricsBindingsUseOpposingGlobalSettings")]
    public async Task Should_ExchangeBothFormats_When_MetricsBindingsUseOpposingGlobalSettings()
    {
        await using var pair = MixedBindingPair.Create("metrics-mixed",
            ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ClusterMetrics.DefaultConfig()),
            ClusterMetrics.DefaultConfig());
        pair.AssertRoundTripsBothWays(Cases, 50, 10);
    }

    [Fact(DisplayName = "Should_PreserveUserPins_When_AllMetricsTypesAreExplicitlyBoundToLegacy")]
    public async Task Should_PreserveUserPins_When_AllMetricsTypesAreExplicitlyBoundToLegacy()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(ClusterMetrics.DefaultConfig());
        await using var pair = MixedBindingPair.Create("metrics-pinned", enabled,
            MixedBindingPair.BindTo("akka-cluster-metrics", typeof(MetricsGossipEnvelope), typeof(AdaptiveLoadBalancingPool),
                typeof(MixMetricsSelector), typeof(CpuMetricsSelector), typeof(MemoryMetricsSelector)).WithFallback(enabled));
        pair.AssertRoundTripsBothWays(Cases, 50, 10);
    }

    [Fact(DisplayName = "Should_PreserveTheNumericKind_When_GossipContainsAllSupportedNumbers")]
    public void Should_PreserveTheNumericKind_When_GossipContainsAllSupportedNumbers()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        foreach (var sample in Cases)
        {
            if (sample.Message is not MetricsGossipEnvelope gossip)
                continue;
            var restored = (MetricsGossipEnvelope)serializer.FromBinary(serializer.ToBinary(gossip), "a");
            AssertGossip(gossip, restored);
        }
    }

    [Fact(DisplayName = "Should_PreserveAllMetricFields_When_GossipContainsSeveralNodesAndMetrics")]
    public void Should_PreserveAllMetricFields_When_GossipContainsSeveralNodesAndMetrics()
    {
        var a = new Address("akka", "metrics", "a", 2552);
        var b = new Address("akka", "metrics", "b", 2553);
        var metrics = new[]
        {
            new NodeMetrics.Types.Metric("int", 7, Option<NodeMetrics.Types.EWMA>.None),
            new NodeMetrics.Types.Metric("long", long.MaxValue, new NodeMetrics.Types.EWMA(10.5, 0.3)),
            new NodeMetrics.Types.Metric("float", 0.5f, Option<NodeMetrics.Types.EWMA>.None),
            new NodeMetrics.Types.Metric("double", double.MaxValue, Option<NodeMetrics.Types.EWMA>.None)
        };
        var message = new MetricsGossipEnvelope(a, new MetricsGossip(ImmutableHashSet.Create(
            new NodeMetrics(a, 123, metrics), new NodeMetrics(b, 456, metrics))), true);
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        AssertGossip(message, (MetricsGossipEnvelope)serializer.FromBinary(serializer.ToBinary(message), "a"));
    }

    [Fact(DisplayName = "Should_UseTheActorSystemsSerializer_When_APoolContainsACustomSelector")]
    public async Task Should_UseTheActorSystemsSerializer_When_APoolContainsACustomSelector()
    {
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.serialization-v2 = on
            akka.actor.serializers.custom-metrics = "{{typeof(CustomSelectorSerializer).AssemblyQualifiedName}}"
            akka.actor.serialization-bindings {
                "{{typeof(CustomSelector).AssemblyQualifiedName}}" = custom-metrics
            }
            """).WithFallback(ClusterMetrics.DefaultConfig());
        await using var kit = new Akka.TestKit.Xunit.TestKit(config, "metrics-custom", _output);
        var selector = new CustomSelector(17);
        var custom = (CustomSelectorSerializer)kit.Sys.Serialization.FindSerializerFor(selector);
        var message = new AdaptiveLoadBalancingPool(selector, 3);
        var serializer = kit.Sys.Serialization.FindSerializerFor(message);
        serializer.Identifier.Should().Be(50);
        var restored = (AdaptiveLoadBalancingPool)serializer.FromBinary(serializer.ToBinary(message), "b");
        restored.MetricsSelector.Should().BeOfType<CustomSelector>().Which.Value.Should().Be(17);
        custom.Writes.Should().BeGreaterThan(0);
        custom.Reads.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Should_RejectUnknownInputs_When_AMetricsManifestOrMessageIsUnsupported")]
    public void Should_RejectUnknownInputs_When_AMetricsManifestOrMessageIsUnsupported()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Action manifest = () => serializer.Manifest(new object());
        Action write = () => serializer.ToBinary(new object());
        Action read = () => serializer.FromBinary(new byte[] { 0x80 }, "unknown");
        manifest.Should().Throw<ArgumentException>();
        write.Should().Throw<ArgumentException>();
        read.Should().Throw<SerializationException>();
    }

    private static void AssertGossip(MetricsGossipEnvelope expected, MetricsGossipEnvelope actual)
    {
        actual.FromAddress.Should().Be(expected.FromAddress);
        actual.Reply.Should().Be(expected.Reply);
        actual.Gossip.Nodes.Should().HaveCount(expected.Gossip.Nodes.Count);
        foreach (var node in expected.Gossip.Nodes)
        {
            var restored = actual.Gossip.NodeMetricsFor(node.Address).Value;
            restored.Timestamp.Should().Be(node.Timestamp);
            restored.Metrics.Should().HaveCount(node.Metrics.Count);
            foreach (var metric in node.Metrics)
            {
                var value = System.Linq.Enumerable.Single(restored.Metrics, m => m.Name == metric.Name);
                value.Value.Type.Should().Be(metric.Value.Type);
                if (metric.Value.Type is AnyNumber.NumberType.Int or AnyNumber.NumberType.Long)
                    value.Value.LongValue.Should().Be(metric.Value.LongValue);
                else
                    value.Value.DoubleValue.Should().Be(metric.Value.DoubleValue);
                value.Average.HasValue.Should().Be(metric.Average.HasValue);
                if (metric.Average.HasValue)
                {
                    value.Average.Value.Value.Should().Be(metric.Average.Value.Value);
                    value.Average.Value.Alpha.Should().Be(metric.Average.Value.Alpha);
                }
            }
        }
    }

    public sealed class CustomSelector(int value) : IMetricsSelector
    {
        public int Value { get; } = value;
        public IImmutableDictionary<Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics)
            => ImmutableDictionary<Address, int>.Empty;
    }

    public sealed class CustomSelectorSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public override int Identifier => 777;
        public override string Manifest(object obj) => "custom";
        public override byte[] ToBinary(object obj)
        {
            Writes++;
            return BitConverter.GetBytes(((CustomSelector)obj).Value);
        }
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Reads++;
            return new CustomSelector(BitConverter.ToInt32(bytes));
        }
    }
}
