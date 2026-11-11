//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsMessagePackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Metrics.Helpers;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Util;

namespace Akka.Cluster.Metrics.Serialization;

/// <summary>Generated counterpart of id 10. Only the global V2 switch moves its default write bindings.</summary>
internal sealed class ClusterMetricsMessagePackSerializer : SerializerV2
{
    internal const int SerializerId = 50;
    private readonly ClusterMetricsMessagePackCodec _codec;

    public ClusterMetricsMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ClusterMetricsMessagePackCodec(system);
    }

    public override int Identifier => SerializerId;

    public override string Manifest(object obj) => obj switch
    {
        MetricsGossipEnvelope => "a",
        AdaptiveLoadBalancingPool => "b",
        MixMetricsSelector => "c",
        CpuMetricsSelector => "d",
        MemoryMetricsSelector => "e",
        _ => throw new ArgumentException($"Unsupported metrics message [{obj.GetType()}].", nameof(obj))
    };

    public override byte[] ToBinary(object obj)
    {
        if (obj is CpuMetricsSelector or MemoryMetricsSelector)
            return [0x80];

        // Prepare the graph once; asking the wrapper for a size hint would build it again.
        var wire = ToWire(obj);
        using var output = new PooledPayloadWriter();
        _codec.Serialize(wire, output);
        return output.WrittenSpan.ToArray();
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        if (obj is CpuMetricsSelector or MemoryMetricsSelector)
        {
            writer.GetSpan(1)[0] = 0x80;
            writer.Advance(1);
            return 1;
        }

        return _codec.Serialize(ToWire(obj), writer);
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (bytes is { Length: 1 } && bytes[0] == 0x80 && ReadSelector(manifest) is { } selector)
            return selector;

        return Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (bytes.IsSingleSegment && bytes.FirstSpan.Length == 1 && bytes.FirstSpan[0] == 0x80
            && ReadSelector(manifest) is { } selector)
            return selector;

        return _codec.Deserialize(bytes, manifest) switch
        {
            MetricsGossipWire gossip => new MetricsGossipEnvelope(gossip.From,
                new MetricsGossip(gossip.Nodes.Select(FromWire).ToImmutableHashSet()), gossip.Reply),
            MetricsPoolWire pool => new AdaptiveLoadBalancingPool((IMetricsSelector)pool.Selector,
                pool.Instances, null, pool.Dispatcher, pool.UsePoolDispatcher),
            MetricsMixWire mix => FromWire(mix),
            MetricsCpuWire => CpuMetricsSelector.Instance,
            MetricsMemoryWire => MemoryMetricsSelector.Instance,
            var wire => throw new SerializationException($"Unknown metrics wire message [{wire.GetType()}].")
        };
    }

    private static IMetricsSelector? ReadSelector(string manifest) => manifest switch
    {
        "d" => CpuMetricsSelector.Instance,
        "e" => MemoryMetricsSelector.Instance,
        _ => null
    };

    private static IMetricsWireMessage ToWire(object obj) => obj switch
    {
        MetricsGossipEnvelope gossip => new MetricsGossipWire(gossip.FromAddress, gossip.Reply,
            gossip.Gossip.Nodes.OrderBy(n => n.Address.ToString(), StringComparer.Ordinal).Select(ToWire).ToArray()),
        // Object fields use the actor system's registered serializer, including user selector overrides.
        // Do not create a second private Serialization instance for these nested envelopes.
        AdaptiveLoadBalancingPool pool => new MetricsPoolWire(pool.MetricsSelector, pool.NrOfInstances,
            pool.RouterDispatcher, pool.UsePoolDispatcher),
        MixMetricsSelector mix => new MetricsMixWire(ToWire(mix)),
        CpuMetricsSelector => new MetricsCpuWire(),
        MemoryMetricsSelector => new MetricsMemoryWire(),
        _ => throw new ArgumentException($"Unsupported metrics message [{obj.GetType()}].", nameof(obj))
    };

    private static object[] ToWire(MixMetricsSelector mix)
    {
        var selectors = mix.Selectors;
        var wire = new object[selectors.Length];
        for (var i = 0; i < selectors.Length; i++)
            wire[i] = selectors[i];
        return wire;
    }

    private static MixMetricsSelector FromWire(MetricsMixWire mix)
    {
        var selectors = ImmutableArray.CreateBuilder<CapacityMetricsSelector>(mix.Selectors.Length);
        foreach (var selector in mix.Selectors)
            selectors.Add((CapacityMetricsSelector)selector);
        return new MixMetricsSelector(selectors.MoveToImmutable());
    }

    private static MetricsNodeWire ToWire(NodeMetrics node) => new(node.Address, node.Timestamp,
        node.Metrics.OrderBy(m => m.Name, StringComparer.Ordinal).Select(m => new MetricsValueWire(m.Name,
            (int)m.Value.Type, m.Value.Type is AnyNumber.NumberType.Int or AnyNumber.NumberType.Long ? m.Value.LongValue : 0,
            m.Value.Type is AnyNumber.NumberType.Float or AnyNumber.NumberType.Double ? m.Value.DoubleValue : 0,
            m.Average.HasValue ? new MetricsAverageWire(m.Average.Value.Value, m.Average.Value.Alpha) : null)).ToArray());

    private static NodeMetrics FromWire(MetricsNodeWire node) => new(node.Address, node.Timestamp,
        node.Metrics.Select(m => new NodeMetrics.Types.Metric(m.Name, FromWire(m),
            m.Average is { } average ? new NodeMetrics.Types.EWMA(average.Value, average.Alpha)
                : Option<NodeMetrics.Types.EWMA>.None)));

    private static AnyNumber FromWire(MetricsValueWire metric) => (AnyNumber.NumberType)metric.Kind switch
    {
        AnyNumber.NumberType.Int => new AnyNumber(checked((int)metric.Integer)),
        AnyNumber.NumberType.Long => new AnyNumber(metric.Integer),
        AnyNumber.NumberType.Float => new AnyNumber((float)metric.Floating),
        AnyNumber.NumberType.Double => new AnyNumber(metric.Floating),
        _ => throw new SerializationException($"Unknown metrics number kind [{metric.Kind}].")
    };
}

internal interface IMetricsWireMessage;

[AkkaSerializer<IMetricsWireMessage>("akka-cluster-metrics-v2-codec", ClusterMetricsMessagePackSerializer.SerializerId)]
[AkkaSerializerFormatter<Address, AddressFormatter>]
internal sealed partial class ClusterMetricsMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "a")]
internal sealed record MetricsGossipWire([property: AkkaField(1)] Address From,
    [property: AkkaField(2)] bool Reply, [property: AkkaField(3)] MetricsNodeWire[] Nodes) : IMetricsWireMessage;

[AkkaSerializable(Manifest = "b")]
internal sealed record MetricsPoolWire([property: AkkaField(1)] object Selector,
    [property: AkkaField(2)] int Instances, [property: AkkaField(3)] string Dispatcher,
    [property: AkkaField(4)] bool UsePoolDispatcher) : IMetricsWireMessage;

[AkkaSerializable(Manifest = "c")]
internal sealed record MetricsMixWire([property: AkkaField(1)] object[] Selectors) : IMetricsWireMessage;

[AkkaSerializable(Manifest = "d", AllowEmpty = true)]
internal sealed record MetricsCpuWire : IMetricsWireMessage;

[AkkaSerializable(Manifest = "e", AllowEmpty = true)]
internal sealed record MetricsMemoryWire : IMetricsWireMessage;

[AkkaSerializable]
internal sealed record MetricsNodeWire([property: AkkaField(1)] Address Address,
    [property: AkkaField(2)] long Timestamp, [property: AkkaField(3)] MetricsValueWire[] Metrics);

[AkkaSerializable]
internal sealed record MetricsValueWire([property: AkkaField(1)] string Name,
    [property: AkkaField(2)] int Kind, [property: AkkaField(3)] long Integer,
    [property: AkkaField(4)] double Floating, [property: AkkaField(5)] MetricsAverageWire? Average);

[AkkaSerializable]
internal sealed record MetricsAverageWire([property: AkkaField(1)] double Value, [property: AkkaField(2)] double Alpha);
