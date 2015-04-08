using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class MetricValuesSpec : MetricsCollectorFactory
    {
        private readonly IMetricsCollector _collector;

        private NodeMetrics _node1;
        private NodeMetrics _node2;

        public MetricValuesSpec()
        {
            _collector = CreateMetricsCollector();
            _node1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), 1, _collector.Sample().Metrics);
            _node2 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2555), 1, _collector.Sample().Metrics);
        }

        public ImmutableHashSet<NodeMetrics> Nodes
        {
            get
            {
                return Enumerable.Range(1, 100).Aggregate(ImmutableHashSet.Create<NodeMetrics>(_node1, _node2),
                    (nodes, i) => nodes.Select(n =>
                        new NodeMetrics(n.Address, n.Timestamp,
                            _collector.Sample().Metrics.Select(latest =>
                            {
                                var streaming = n.Metrics.First(latest.Equals);
                                return streaming + latest;
                            }).ToImmutableHashSet()
                    )).ToImmutableHashSet());
            }
        }

        [Fact]
        public void NodeMetrics_MetricValues_must_extract_expected_metrics_for_load_balancing()
        {
            var stream1 = Convert.ToInt64(_node1.Metric(StandardMetrics.SystemMemoryMax).Value);
            var stream2 = Convert.ToInt64(_node2.Metric(StandardMetrics.SystemMemoryAvailable).Value);
            Assert.True(stream1 >= stream2);
        }

        [Fact]
        public void NodeMetrics_MetricValues_must_extract_expected_MetricValue_types_for_load_balancing()
        {
            foreach (var node in Nodes)
            {
                var memory = StandardMetrics.SystemMemory.ExtractSystemMemory(node);
                Assert.True(memory.Used > 0L);
                Assert.True(memory.Available <= memory.Max);
                Assert.True(memory.Used <= memory.Max);

                var cpu = StandardMetrics.Cpu.ExtractCpu(node);
                Assert.True(cpu.Cores > 0);
                if (cpu.SystemLoadAverageMeasurement.HasValue)
                    Assert.True(cpu.SystemLoadAverageMeasurement.Value >= 0.0d);
                if (cpu.CpuCombinedMeasurement.HasValue)
                {
                    Assert.True(cpu.CpuCombinedMeasurement.Value <= 1.0d);
                    Assert.True(cpu.CpuCombinedMeasurement.Value >= 0.0d);
                }
            }
        }
    }
}
