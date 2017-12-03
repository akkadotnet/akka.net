//-----------------------------------------------------------------------
// <copyright file="LeastShardAllocationStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Cluster.Sharding.Tests
{
    public class LeastShardAllocationStrategySpec : TestKitBase
    {
        private readonly IShardAllocationStrategy _allocationStrategy;
        private readonly IActorRef _regionA;
        private readonly IActorRef _regionB;
        private readonly IActorRef _regionC;

        public LeastShardAllocationStrategySpec() : base(new XunitAssertions(), "LeastShardAllocationStrategySpec")
        {
            _regionA = Sys.ActorOf(Props.Empty, "regionA");
            _regionB = Sys.ActorOf(Props.Empty, "regionB");
            _regionC = Sys.ActorOf(Props.Empty, "regionC");

            _allocationStrategy = new LeastShardAllocationStrategy(3, 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_should_allocate_to_region_with_least_number_of_shards()
        {
            var allocations = new Dictionary<IActorRef, IImmutableList<string>>
            {
                {_regionA, new []{"shard1"}.ToImmutableList() },
                {_regionB, new []{"shard2"}.ToImmutableList() },
                {_regionC,  ImmutableList<string>.Empty }
            }.ToImmutableDictionary();

            var result = _allocationStrategy.AllocateShard(_regionA, "shard3", allocations).Result;
            result.Should().Be(_regionC);
        }

        [Fact]
        public void LeastShardAllocationStrategy_should_rebalance_from_region_with_most_number_of_shards()
        {
            var allocations = new Dictionary<IActorRef, IImmutableList<string>>
            {
                {_regionA, new []{"shard1"}.ToImmutableList() },
                {_regionB, new []{"shard2", "shard3"}.ToImmutableList() },
                {_regionC,  ImmutableList<string>.Empty }
            }.ToImmutableDictionary();

            // so far regionB has 2 shards and regionC has 0 shards, but the diff is less than rebalanceThreshold
            var r1 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            r1.Count.Should().Be(0);

            allocations = allocations.SetItem(_regionB, new[] { "shard2", "shard3", "shard4" }.ToImmutableList());
            var r2 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            r2.Should().BeEquivalentTo(new[] { "shard2", "shard3" });

            var r3 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard4")).Result;
            r3.Count.Should().Be(0);

            allocations = allocations.SetItem(_regionA, new[] { "shard1", "shard5", "shard6" }.ToImmutableList());
            var r4 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard1")).Result;
            r4.Should().BeEquivalentTo(new[] { "shard2" });
        }

        [Fact]
        public void LeastShardAllocationStrategy_should_rebalance_multiple_shards_if_max_simultaneous_rebalances_is_not_exceeded()
        {
            var allocations = new Dictionary<IActorRef, IImmutableList<string>>
            {
                {_regionA, new []{"shard1"}.ToImmutableList() },
                {_regionB, new []{ "shard2", "shard3", "shard4", "shard5", "shard6" }.ToImmutableList() },
                {_regionC, ImmutableList<string>.Empty}
            }.ToImmutableDictionary();

            var r1 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            r1.Should().BeEquivalentTo(new[] { "shard2", "shard3" });

            var r2 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard2", "shard3")).Result;
            r2.Count.Should().Be(0);
        }

        [Fact]
        public void LeastShardAllocationStrategy_should_limit_number_of_simultaneous_rebalances()
        {
            var allocations = new Dictionary<IActorRef, IImmutableList<string>>
            {
                {_regionA, new []{"shard1"}.ToImmutableList() },
                {_regionB, new []{ "shard2", "shard3", "shard4", "shard5", "shard6" }.ToImmutableList() },
                {_regionC, ImmutableList<string>.Empty}
            }.ToImmutableDictionary();

            var r1 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard2")).Result;
            r1.Should().BeEquivalentTo(new[] { "shard3" });

            var r2 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard2", "shard3")).Result;
            r2.Count.Should().Be(0);
        }

        [Fact]
        public void LeastShardAllocationStrategy_dont_rebalance_excessive_shards_if_maxSimultaneousRebalance_gt_rebalanceThreshold()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 5);
            var allocations = new Dictionary<IActorRef, IImmutableList<string>>
            {
                {_regionA, new []{"shard1", "shard2", "shard3", "shard4", "shard5", "shard6", "shard7", "shard8"}.ToImmutableList() },
                {_regionB, new []{"shard9", "shard10", "shard11", "shard12" }.ToImmutableList() }
            }.ToImmutableDictionary();

            var r1 = allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard2")).Result;
            r1.Should().BeEquivalentTo(new[] { "shard1", "shard3", "shard4" });

            var r2 = _allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("shard5", "shard6", "shard7", "shard8")).Result;
            r2.Count.Should().Be(0);
        }
    }
}