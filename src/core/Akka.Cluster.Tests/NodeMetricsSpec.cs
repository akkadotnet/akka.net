//-----------------------------------------------------------------------
// <copyright file="NodeMetricsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class NodeMetricsSpec : AkkaSpec
    {
        Address _node1 = new Address("akka.tcp", "sys", "a", 2554);
        Address _node2 = new Address("akka.tcp", "sys", "a", 2555);

        [Fact]
        public void NodeMetrics_must_return_correct_result_for_2_same_nodes()
        {
            new NodeMetrics(_node1, 1).Equals(new NodeMetrics(_node1, 2)).ShouldBeTrue();
        }

        [Fact]
        public void NodeMetrics_must_return_correct_result_for_2_NOT_same_nodes()
        {
            new NodeMetrics(_node1, 1).Equals(new NodeMetrics(_node2, 2)).ShouldBeFalse();
        }

        [Fact]
        public void NodeMetrics_must_merge_2_NodeMetrics_by_most_recent()
        {
            var sample1 = new NodeMetrics(_node1, 1,
                ImmutableHashSet.Create<Metric>(Metric.Create("a", 10), Metric.Create("b", 20)));
            var sample2 = new NodeMetrics(_node1, 2, ImmutableHashSet.Create<Metric>(Metric.Create("a", 11), Metric.Create("c", 30)));
            var merged = sample1.Merge(sample2);
            merged.Timestamp.ShouldBe(sample2.Timestamp);
            merged.Metric("a").Value.ShouldBe(11);
            merged.Metric("b").Value.ShouldBe(20);
            merged.Metric("c").Value.ShouldBe(30);
        }

        [Fact]
        public void NodeMetrics_must_not_merge_2_NodeMetrics_if_master_is_more_recent()
        {
            var sample1 = new NodeMetrics(_node1, 1,
                 ImmutableHashSet.Create<Metric>(Metric.Create("a", 10), Metric.Create("b", 20)));
            var sample2 = new NodeMetrics(_node1, 0, ImmutableHashSet.Create<Metric>(Metric.Create("a", 11), Metric.Create("c", 30)));
            var merged = sample1.Merge(sample2); //older and not the same
            merged.Timestamp.ShouldBe(sample1.Timestamp);
            merged.Metrics.ShouldBe(sample1.Metrics);
        }
    }
}

