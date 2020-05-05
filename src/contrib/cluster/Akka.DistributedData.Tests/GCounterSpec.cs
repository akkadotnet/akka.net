//-----------------------------------------------------------------------
// <copyright file="GCounterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;
using System.Numerics;
using Akka.Actor;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class GCounterSpec
    {
        private readonly UniqueAddress _node1 = new UniqueAddress(new Actor.Address("akka.tcp", "Sys", "localhost", 2551), 1);
        private readonly UniqueAddress _node2 = new UniqueAddress(new Actor.Address("akka.tcp", "Sys", "localhost", 2552), 2);
        private readonly UniqueAddress _node3 = new UniqueAddress(new Actor.Address("akka.tcp", "Sys", "localhost", 2553), 3);

        public GCounterSpec(ITestOutputHelper output)
        {
        }

        [Fact]
        public void GCounter_must_be_able_to_increment_each_node_record_by_one()
        {
            var c1 = new GCounter();
            var c2 = c1.Increment(_node1);
            var c3 = c2.Increment(_node1);
            var c4 = c3.Increment(_node2);
            var c5 = c4.Increment(_node2);
            var c6 = c5.Increment(_node2);

            c6.State[_node1].Should().Be(2);
            c6.State[_node2].Should().Be(3);
        }

        [Fact]
        public void GCounter_must_be_able_to_increment_each_node_record_by_arbitrary_delta()
        {
            var c1 = new GCounter();
            var c2 = c1.Increment(_node1, 3);
            var c3 = c2.Increment(_node1, 4);
            var c4 = c3.Increment(_node2, 2);
            var c5 = c4.Increment(_node2, 7);
            var c6 = c5.Increment(_node2);

            c6.State[_node1].Should().Be(7);
            c6.State[_node2].Should().Be(10);
        }

        [Fact]
        public void GCounter_must_be_able_to_summarize_the_history_to_the_correct_aggregated_value()
        {
            var c1 = new GCounter();
            var c2 = c1.Increment(_node1, 3);
            var c3 = c2.Increment(_node1, 4);
            var c4 = c3.Increment(_node2, 2);
            var c5 = c4.Increment(_node2, 7);
            var c6 = c5.Increment(_node2);

            c6.State[_node1].Should().Be(7);
            c6.State[_node2].Should().Be(10);
            c6.Value.Should().Be(17);
        }

        [Fact]
        public void GCounter_must_be_able_to_have_its_history_correctly_merged_with_another_GCounter1()
        {
            // counter 1
            var c11 = new GCounter();
            var c12 = c11.Increment(_node1, 3);
            var c13 = c12.Increment(_node1, 4);
            var c14 = c13.Increment(_node2, 2);
            var c15 = c14.Increment(_node2, 7);
            var c16 = c15.Increment(_node2);

            Assert.Equal(7UL, c16.State[_node1]);
            Assert.Equal(10UL, c16.State[_node2]);
            Assert.Equal(17UL, c16.Value);

            // counter 2
            var c21 = new GCounter();
            var c22 = c21.Increment(_node1, 2);
            var c23 = c22.Increment(_node1, 2);
            var c24 = c23.Increment(_node2, 3);
            var c25 = c24.Increment(_node2, 2);
            var c26 = c25.Increment(_node2);

            Assert.Equal(4UL, c26.State[_node1]);
            Assert.Equal(6UL, c26.State[_node2]);
            Assert.Equal(10UL, c26.Value);

            // merge both ways
            var merged1 = c16.Merge(c26);

            Assert.Equal(7UL, merged1.State[_node1]);
            Assert.Equal(10UL, merged1.State[_node2]);
            Assert.Equal(17UL, merged1.Value);

            var merged2 = c16.Merge(c26);

            Assert.Equal(7UL, merged2.State[_node1]);
            Assert.Equal(10UL, merged2.State[_node2]);
            Assert.Equal(17UL, merged2.Value);
        }

        [Fact]
        public void GCounter_must_be_able_to_have_its_history_correctly_merged_with_another_GCounter2()
        {
            // counter 1
            var c11 = new GCounter();
            var c12 = c11.Increment(_node1, 2);
            var c13 = c12.Increment(_node1, 2);
            var c14 = c13.Increment(_node2, 2);
            var c15 = c14.Increment(_node2, 7);
            var c16 = c15.Increment(_node2);

            Assert.Equal(4UL, c16.State[_node1]);
            Assert.Equal(10UL, c16.State[_node2]);
            Assert.Equal(14UL, c16.Value);

            // counter 2
            var c21 = new GCounter();
            var c22 = c21.Increment(_node1, 3);
            var c23 = c22.Increment(_node1, 4);
            var c24 = c23.Increment(_node2, 3);
            var c25 = c24.Increment(_node2, 2);
            var c26 = c25.Increment(_node2);

            Assert.Equal(7UL, c26.State[_node1]);
            Assert.Equal(6UL, c26.State[_node2]);
            Assert.Equal(13UL, c26.Value);

            // merge both ways
            var merged1 = c16.Merge(c26);

            Assert.Equal(7UL, merged1.State[_node1]);
            Assert.Equal(10UL, merged1.State[_node2]);
            Assert.Equal(17UL, merged1.Value);

            var merged2 = c16.Merge(c26);

            Assert.Equal(7UL, merged2.State[_node1]);
            Assert.Equal(10UL, merged2.State[_node2]);
            Assert.Equal(17UL, merged2.Value);
        }

        [Fact]
        public void GCounter_must_support_pruning()
        {
            var c1 = new GCounter();
            var c2 = c1.Increment(_node1);
            var c3 = c2.Increment(_node2);

            Assert.True(c2.NeedPruningFrom(_node1));
            Assert.False(c2.NeedPruningFrom(_node2));
            Assert.True(c3.NeedPruningFrom(_node1));
            Assert.True(c3.NeedPruningFrom(_node2));
            Assert.Equal(2UL, c3.Value);

            var c4 = c3.Prune(_node1, _node2);
            Assert.True(c4.NeedPruningFrom(_node2));
            Assert.False(c4.NeedPruningFrom(_node1));
            Assert.Equal(2UL, c4.Value);

            var c5 = c4.Increment(_node1).PruningCleanup(_node1);
            Assert.False(c5.NeedPruningFrom(_node1));
            Assert.Equal(2UL, c4.Value);
        }

        [Fact]
        public void GCounter_equality_must_factor_nodes()
        {
            var c1 = new GCounter();
            var c2 = c1.Increment(_node1);
            var c3 = c1.Increment(_node2);

            c2.Equals(c3).Should().BeFalse();
        }
    }
}
