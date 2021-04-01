//-----------------------------------------------------------------------
// <copyright file="LeastShardAllocationStrategyRandomizedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using FluentAssertions.Execution;
using static Akka.Cluster.ClusterEvent;

namespace Akka.Cluster.Sharding.Tests
{
    public class LeastShardAllocationStrategyRandomizedSpec : TestKit.Xunit2.TestKit
    {
        private readonly IShardAllocationStrategy strategyWithoutLimits;
        private int rndSeed;
        private Random rnd;
        private int iteration = 1;
        private int iterationsPerTest = 10;

        private ImmutableSortedSet<Member> clusterMembers = ImmutableSortedSet<Member>.Empty;

        public LeastShardAllocationStrategyRandomizedSpec()
        {
            rndSeed = DateTime.UtcNow.Millisecond;
            rnd = new Random(rndSeed);
            Log.Info($"Random seed: {rndSeed}");
            strategyWithoutLimits = StrategyWithFakeCluster();
        }

        private IImmutableDictionary<IActorRef, IImmutableList<string>> CreateAllocations(IImmutableDictionary<IActorRef, int> countPerRegion)
        {
            return countPerRegion.ToImmutableDictionary(i => i.Key, i => (IImmutableList<string>)Enumerable.Range(1, i.Value).Select(n => n.ToString("000")).Select(n => $"{i.Key.Path.Name}-{n}").ToImmutableList());
        }

        private IShardAllocationStrategy StrategyWithFakeCluster(int absoluteLimit = 100000, double relativeLimit = 1.0)
        {
            // we don't really "start" it as we fake the cluster access
            return new LeastShardAllocationStrategySpec.TestLeastShardAllocationStrategy(absoluteLimit, relativeLimit, () => new CurrentClusterState().Copy(members: clusterMembers), () => clusterMembers.FirstOrDefault());
        }

        private void TestRebalance(
            IShardAllocationStrategy allocationStrategy,
            int maxRegions,
            int maxShardsPerRegion,
            int expectedMaxSteps)
        {
            foreach (var i in Enumerable.Range(1, iterationsPerTest))
            {
                iteration += 1;
                var numberOfRegions = rnd.Next(maxRegions) + 1;

                var memberArray = Enumerable.Range(1, numberOfRegions).Select(n => LeastShardAllocationStrategySpec.NewUpMember("127.0.0.1", port: n)).ToArray();
                clusterMembers = ImmutableSortedSet.Create(memberArray);//.toIndexedSeq: _ *);
                var regions = Enumerable.Range(1, numberOfRegions).Select(n => LeastShardAllocationStrategySpec.NewFakeRegion($"{iteration}-R{n}", memberArray[n - 1]));

                //var regions = Enumerable.Range(1, numberOfRegions).Select(n => Sys.ActorOf(Props.Empty, $"{iteration}-R{n}")).ToImmutableList();
                var countPerRegion = regions.ToImmutableDictionary(region => region, region => rnd.Next(maxShardsPerRegion));
                var allocations = CreateAllocations(countPerRegion);
                TestRebalance(allocationStrategy, allocations, ImmutableList.Create(allocations), expectedMaxSteps);
                foreach (var region in regions)
                    Sys.Stop(region);
            }
        }

        private void TestRebalance(
              IShardAllocationStrategy allocationStrategy,
              IImmutableDictionary<IActorRef, IImmutableList<string>> allocations,
              ImmutableList<IImmutableDictionary<IActorRef, IImmutableList<string>>> steps,
              int maxSteps)
        {
            var round = steps.Count;
            var rebalanceResult = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            var newAllocations = LeastShardAllocationStrategySpec.AfterRebalance(allocationStrategy, allocations, rebalanceResult);

            LeastShardAllocationStrategySpec.CountShards(newAllocations).Should().Be(LeastShardAllocationStrategySpec.CountShards(allocations), $"test {allocationStrategy}[{ string.Join(", ", LeastShardAllocationStrategySpec.CountShardsPerRegion(allocations))}]: ");
            var min = LeastShardAllocationStrategySpec.CountShardsPerRegion(newAllocations).Min();
            var max = LeastShardAllocationStrategySpec.CountShardsPerRegion(newAllocations).Max();
            var diff = max - min;
            var newSteps = steps.Add(newAllocations);
            if (diff <= 1)
            {
                if (round >= 3 && maxSteps <= 10)
                {
                    // Should be very rare (I have not seen it)
                    Sys.Log.Info($"rebalance solved in round {round}, [{string.Join(" => ", newSteps.Select(step => string.Join(", ", LeastShardAllocationStrategySpec.CountShardsPerRegion(step))))}]");
                }
            }
            else if (round == maxSteps)
            {
                throw new AssertionFailedException($"Couldn't solve rebalance in $round rounds, [{string.Join(" => ", newSteps.Select(step => string.Join(", ", LeastShardAllocationStrategySpec.CountShardsPerRegion(step))))}]");
            }
            else
            {
                TestRebalance(allocationStrategy, newAllocations, newSteps, maxSteps);
            }
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_5_regions_5_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 5, maxShardsPerRegion: 5, expectedMaxSteps: 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_5_regions_100_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 5, maxShardsPerRegion: 100, expectedMaxSteps: 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_20_regions_5_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 20, maxShardsPerRegion: 5, expectedMaxSteps: 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_20_regions__20_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 20, maxShardsPerRegion: 20, expectedMaxSteps: 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_20_regions_200_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 20, maxShardsPerRegion: 200, expectedMaxSteps: 5);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_100_regions_100_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 100, maxShardsPerRegion: 100, expectedMaxSteps: 5);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_100_regions_1000_shards()
        {
            TestRebalance(strategyWithoutLimits, maxRegions: 100, maxShardsPerRegion: 1000, expectedMaxSteps: 5);
        }

        [Fact]
        public void LeastShardAllocationStrategy_with_random_scenario_must_rebalance_shards_with_max_20_regions_20_shards_and_limits()
        {
            var absoluteLimit = 3 + rnd.Next(7) + 3;
            var relativeLimit = 0.05 + (rnd.NextDouble() * 0.95);

            var strategy = StrategyWithFakeCluster(absoluteLimit, relativeLimit);
            TestRebalance(strategy, maxRegions: 20, maxShardsPerRegion: 20, expectedMaxSteps: 20);
        }
    }
}
