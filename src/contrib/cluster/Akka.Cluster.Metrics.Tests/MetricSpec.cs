//-----------------------------------------------------------------------
// <copyright file="MetricSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Cluster.Metrics.Tests.Base;
using Akka.Cluster.Metrics.Tests.Helpers;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Tests
{
    public class MetricSpec
    {
        [Fact]
        public void AnyNumber_conversion_should_be_valid()
        {
            NodeMetrics.Types.Metric.ConvertNumber(0).IsLeft.Should().BeTrue();
            NodeMetrics.Types.Metric.ConvertNumber(1).ToLeft().Value.Should().Be(1);
            NodeMetrics.Types.Metric.ConvertNumber(1L).IsLeft.Should().BeTrue();
            NodeMetrics.Types.Metric.ConvertNumber(0.0).IsRight.Should().BeTrue();
        }

        [Fact]
        public void New_metric_creation_should_work()
        {
            var metric = NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, 256L, decayFactor: 0.18).Value;
            metric.Name.Should().Be(StandardMetrics.MemoryUsed);
            metric.Value.LongValue.Should().Be(256L);
            metric.IsSmooth.Should().BeTrue();
            metric.SmoothValue.Should().BeApproximately(256, 0.0001);
        }

        [Fact]
        public void New_metric_with_undefined_value_Should_be_None()
        {
            NodeMetrics.Types.Metric.Create("x", -1, Option<double>.None).HasValue.Should().BeFalse();
            NodeMetrics.Types.Metric.Create("x", new Try<AnyNumber>(new Exception()), Option<double>.None).HasValue.Should().BeFalse();
        }

        [Fact]
        public void Should_recognize_defined_metric()
        {
            NodeMetrics.Types.Metric.Defined(0).Should().BeTrue();
            NodeMetrics.Types.Metric.Defined(0.0).Should().BeTrue();
        }
        
        [Fact]
        public void Should_recognize_not_defined_metric()
        {
            NodeMetrics.Types.Metric.Defined(-1).Should().BeFalse();
            NodeMetrics.Types.Metric.Defined(-1.0).Should().BeFalse();
        }
    }
    
    public class NodeMetricSpec
    {
        private readonly Address _node1 = new Address("akka", "sys", "a", 2554);
        private readonly Address _node2 = new Address("akka", "sys", "a", 2555);

        [Fact]
        public void NodeMetrics_Should_return_correct_result_for_2_same_nodes()
        {
            new NodeMetrics(_node1, 0, new NodeMetrics.Types.Metric[0])
                .SameAs(new NodeMetrics(_node1, 0, new NodeMetrics.Types.Metric[0]))
                .Should().BeTrue();
        }
        
        [Fact]
        public void NodeMetrics_Should_return_correct_result_for_2_not_same_nodes()
        {
            new NodeMetrics(_node1, 0, new NodeMetrics.Types.Metric[0])
                .SameAs(new NodeMetrics(_node2, 0, new NodeMetrics.Types.Metric[0]))
                .Should().BeFalse();
        }

        [Fact]
        public void NodeMetrics_should_merge_2_nodeMetrics_by_most_resent()
        {
            var sample1 = new NodeMetrics(_node1, 1, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 10, Option<double>.None).Value,
                NodeMetrics.Types.Metric.Create("b", 20, Option<double>.None).Value
            ));
            var sample2 = new NodeMetrics(_node1, 2, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 11, Option<double>.None).Value,
                NodeMetrics.Types.Metric.Create("c", 30, Option<double>.None).Value
            ));

            var merged = sample1.Merge(sample2);
            merged.Timestamp.Should().Be(sample2.Timestamp);
            merged.Metric("a").Value.Value.LongValue.Should().Be(11);
            merged.Metric("b").Value.Value.LongValue.Should().Be(20);
            merged.Metric("c").Value.Value.LongValue.Should().Be(30);
        }

        [Fact]
        public void NodeMetrics_should_not_merge_2_nodeMetrics_if_master_is_more_resent()
        {
            var sample1 = new NodeMetrics(_node1, 1, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 10, Option<double>.None).Value,
                NodeMetrics.Types.Metric.Create("b", 20, Option<double>.None).Value
            ));
            var sample2 = new NodeMetrics(_node1, 0, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 11, Option<double>.None).Value,
                NodeMetrics.Types.Metric.Create("c", 30, Option<double>.None).Value
            ));
            
            var merged = sample1.Merge(sample2);
            merged.Timestamp.Should().Be(sample1.Timestamp);
            merged.Metrics.ShouldBeEquivalentTo(sample1.Metrics);
        }

        [Fact]
        public void NodeMetrics_should_update_2_nodeMetrics_by_most_resent()
        {
            var sample1 = new NodeMetrics(_node1, 1, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 10, Option<double>.None).Value,
                NodeMetrics.Types.Metric.Create("b", 20, Option<double>.None).Value
            ));
            var sample2 = new NodeMetrics(_node1, 2, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 11, Option<double>.None).Value,
                NodeMetrics.Types.Metric.Create("c", 30, Option<double>.None).Value
            ));

            var updated = sample1.Update(sample2);
            updated.Metrics.Should().HaveCount(3);
            updated.Timestamp.Should().Be(sample2.Timestamp);
            updated.Metric("a").Value.Value.LongValue.Should().Be(11);
            updated.Metric("b").Value.Value.LongValue.Should().Be(20);
            updated.Metric("c").Value.Value.LongValue.Should().Be(30);
        }

        [Fact]
        public void NodeMetric_should_update_3_nodeMetrics_with_ewma_applied()
        {
            const double decay = ClusterMetricsTestConfig.DefaultDecayFactor;
            const double epsilon = 0.001;
            
            var sample1 = new NodeMetrics(_node1, 1, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 1, decay).Value,
                NodeMetrics.Types.Metric.Create("b", 4, decay).Value
            ));
            var sample2 = new NodeMetrics(_node1, 2, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 2, decay).Value,
                NodeMetrics.Types.Metric.Create("c", 5, decay).Value
            ));
            var sample3 = new NodeMetrics(_node1, 3, ImmutableHashSet.Create(
                NodeMetrics.Types.Metric.Create("a", 3, decay).Value,
                NodeMetrics.Types.Metric.Create("d", 6, decay).Value
            ));

            var updated = sample1.Update(sample2).Update(sample3);

            updated.Metrics.Should().HaveCount(4);
            updated.Timestamp.Should().Be(sample3.Timestamp);

            updated.Metric("a").Value.Value.LongValue.Should().Be(3);
            updated.Metric("b").Value.Value.LongValue.Should().Be(4);
            updated.Metric("c").Value.Value.LongValue.Should().Be(5);
            updated.Metric("d").Value.Value.LongValue.Should().Be(6);

            updated.Metric("a").Value.SmoothValue.Should().BeApproximately(1.512, epsilon);
            updated.Metric("b").Value.SmoothValue.Should().BeApproximately(4.000, epsilon);
            updated.Metric("c").Value.SmoothValue.Should().BeApproximately(5.000, epsilon);
            updated.Metric("d").Value.SmoothValue.Should().BeApproximately(6.000, epsilon);
        }
    }

    public class MetricGossipSpec : AkkaSpecWithCollector
    {
        private long NewTimestamp => DateTime.UtcNow.ToTimestamp();

        public MetricGossipSpec() : base(ClusterMetricsTestConfig.DefaultEnabled)
        {
        }

        [Fact]
        public void MetricGossip_should_add_new_nodeMetrics()
        {
            var m1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), NewTimestamp, Collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka", "sys", "a", 2555), NewTimestamp, Collector.Sample().Metrics);

            m1.Metrics.Count.Should().BeGreaterThan(3);
            m2.Metrics.Count.Should().BeGreaterThan(3);

            var g1 = MetricsGossip.Empty + m1;
            g1.Nodes.Should().HaveCount(1);
            g1.NodeMetricsFor(m1.Address).Value.Metrics.ShouldBeEquivalentTo(m1.Metrics);

            var g2 = g1 + m2;
            g2.Nodes.Should().HaveCount(2);
            g2.NodeMetricsFor(m1.Address).Value.Metrics.ShouldBeEquivalentTo(m1.Metrics);
            g2.NodeMetricsFor(m2.Address).Value.Metrics.ShouldBeEquivalentTo(m2.Metrics);
        }

        [Fact]
        public void MetricGossip_should_merge_peer_metrics()
        {
            var m1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), NewTimestamp, Collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka", "sys", "a", 2555), NewTimestamp, Collector.Sample().Metrics);

            var g1 = MetricsGossip.Empty + m1 + m2;
            g1.Nodes.Should().HaveCount(2);
            
            var m2Updated = new NodeMetrics(m2.Address, m2.Timestamp + 1000, NewSample(m2.Metrics));
            var g2 = g1 + m2Updated; // merge peers
            g2.Nodes.Should().HaveCount(2);
            g2.NodeMetricsFor(m1.Address).Value.Metrics.Should().BeEquivalentTo(m1.Metrics);
            g2.NodeMetricsFor(m2.Address).Value.Metrics.Should().BeEquivalentTo(m2Updated.Metrics);
            g2.Nodes.Where(p => p.Address.Equals(m2.Address)).ForEach(p => p.Timestamp.Should().Be(m2Updated.Timestamp));
        }

        [Fact]
        public void MetricGossip_Should_merge_an_existing_metric_set_for_a_node_and_update_node_ring()
        {
            var m1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), NewTimestamp, Collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka", "sys", "a", 2555), NewTimestamp, Collector.Sample().Metrics);
            var m3 = new NodeMetrics(new Address("akka", "sys", "a", 2556), NewTimestamp, Collector.Sample().Metrics);
            var m2Updated = new NodeMetrics(m2.Address, m2.Timestamp + 1000, NewSample(m2.Metrics));

            var g1 = MetricsGossip.Empty + m1 + m2;
            var g2 = MetricsGossip.Empty + m3 + m2Updated;
            
            g1.Nodes.Select(n => n.Address).Should().BeEquivalentTo(m1.Address, m2.Address);
            
            // should contain nodes 1,3, and the most recent version of 2
            var mergedGossip = g1.Merge(g2);
            mergedGossip.Nodes.Select(n => n.Address).Should().BeEquivalentTo(m1.Address, m2.Address, m3.Address);
            mergedGossip.NodeMetricsFor(m1.Address).Value.Metrics.Should().BeEquivalentTo(m1.Metrics);
            mergedGossip.NodeMetricsFor(m2.Address).Value.Metrics.Should().BeEquivalentTo(m2Updated.Metrics);
            mergedGossip.NodeMetricsFor(m3.Address).Value.Metrics.Should().BeEquivalentTo(m3.Metrics);
            mergedGossip.Nodes.ForEach(n => n.Metrics.Count.Should().BeGreaterThan(3));
            mergedGossip.NodeMetricsFor(m2.Address).Value.Timestamp.Should().Be(m2Updated.Timestamp);
        }

        [Fact]
        public void MetricGossip_should_get_the_current_NodeMetrics_if_it_exists_in_the_local_nodes()
        {
            var m1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), NewTimestamp, Collector.Sample().Metrics);
            var g1 = MetricsGossip.Empty + m1;
            g1.NodeMetricsFor(m1.Address).Value.Metrics.ShouldBeEquivalentTo(m1.Metrics);
        }

        [Fact]
        public void MetricGossip_should_remove_a_node_if_it_is_no_longer_up()
        {
            var m1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), NewTimestamp, Collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka", "sys", "a", 2555), NewTimestamp, Collector.Sample().Metrics);

            var g1 = MetricsGossip.Empty + m1 + m2;
            g1.Nodes.Should().HaveCount(2);
            var g2 = g1.Remove(m1.Address);
            g2.Nodes.Should().HaveCount(1);
            g2.Nodes.Should().NotContain(n => n.Address.Equals(m1.Address));
            g2.NodeMetricsFor(m1.Address).Should().Be(Option<NodeMetrics>.None);
            g2.NodeMetricsFor(m2.Address).Value.Metrics.ShouldBeEquivalentTo(m2.Metrics);
        }

        [Fact]
        public void MetricGossip_should_filter_nodes()
        {
            var m1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), NewTimestamp, Collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka", "sys", "a", 2555), NewTimestamp, Collector.Sample().Metrics);

            var g1 = MetricsGossip.Empty + m1 + m2;
            g1.Nodes.Should().HaveCount(2);
            var g2 = g1.Filter(ImmutableHashSet.Create(m2.Address));
            g2.Nodes.Should().HaveCount(1);
            g2.Nodes.Should().NotContain(n => n.Address.Equals(m1.Address));
            g2.NodeMetricsFor(m1.Address).Should().Be(Option<NodeMetrics>.None);
            g2.NodeMetricsFor(m2.Address).Value.Metrics.ShouldBeEquivalentTo(m2.Metrics);
        }

        /// <summary>
        /// Sometimes collector may not be able to return a valid value (None and such) so must ensure they
        /// have the same Metric types
        /// </summary>
        private IEnumerable<NodeMetrics.Types.Metric> NewSample(ICollection<NodeMetrics.Types.Metric> previousSample)
        {
            // Metric.Equals is based on name equality
            return Collector.Sample().Metrics.Where(previousSample.Contains).Concat(previousSample).Distinct();
        }
    }

    public class MetricValuesSpec : AkkaSpecWithCollector
    {
        private readonly NodeMetrics _node1;
        private readonly NodeMetrics _node2;
        private readonly IImmutableList<NodeMetrics> _nodes;
        
        public MetricValuesSpec() : base(ClusterMetricsTestConfig.DefaultEnabled)
        {
            _node1 = new NodeMetrics(new Address("akka", "sys", "a", 2554), 1, Collector.Sample().Metrics);
            _node2 = new NodeMetrics(new Address("akka", "sys", "a", 2555), 1, Collector.Sample().Metrics);
            _nodes = Enumerable.Range(1, 100).Aggregate(ImmutableList.Create(_node1, _node2), (nodes, _) =>
            {
                return nodes.Select(n =>
                {
                    return new NodeMetrics(n.Address, n.Timestamp, metrics: Collector.Sample().Metrics.SelectMany(latest =>
                    {
                        return n.Metrics.Where(latest.SameAs).Select(streaming => streaming + latest);
                    }));
                }).ToImmutableList();
            });
        }

        [Fact]
        public void NodeMetrics_MetricValues_should_extract_expected_metrics_for_load_balancing()
        {
            var stream1 = _node2.Metric(StandardMetrics.MemoryAvailable).Value.Value.LongValue;
            var stream2 = _node1.Metric(StandardMetrics.MemoryUsed).Value.Value.LongValue;
            stream1.Should().BeGreaterOrEqualTo(stream2);
        }

        [Fact]
        public void NodeMetrics_MetricValues_should_extract_expected_MetricValue_types_for_load_balancing()
        {
            foreach (var node in _nodes)
            {
                var heapMemory = StandardMetrics.Memory.Decompose(node);
                if (heapMemory.HasValue)
                {
                    heapMemory.Value.UsedSmoothValue.Should().BeGreaterThan(0, "Memory actual usage should be collected");
                    heapMemory.Value.AvailableSmoothValue.Should().BeGreaterOrEqualTo(0, "Available memory should be collected");
                }

                var cpu = StandardMetrics.Cpu.Decompose(node);
                if (cpu.HasValue)
                {
                    cpu.Value.Processors.Should().BeGreaterThan(0);
                    
                    cpu.Value.CpuProcessUsage.Should().BeInRange(0, 1, "CPU process usage should be between 0% and 100%");
                    cpu.Value.CpuTotalUsage.Should().BeInRange(0, 1, "Total tracked CPU usage should be between 0% and 100%");
                }
            }
        }
    }
}
