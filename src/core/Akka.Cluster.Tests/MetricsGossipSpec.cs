using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Remoting;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class MetricsGossipSpec : MetricsCollectorFactory
    {
        public ActorRef Self { get { return TestActor; } }

        private IMetricsCollector _collector;

        public MetricsGossipSpec()
        {
            _collector = CreateMetricsCollector();
        }

        [Fact]
        public void MetricsGossip_must_add_new_NodeMetrics()
        {
            var m1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2555), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);

            Assert.True(m1.Metrics.Count > 3);
            Assert.True(m2.Metrics.Count > 3);

            var g1 = MetricsGossip.Empty + m1;
            g1.Nodes.Count.ShouldBe(1);
            g1.NodeMetricsFor(m1.Address).Metrics.ShouldBe(m1.Metrics);

            var g2 = g1 + m2;
            g2.Nodes.Count.ShouldBe(2);
            g2.NodeMetricsFor(m1.Address).Metrics.ShouldBe(m1.Metrics);
            g2.NodeMetricsFor(m2.Address).Metrics.ShouldBe(m2.Metrics);
        }

        [Fact]
        public void MetricsGossip_must_merge_peer_metrics()
        {
            var m1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2555), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);

            var g1 = MetricsGossip.Empty + m1 + m2;
            g1.Nodes.Count.ShouldBe(2);
            var beforeMergeNodes = g1.Nodes;

            var m2Updated = m2.Copy(metrics: _collector.Sample().Metrics, timestamp: m2.Timestamp + 1000);
            var g2 = g1 + m2Updated; //merge peers
            g2.Nodes.Count.ShouldBe(2);
            g2.NodeMetricsFor(m1.Address).Metrics.ShouldBe(m1.Metrics);
            g2.NodeMetricsFor(m2.Address).Metrics.ShouldBe(m2.Metrics);
            foreach (var peer in g2.Nodes)
            {
                if(peer.Address == m2.Address)
                    peer.Timestamp.ShouldBe(m2Updated.Timestamp);
            }
        }

        [Fact]
        public void MetricsGossip_must_merge_an_existing_metric_set_for_a_node_and_update_node_ring()
        {
            var m1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2555), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m3 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2556), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m2Updated = m2.Copy(metrics: _collector.Sample().Metrics, timestamp: m2.Timestamp + 1000);

            var g1 = MetricsGossip.Empty + m1 + m2;
            var g2 = MetricsGossip.Empty + m3 + m2Updated;
            Assert.True(g1.Nodes.All(x => x.Address == m1.Address || x.Address == m2.Address));

            //should contain nodes 1,3 and the most recent version of 2
            var mergedGossip = g1.Merge(g2);
            XunitAssertions.Equivalent(mergedGossip.Nodes.Select(x => x.Address),
                new[] {m1.Address, m2.Address, m3.Address});
            mergedGossip.NodeMetricsFor(m1.Address).Metrics.ShouldBe(m1.Metrics);
            mergedGossip.NodeMetricsFor(m2.Address).Metrics.ShouldBe(m2Updated.Metrics);
            mergedGossip.NodeMetricsFor(m3.Address).Metrics.ShouldBe(m3.Metrics);
            Assert.True(mergedGossip.Nodes.All(x => x.Metrics.Count > 3));
            mergedGossip.NodeMetricsFor(m2.Address).Timestamp.ShouldBe(m2Updated.Timestamp);
        }

        [Fact]
        public void MetricsGossip_must_get_the_current_NodeMetrics_if_it_exists_in_local_nodes()
        {
            var m1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), StandardMetrics.NewTimestamp(),
               _collector.Sample().Metrics);
            var g1 = MetricsGossip.Empty + m1;
            g1.NodeMetricsFor(m1.Address).Metrics.ShouldBe(m1.Metrics);
        }

        [Fact]
        public void MetricsGossip_must_remove_a_node_if_it_is_no_longer_up()
        {
            var m1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2555), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);

            var g1 = MetricsGossip.Empty + m1 + m2;
            g1.Nodes.Count.ShouldBe(2);
            var g2 = g1.Remove(m1.Address);
            g2.Nodes.Count.ShouldBe(1);
            g2.Nodes.Any(x => x.Address == m1.Address).ShouldBeFalse();
            g2.NodeMetricsFor(m1.Address).ShouldBe(null);
            g2.NodeMetricsFor(m2.Address).Metrics.ShouldBe(m2.Metrics);
        }

        [Fact]
        public void MetricsGossip_must_filter_nodes()
        {
            var m1 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2554), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);
            var m2 = new NodeMetrics(new Address("akka.tcp", "sys", "a", 2555), StandardMetrics.NewTimestamp(),
                _collector.Sample().Metrics);

            var g1 = MetricsGossip.Empty + m1 + m2;
            g1.Nodes.Count.ShouldBe(2);
            var g2 = g1.Filter(ImmutableHashSet.Create<Address>(new[] {m2.Address}));
            g2.Nodes.Count.ShouldBe(1);
            g2.Nodes.Any(x => x.Address == m1.Address).ShouldBeFalse();
            g2.NodeMetricsFor(m1.Address).ShouldBe(null);
            g2.NodeMetricsFor(m2.Address).Metrics.ShouldBe(m2.Metrics);
        }
    }
}
