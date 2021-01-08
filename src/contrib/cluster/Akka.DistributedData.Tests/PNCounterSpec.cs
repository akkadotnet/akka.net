//-----------------------------------------------------------------------
// <copyright file="PNCounterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class PNCounterSpec
    {
        readonly UniqueAddress _address1;
        readonly UniqueAddress _address2;

        public PNCounterSpec(ITestOutputHelper output)
        {
            _address1 = new UniqueAddress(new Actor.Address("akka.tcp", "PNCounterSpec", "some.host.org", 112), 1);
            _address2 = new UniqueAddress(new Actor.Address("akka.tcp", "PNCounterSpec2", "other.host.org", 112), 2);
        }

        [Fact]
        public void PNCounter_must_be_able_to_increment_each_nodes_record_by_one()
        {
            var c1 = new PNCounter();

            var c2 = c1.Increment(_address1);
            var c3 = c2.Increment(_address1);

            var c4 = c3.Increment(_address2);
            var c5 = c4.Increment(_address2);
            var c6 = c5.Increment(_address2);

            Assert.Equal(2UL, c6.Increments.State[_address1]);
            Assert.Equal(3UL, c6.Increments.State[_address2]);
        }

        [Fact]
        public void PNCounter_must_be_able_to_decrement_each_nodes_record_by_one()
        {
            var c1 = new PNCounter();

            var c2 = c1.Decrement(_address1);
            var c3 = c2.Decrement(_address1);

            var c4 = c3.Decrement(_address2);
            var c5 = c4.Decrement(_address2);
            var c6 = c5.Decrement(_address2);

            Assert.Equal(2UL, c6.Decrements.State[_address1]);
            Assert.Equal(3UL, c6.Decrements.State[_address2]);
        }

        [Fact]
        public void PNCounter_must_be_able_to_increment_each_nodes_record_by_arbitrary_delta()
        {
            var c1 = new PNCounter();

            var c2 = c1.Increment(_address1, 3);
            var c3 = c2.Increment(_address1, 4);
            var c4 = c3.Increment(_address2, 2);
            var c5 = c4.Increment(_address2, 7);
            var c6 = c5.Increment(_address2);

            Assert.Equal(7UL, c6.Increments.State[_address1]);
            Assert.Equal(10UL, c6.Increments.State[_address2]);
        }

        [Fact]
        public void PNCounter_must_be_able_to_decrement_each_nodes_record_by_arbitrary_delta()
        {
            var c1 = new PNCounter();

            var c2 = c1.Decrement(_address1, 3);
            var c3 = c2.Decrement(_address1, 4);
            var c4 = c3.Decrement(_address2, 2);
            var c5 = c4.Decrement(_address2, 7);
            var c6 = c5.Decrement(_address2);

            Assert.Equal(7UL, c6.Decrements.State[_address1]);
            Assert.Equal(10UL, c6.Decrements.State[_address2]);
        }

        [Fact]
        public void PNCounter_must_be_able_to_increment_and_decrement_each_nodes_record_by_arbitrary_delta()
        {
            var c1 = new PNCounter();

            var c2 = c1.Increment(_address1, 3);
            var c3 = c2.Decrement(_address1, 2);
            var c4 = c3.Increment(_address2, 5);
            var c5 = c4.Decrement(_address2, 2);
            var c6 = c5.Increment(_address2);

            Assert.Equal(9, (long)c6.Increments.Value);
            Assert.Equal(4, (long)c6.Decrements.Value);
        }

        [Fact]
        public void PNCounter_must_be_able_to_summarize_the_history_to_the_correct_aggregated_value_of_increments_and_decrements()
        {
            var c1 = new PNCounter();

            var c2 = c1.Increment(_address1, 3);
            var c3 = c2.Decrement(_address1, 2);
            var c4 = c3.Increment(_address2, 5);
            var c5 = c4.Decrement(_address2, 2);
            var c6 = c5.Increment(_address2);

            Assert.Equal(9, (long)c6.Increments.Value);
            Assert.Equal(4, (long)c6.Decrements.Value);

            Assert.Equal(5, (long)c6.Value);
        }

        [Fact]
        public void PNCounter_must_be_able_to_have_its_history_correctly_merged_with_another_PNCounter()
        {
            var c11 = new PNCounter();
            var c12 = c11.Increment(_address1, 3);
            var c13 = c12.Decrement(_address1, 2);
            var c14 = c13.Increment(_address2, 5);
            var c15 = c14.Decrement(_address2, 2);
            var c16 = c15.Increment(_address2);

            Assert.Equal(9, (long)c16.Increments.Value);
            Assert.Equal(4, (long)c16.Decrements.Value);

            Assert.Equal(5, (long)c16.Value);

            var c21 = new PNCounter();
            var c22 = c21.Increment(_address1, 2);
            var c23 = c22.Decrement(_address1, 3);
            var c24 = c23.Increment(_address2, 3);
            var c25 = c24.Decrement(_address2, 2);
            var c26 = c25.Increment(_address2);

            Assert.Equal(6, (long)c26.Increments.Value);
            Assert.Equal(5, (long)c26.Decrements.Value);

            Assert.Equal(1, (long)c26.Value);

            var merged1 = c16.Merge(c26);
            Assert.Equal(9UL, merged1.Increments.Value);
            Assert.Equal(5UL, merged1.Decrements.Value);
            Assert.Equal(4UL, merged1.Value);

            var merged2 = c26.Merge(c16);
            Assert.Equal(9UL, merged2.Increments.Value);
            Assert.Equal(5UL, merged2.Decrements.Value);
            Assert.Equal(4UL, merged2.Value);
        }

        [Fact]
        public void PNCounter_must_have_support_for_pruning()
        {
            var c1 = new PNCounter();
            var c2 = c1.Increment(_address1);
            var c3 = c2.Decrement(_address2);

            Assert.True(c2.NeedPruningFrom(_address1));
            Assert.False(c2.NeedPruningFrom(_address2));
            Assert.True(c3.NeedPruningFrom(_address1));
            Assert.True(c3.NeedPruningFrom(_address2));

            var c4 = c3.Prune(_address1, _address2);
            Assert.True(c4.NeedPruningFrom(_address2));
            Assert.False(c4.NeedPruningFrom(_address1));

            var c5 = (c4.Increment(_address1)).PruningCleanup(_address1);
            Assert.False(c5.NeedPruningFrom(_address1));
        }
    }
}
