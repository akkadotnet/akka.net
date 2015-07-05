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
        private readonly IShardAllocationStrategy allocationStrategy;
        private readonly IActorRef regionA;
        private readonly IActorRef regionB;
        private readonly IActorRef regionC;

        public LeastShardAllocationStrategySpec() : base(new XunitAssertions(), "LeastShardAllocationStrategySpec")
        {
            regionA = Sys.ActorOf(Props.Empty, "regionA");
            regionB = Sys.ActorOf(Props.Empty, "regionB");
            regionC = Sys.ActorOf(Props.Empty, "regionC");

            allocationStrategy = new LeastShardAllocationStrategy(3, 2);
        }

        [Fact(Skip = "TODO")]
        public void LeastShardAllocationStrategy_should_allocate_to_region_with_least_number_of_shards()
        {
            var allocations = new Dictionary<IActorRef, string[]>
            {
                {regionA, new []{"shard1"} },
                {regionB, new []{"shard2"} },
                {regionC, new string[0] }
            };

            var result = allocationStrategy.AllocateShard(regionA, "shard3", allocations).Result;
            Assert.Equal(result, regionC);
        }

        [Fact(Skip = "TODO")]
        public void LeastShardAllocationStrategy_should_reallocate_from_region_with_most_number_of_shards()
        {
            var allocations = new Dictionary<IActorRef, string[]>
            {
                {regionA, new []{"shard1"} },
                {regionB, new []{"shard2", "shard3"} },
                {regionC, new string[0] }
            };

            // so far regionB has 2 shards and regionC has 0 shards, but the diff is less than rebalanceThreshold
            var r1 = allocationStrategy.Rebalance(allocations, new HashSet<string>()).Result;
            Assert.Equal(r1.Count, 0);

            allocations[regionB] = new[] { "shard2", "shard3", "shard4" };
            var r2 = allocationStrategy.Rebalance(allocations, new HashSet<string>()).Result;
            Assert.Equal(r2.Count, 1);
            Assert.Equal(r2.First(), "shard2");

            var r3 = allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard4" }).Result;
            Assert.Equal(r3.Count, 0);

            allocations[regionA] = new[] { "shard1", "shard5", "shard6" };
            var r4 = allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard1" }).Result;
            Assert.Equal(r4.Count, 1);
            Assert.Equal(r2.First(), "shard2");
        }

        [Fact(Skip = "TODO")]
        public void LeastShardAllocationStrategy_should_limit_number_of_simultanious_rebalances()
        {
            var allocations = new Dictionary<IActorRef, string[]>
            {
                {regionA, new []{"shard1"} },
                {regionB, new []{ "shard2", "shard3", "shard4", "shard5", "shard6" } },
                {regionC, new string[0] }
            };

            var r1 = allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard2" }).Result;
            Assert.Equal(r1.Count, 1);
            Assert.Equal(r1.First(), "shard3");

            var r2 = allocationStrategy.Rebalance(allocations, new HashSet<string> { "shard2", "shard3" }).Result;
            Assert.Equal(r2.Count, 0);
        }
    }
}