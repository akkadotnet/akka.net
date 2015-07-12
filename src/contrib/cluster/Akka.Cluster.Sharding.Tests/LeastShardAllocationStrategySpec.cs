//-----------------------------------------------------------------------
// <copyright file="LeastShardAllocationStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Xunit;

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
            var allocations = new Dictionary<IActorRef, string[]>
            {
                {_regionA, new []{"shard1"} },
                {_regionB, new []{"shard2"} },
                {_regionC, new string[0] }
            };

            var result = _allocationStrategy.AllocateShard(_regionA, "shard3", allocations).Result;
            Assert.Equal(result, _regionC);
        }

        [Fact]
        public void LeastShardAllocationStrategy_should_reallocate_from_region_with_most_number_of_shards()
        {
            var allocations = new Dictionary<IActorRef, string[]>
            {
                {_regionA, new []{"shard1"} },
                {_regionB, new []{"shard2", "shard3"} },
                {_regionC, new string[0] }
            };

            // so far regionB has 2 shards and regionC has 0 shards, but the diff is less than rebalanceThreshold
            var r1 = _allocationStrategy.Rebalance(allocations, new HashSet<string>()).Result;
            Assert.Equal(r1.Count, 0);

            allocations[_regionB] = new[] { "shard2", "shard3", "shard4" };
            var r2 = _allocationStrategy.Rebalance(allocations, new HashSet<string>()).Result;
            Assert.Equal(r2.Count, 1);
            Assert.Equal(r2.First(), "shard2");

            var r3 = _allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard4" }).Result;
            Assert.Equal(r3.Count, 0);

            allocations[_regionA] = new[] { "shard1", "shard5", "shard6" };
            var r4 = _allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard1" }).Result;
            Assert.Equal(r4.Count, 1);
            Assert.Equal(r2.First(), "shard2");
        }

        [Fact]
        public void LeastShardAllocationStrategy_should_limit_number_of_simultanious_rebalances()
        {
            var allocations = new Dictionary<IActorRef, string[]>
            {
                {_regionA, new []{"shard1"} },
                {_regionB, new []{ "shard2", "shard3", "shard4", "shard5", "shard6" } },
                {_regionC, new string[0] }
            };

            var r1 = _allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard2" }).Result;
            Assert.Equal(r1.Count, 1);
            Assert.Equal(r1.First(), "shard3");

            var r2 = _allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard2", "shard3" }).Result;
            Assert.Equal(r2.Count, 0);
        }
    }
}