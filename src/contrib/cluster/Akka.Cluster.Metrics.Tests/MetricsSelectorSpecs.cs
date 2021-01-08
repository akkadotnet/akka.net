//-----------------------------------------------------------------------
// <copyright file="MetricsSelectorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Cluster.Metrics.Serialization;
using Akka.Util.Extensions;
using FluentAssertions;
using Xunit;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Tests
{
    public class MetricsSelectorSpecs
    {
        // TODO: read from reference.conf
        private const double Factor = 0.3;
        private const double DecayFactor = 0.18;

        private readonly Address _a1 = new Address("akka", "sys", "a1", 2551);
        private readonly Address _b1 = new Address("akka", "sys", "b1", 2551);
        private readonly Address _c1 = new Address("akka", "sys", "c1", 2551);
        private readonly Address _d1 = new Address("akka", "sys", "d1", 2551);
        
        private readonly CapacityMetricsSelector _abstractSelector = new AbstractSelector();
        private readonly IImmutableSet<NodeMetrics> _nodeMetrics;

        public MetricsSelectorSpecs()
        {
            var nodeMetricsA = new NodeMetrics(_a1, DateTime.UtcNow.ToTimestamp(), new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, 128, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, 256, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MaxMemoryRecommended, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuTotalUsage, 0.2, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuProcessUsage, 0.1, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 8, DecayFactor).Value,
            });
            
            var nodeMetricsB = new NodeMetrics(_b1, DateTime.UtcNow.ToTimestamp(), new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, 256, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MaxMemoryRecommended, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuTotalUsage, 0.4, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuProcessUsage, 0.2, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 16, DecayFactor).Value,
            });
            
            var nodeMetricsC = new NodeMetrics(_c1, DateTime.UtcNow.ToTimestamp(), new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MaxMemoryRecommended, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuTotalUsage, 0.6, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuProcessUsage, 0.3, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 16, DecayFactor).Value,
            });
            
            var nodeMetricsD = new NodeMetrics(_d1, DateTime.UtcNow.ToTimestamp(), new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, 511, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.MaxMemoryRecommended, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 2, DecayFactor).Value,
            });
            
            _nodeMetrics = ImmutableHashSet.Create(nodeMetricsA, nodeMetricsB, nodeMetricsC, nodeMetricsD);
        }

        [Fact]
        public void CapacityMetricsSelector_Should_calculate_weights_from_capacity()
        {
            var capacity = new Dictionary<Address, double>()
            {
                
                [_a1] = 0.6,
                [_b1] = 0.3,
                [_c1] = 0.1
                
            }.ToImmutableDictionary();
            var weights = _abstractSelector.Weights(capacity);
            weights.Should().BeEquivalentTo(new Dictionary<Address, int>()
            {
                [_c1] = 1,
                [_b1] = 3,
                [_a1] = 6
            });
        }

        [Fact]
        public void CapacityMetricsSelector_Should_handle_low_and_zero_capacity()
        {
            var capacity = new Dictionary<Address, double>()
            {
                
                [_a1] = 0.0,
                [_b1] = 1.0,
                [_c1] = 0.005,
                [_d1] = 0.004
                
            }.ToImmutableDictionary();
            var weights = _abstractSelector.Weights(capacity);
            weights.Should().BeEquivalentTo(new Dictionary<Address, int>()
            {
                [_a1] = 0,
                [_b1] = 100,
                [_c1] = 1,
                [_d1] = 0
            });
        }

        [Fact]
        public void HeapMetricsSelector_Should_calculate_capacity_of_heap_metrics()
        {
            var capacity = MemoryMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately(0.75, 0.0001);
            capacity[_b1].Should().BeApproximately(0.75, 0.0001);
            capacity[_c1].Should().BeApproximately(0.0, 0.0001);
            capacity[(_d1)].Should().BeApproximately(0.001953125, 0.0001);
        }
        
        [Fact]
        public void CpuMetricsSelector_Should_calculate_capacity_of_cpuCombined_metrics()
        {
            var capacity = CpuMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately(1.0 - 0.2, 0.0001);
            capacity[_b1].Should().BeApproximately(1.0 - 0.4, 0.0001);
            capacity[_c1].Should().BeApproximately(1.0 - 0.6, 0.0001);
            capacity.Should().NotContain(pair => pair.Key.Equals(_d1));
        }
        
        [Fact]
        public void MixMetricsSelector_Should_aggregate_capacity_of_all_metrics()
        {
            var capacity = MixMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately((0.75 + 0.8) / 2, 0.0001);
            capacity[_b1].Should().BeApproximately((0.75 + 0.6 ) / 2, 0.0001);
            capacity[_c1].Should().BeApproximately((0.0 + 0.4) / 2, 0.0001);
            capacity[_d1].Should().BeApproximately((0.001953125) / 1, 0.0001);
        }
        
        private class AbstractSelector : CapacityMetricsSelector
        {
            /// <inheritdoc />
            public override IImmutableDictionary<Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
            {
                return ImmutableDictionary<Address, double>.Empty;
            }
        }
    }
}
