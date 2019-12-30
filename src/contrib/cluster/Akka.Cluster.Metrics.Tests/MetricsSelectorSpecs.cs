// //-----------------------------------------------------------------------
// // <copyright file="MetricsSelectorSpecs.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Cluster.Metrics.Serialization;
using Akka.Util;
using Akka.Util.Extensions;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
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
            var nodeMetricsA = new NodeMetrics(_a1, DateTime.UtcNow.ToTimestamp().Seconds, new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryUsed, 128, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryCommitted, 256, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryMax, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuCombinedName, 0.2, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuStolenName, 0.1, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.SystemLoadAverageName, 0.5, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 8, DecayFactor).Value,
            });
            
            var nodeMetricsB = new NodeMetrics(_b1, DateTime.UtcNow.ToTimestamp().Seconds, new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryUsed, 256, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryCommitted, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryMax, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuCombinedName, 0.4, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuStolenName, 0.2, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.SystemLoadAverageName, 1.0, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 16, DecayFactor).Value,
            });
            
            var nodeMetricsC = new NodeMetrics(_c1, DateTime.UtcNow.ToTimestamp().Seconds, new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryUsed, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryCommitted, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryMax, 1024, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuCombinedName, 0.6, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.CpuStolenName, 0.3, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.SystemLoadAverageName, 16.0, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, 16, DecayFactor).Value,
            });
            
            var nodeMetricsD = new NodeMetrics(_d1, DateTime.UtcNow.ToTimestamp().Seconds, new[]
            {
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryUsed, 511, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryCommitted, 512, DecayFactor).Value,
                NodeMetrics.Types.Metric.Create(StandardMetrics.HeapMemoryMax, 512, DecayFactor).Value,
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
            var capacity = HeapMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately(0.75, 0.0001);
            capacity[_b1].Should().BeApproximately(0.75, 0.0001);
            capacity[_c1].Should().BeApproximately(0.0, 0.0001);
            capacity[(_d1)].Should().BeApproximately(0.001953125, 0.0001);
        }
        
        [Fact]
        public void CpuMetricsSelector_Should_calculate_capacity_of_cpuCombined_metrics()
        {
            var capacity = CpuMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately(1.0 - 0.2 - 0.1 * (1.0 + Factor), 0.0001);
            capacity[_b1].Should().BeApproximately(1.0 - 0.4 - 0.2 * (1.0 + Factor), 0.0001);
            capacity[_c1].Should().BeApproximately(1.0 - 0.6 - 0.3 * (1.0 + Factor), 0.0001);
            capacity.Should().NotContain(pair => pair.Key.Equals(_d1));
        }
        
        [Fact]
        public void SystemLoadAverageMetricsSelector_Should_calculate_capacity_of_systemLoadAverage_metrics()
        {
            var capacity = SystemLoadAverageMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately(0.9375, 0.0001);
            capacity[_b1].Should().BeApproximately(0.9375, 0.0001);
            capacity[_c1].Should().BeApproximately(0.0, 0.0001);
            capacity.Should().NotContain(pair => pair.Key.Equals(_d1));
        }
        
        [Fact]
        public void MixMetricsSelector_Should_aggregate_capacity_of_all_metrics()
        {
            var capacity = MixMetricsSelector.Instance.Capacity(_nodeMetrics);
            capacity[_a1].Should().BeApproximately((0.75 + 0.67 + 0.9375) / 3, 0.0001);
            capacity[_b1].Should().BeApproximately((0.75 + 0.34 + 0.9375) / 3, 0.0001);
            capacity[_c1].Should().BeApproximately((0.0 + 0.01 + 0.0) / 3, 0.0001);
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