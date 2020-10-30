//-----------------------------------------------------------------------
// <copyright file="DeprecatedLeastShardAllocationStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;
using FluentAssertions;
using System.Collections;
using System;

namespace Akka.Cluster.Sharding.Tests
{
    public class DeprecatedLeastShardAllocationStrategySpec : TestKit.Xunit2.TestKit
    {
        private readonly IShardAllocationStrategy _allocationStrategy;
        private readonly IActorRef _regionA;
        private readonly IActorRef _regionB;
        private readonly IActorRef _regionC;

        public DeprecatedLeastShardAllocationStrategySpec()
        {
            _regionA = Sys.ActorOf(Props.Empty, "regionA");
            _regionB = Sys.ActorOf(Props.Empty, "regionB");
            _regionC = Sys.ActorOf(Props.Empty, "regionC");

            _allocationStrategy = new LeastShardAllocationStrategy(3, 2);
        }

        private IImmutableDictionary<IActorRef, IImmutableList<string>> CreateAllocations(int aCount, int bCount = 0, int cCount = 0)
        {
            var shards = Enumerable.Range(1, (aCount + bCount + cCount)).Select(i => i.ToString("000"));

            IImmutableDictionary<IActorRef, IImmutableList<string>> allocations = LeastShardAllocationStrategySpec.ImmutableDictionaryKeepOrder<IActorRef, IImmutableList<string>>.Empty;
            allocations = allocations.Add(_regionA, shards.Take(aCount).ToImmutableList());
            allocations = allocations.Add(_regionB, shards.Skip(aCount).Take(bCount).ToImmutableList());
            allocations = allocations.Add(_regionC, shards.Skip(aCount + bCount).Take(cCount).ToImmutableList());
            return allocations;
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_allocate_to_region_with_least_number_of_shards()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 3, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 1);
            allocationStrategy.AllocateShard(_regionA, "003", allocations).Result.Should().Be(_regionC);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_2_0_0_rebalanceThreshold_1()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_diff_equal_to_threshold_1_1_0_rebalanceThreshold_1()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 1);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_1_2_0_rebalanceThreshold_1()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("002");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("002")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_3_0_0_rebalanceThreshold_1()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 3);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("002");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_4_4_0_rebalanceThreshold_1()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 4, bCount: 4);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("005");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_4_4_2_rebalanceThreshold_1()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 4, bCount: 4, cCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            // not optimal, 005 stopped and started again, but ok
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("005");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_rebalance_from_region_with_most_number_of_shards_1_3_0_rebalanceThreshold_2()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 2);

            // so far regionB has 2 shards and regionC has 0 shards, but the diff is <= rebalanceThreshold
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();

            var allocations2 = CreateAllocations(aCount: 1, bCount: 3);
            allocationStrategy.Rebalance(allocations2, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("002");
            allocationStrategy.Rebalance(allocations2, ImmutableHashSet<string>.Empty.Add("002")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_diff_equal_to_threshold_2_2_0_rebalanceThreshold_2()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 2, bCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_3_3_0_rebalanceThreshold_2()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 3, bCount: 3);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("004");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("004")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_4_4_0_rebalanceThreshold_2()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 4, bCount: 4);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001", "002");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002")).Result.Should().BeEquivalentTo("005", "006");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002").Add("005").Add("006")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_5_5_0_rebalanceThreshold_2()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 5, bCount: 5);
            // optimal would => [4, 4, 2] or even => [3, 4, 3]
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001", "002");
            // if 001 and 002 are not started quickly enough this is stopping more than optimal
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002")).Result.Should().BeEquivalentTo("006", "007");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002").Add("006").Add("007")).Result.Should().BeEquivalentTo("003");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_50_50_0_rebalanceThreshold_2()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 100);
            var allocations = CreateAllocations(aCount: 50, bCount: 50);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001", "002");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002")).Result.Should().BeEquivalentTo("051", "052");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002").Add("051").Add("052")).Result.Should().BeEquivalentTo("003", "004");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_limit_number_of_simultaneous_rebalance()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 3, maxSimultaneousRebalance: 2);
            var allocations = CreateAllocations(aCount: 1, bCount: 10);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("002", "003");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("002").Add("003")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_pick_shards_that_are_in_progress()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(rebalanceThreshold: 3, maxSimultaneousRebalance: 4);
            var allocations = CreateAllocations(aCount: 10);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("002").Add("003")).Result.Should().BeEquivalentTo("001", "004");
        }
    }
}
