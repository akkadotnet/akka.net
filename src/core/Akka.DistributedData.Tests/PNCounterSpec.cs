using Akka.Cluster;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Akka.DistributedData.Tests
{
    public class PNCounterSpec
    {
        readonly UniqueAddress _address1;
        readonly UniqueAddress _address2;

        [Fact]
        public void APNCounterMustBeAbleToIncrementEachNodesRecordByOne()
        {
            var c1 = new PNCounter();
            
            var c2 = c1.Increment(_address1);
            var c3 = c2.Increment(_address1);

            var c4 = c3.Increment(_address2);
            var c5 = c4.Increment(_address2);
            var c6 = c5.Increment(_address2);

            Assert.Equal(2, c6.Increments.State[_address1]);
            Assert.Equal(3, c6.Increments.State[_address2]);
        }

        [Fact]
        public void APNCounterMustBeAbleToDecrementEachNodesRecordByOne()
        {
            var c1 = new PNCounter();

            var c2 = c1.Decrement(_address1);
            var c3 = c2.Decrement(_address1);

            var c4 = c3.Decrement(_address2);
            var c5 = c4.Decrement(_address2);
            var c6 = c5.Decrement(_address2);

            Assert.Equal(2, c6.Decrements.State[_address1]);
            Assert.Equal(3, c6.Decrements.State[_address2]);
        }

        [Fact]
        public void APNCounterMustBeAbleToIncrementEachNodesRecordByArbitraryDelta()
        {
            var c1 = new PNCounter();

            var c2 = c1.Increment (_address1, 3);
            var c3 = c2.Increment (_address1, 4);
            var c4 = c3.Increment (_address2, 2);
            var c5 = c4.Increment (_address2, 7);
            var c6 = c5.Increment(_address2);

            Assert.Equal(7, c6.Increments.State[_address1]);
            Assert.Equal(10, c6.Increments.State[_address2]);
        }

        [Fact]
        public void APNCounterMustBeAbleToDecrementEachNodesRecordByArbitraryDelta()
        {
            var c1 = new PNCounter();

            var c2 = c1.Decrement(_address1, 3);
            var c3 = c2.Decrement(_address1, 4);
            var c4 = c3.Decrement(_address2, 2);
            var c5 = c4.Decrement(_address2, 7);
            var c6 = c5.Decrement(_address2);

            Assert.Equal(7, c6.Decrements.State[_address1]);
            Assert.Equal(10, c6.Decrements.State[_address2]);
        }

        [Fact]
        public void APNCounterMustbBeAbleToIncrementAndDecrementEachNodesRecordByArbitraryDelta()
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
        public void APNCounterMustbBeAbleToSummarizeTheHistoryToTheCorrectAggregatedValueOfIncrementsAndDecrements()
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
        public void APNCounterMustbBeAbleToSummarizeTheHistoryToTheCorrectAggregatedValueOfIncrementsAndDecrements()
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
        public void APNCounterMustbBeAbleToHaveItsHistoryCorrectlyMergedWithAnotherGCounter()
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
            Assert.Equal(9, merged1.Increments.Value);
            Assert.Equal(5, merged1.Decrements.Value);
            Assert.Equal(4, merged1.Value);

            var merged2 = c26.Merge(c16);
            Assert.Equal(9, merged2.Increments.Value);
            Assert.Equal(5, merged2.Decrements.Value);
            Assert.Equal(4, merged2.Value);
        }

        [Fact]
        public void APNCounterMustHaveSupportForPruning()
        {
            var c1 = new PNCounter();
            var c2 = c1.Increment(_address1);
            var c3 = c2.Decrement(_address2);

            Assert.Equal(true, c2.NeedPruningFrom(_address1));
            Assert.Equal(false, c2.NeedPruningFrom(_address2));
            Assert.Equal(true, c3.NeedPruningFrom(_address1));
            Assert.Equal(true, c3.NeedPruningFrom(_address2));

            var c4 = c3.Prune(_address1, _address2);
            Assert.Equal(true, c4.NeedPruningFrom(_address2));
            Assert.Equal(false, c4.NeedPruningFrom(_address1));

            var c5 = (c4.Increment(_address1)).PruningCleanup(_address1);
            Assert.Equal(false, c5.NeedPruningFrom(_address1));
        }
    }
}
