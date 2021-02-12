//-----------------------------------------------------------------------
// <copyright file="DeprecatedLeastShardAllocationStrategySpec.cs" company="Akka.NET Project">
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
using static Akka.Cluster.ClusterEvent;
using Akka.Util;

namespace Akka.Cluster.Sharding.Tests
{
    public class DeprecatedLeastShardAllocationStrategySpec : TestKit.Xunit2.TestKit
    {
        internal class TestLeastShardAllocationStrategy : LeastShardAllocationStrategy
        {
            private readonly Func<CurrentClusterState> clusterState;
            private readonly Func<Member> selfMember;

            public TestLeastShardAllocationStrategy(int rebalanceThreshold, int maxSimultaneousRebalance, Func<CurrentClusterState> clusterState, Func<Member> selfMember) :
                base(rebalanceThreshold, maxSimultaneousRebalance)
            {
                this.clusterState = clusterState;
                this.selfMember = selfMember;
            }

            protected override CurrentClusterState ClusterState => clusterState();
            protected override Member SelfMember => selfMember();
        }

        internal static Member NewUpMember(string host, int port = 252525, AppVersion version = null) => LeastShardAllocationStrategySpec.NewUpMember(host, port, version);

        internal static IActorRef NewFakeRegion(string idForDebug, Member member) => LeastShardAllocationStrategySpec.NewFakeRegion(idForDebug, member);

        private Member memberA;
        private Member memberB;
        private Member memberC;

        private readonly IActorRef regionA;
        private readonly IActorRef regionB;
        private readonly IActorRef regionC;

        public DeprecatedLeastShardAllocationStrategySpec()
        {
            memberA = NewUpMember("127.0.0.1");
            memberB = NewUpMember("127.0.0.2");
            memberC = NewUpMember("127.0.0.3");

            regionA = NewFakeRegion("regionA", memberA);
            regionB = NewFakeRegion("regionB", memberB);
            regionC = NewFakeRegion("regionC", memberC);
        }

        private IImmutableDictionary<IActorRef, IImmutableList<string>> CreateAllocations(int aCount, int bCount = 0, int cCount = 0)
        {
            var shards = Enumerable.Range(1, (aCount + bCount + cCount)).Select(i => i.ToString("000"));

            IImmutableDictionary<IActorRef, IImmutableList<string>> allocations = LeastShardAllocationStrategySpec.ImmutableDictionaryKeepOrder<IActorRef, IImmutableList<string>>.Empty;
            allocations = allocations.Add(regionA, shards.Take(aCount).ToImmutableList());
            allocations = allocations.Add(regionB, shards.Skip(aCount).Take(bCount).ToImmutableList());
            allocations = allocations.Add(regionC, shards.Skip(aCount + bCount).Take(cCount).ToImmutableList());
            return allocations;
        }

        private IShardAllocationStrategy AllocationStrategyWithFakeCluster(int rebalanceThreshold, int maxSimultaneousRebalance)
        {
            // we don't really "start" it as we fake the cluster access
            return new TestLeastShardAllocationStrategy(rebalanceThreshold, maxSimultaneousRebalance, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(memberA, memberB, memberC)), () => memberA);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_allocate_to_region_with_least_number_of_shards_1_1_0()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 3, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 1);
            allocationStrategy.AllocateShard(regionA, "003", allocations).Result.Should().Be(regionC);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_2_0_0_rebalanceThreshold_1()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_diff_equal_to_threshold_1_1_0_rebalanceThreshold_1()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 1);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_1_2_0_rebalanceThreshold_1()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 1, bCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("002");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("002")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_3_0_0_rebalanceThreshold_1()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 3);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("002");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_4_4_0_rebalanceThreshold_1()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 4, bCount: 4);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("005");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_4_4_2_rebalanceThreshold_1()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 1, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 4, bCount: 4, cCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            // not optimal, 005 stopped and started again, but ok
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("005");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_rebalance_from_region_with_most_number_of_shards_1_3_0_rebalanceThreshold_2()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
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
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 2, bCount: 2);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_3_3_0_rebalanceThreshold_2()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 3, bCount: 3);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001")).Result.Should().BeEquivalentTo("004");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("004")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_4_4_0_rebalanceThreshold_2()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
            var allocations = CreateAllocations(aCount: 4, bCount: 4);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001", "002");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002")).Result.Should().BeEquivalentTo("005", "006");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002").Add("005").Add("006")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_from_region_with_most_number_of_shards_5_5_0_rebalanceThreshold_2()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 2, maxSimultaneousRebalance: 10);
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
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 2, maxSimultaneousRebalance: 100);
            var allocations = CreateAllocations(aCount: 50, bCount: 50);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("001", "002");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002")).Result.Should().BeEquivalentTo("051", "052");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("001").Add("002").Add("051").Add("052")).Result.Should().BeEquivalentTo("003", "004");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_limit_number_of_simultaneous_rebalance_1_10_0()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 3, maxSimultaneousRebalance: 2);
            var allocations = CreateAllocations(aCount: 1, bCount: 10);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEquivalentTo("002", "003");
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("002").Add("003")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_pick_shards_that_are_in_progress_10_0_0()
        {
            var allocationStrategy = AllocationStrategyWithFakeCluster(rebalanceThreshold: 3, maxSimultaneousRebalance: 4);
            var allocations = CreateAllocations(aCount: 10);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty.Add("002").Add("003")).Result.Should().BeEquivalentTo("001", "004");
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_prefer_least_shards_latest_version_non_downed_leaving_or_exiting_nodes()
        {
            // old version, up
            var oldMember = NewUpMember("127.0.0.1", version: AppVersion.Create("1.0.0"));
            // leaving, new version
            var leavingMember = NewUpMember("127.0.0.2", version: AppVersion.Create("1.0.0")).Copy(MemberStatus.Leaving);
            // new version, up
            var newVersionMember1 = NewUpMember("127.0.0.3", version: AppVersion.Create("1.0.1"));
            // new version, up
            var newVersionMember2 = NewUpMember("127.0.0.4", version: AppVersion.Create("1.0.1"));
            // new version, up
            var newVersionMember3 = NewUpMember("127.0.0.5", version: AppVersion.Create("1.0.1"));

            var fakeLocalRegion = NewFakeRegion("oldapp", oldMember);
            var fakeRegionA = NewFakeRegion("leaving", leavingMember);
            var fakeRegionB = NewFakeRegion("fewest", newVersionMember1);
            var fakeRegionC = NewFakeRegion("oneshard", newVersionMember2);
            var fakeRegionD = NewFakeRegion("most", newVersionMember3);

            var shardsAndMembers = ImmutableList.Create(
                new Internal.AbstractLeastShardAllocationStrategy.RegionEntry(fakeRegionB, newVersionMember1, ImmutableList<string>.Empty),
                new Internal.AbstractLeastShardAllocationStrategy.RegionEntry(fakeRegionA, leavingMember, ImmutableList<string>.Empty),
                new Internal.AbstractLeastShardAllocationStrategy.RegionEntry(fakeRegionD, newVersionMember3, ImmutableList.Create("ShardId2", "ShardId3")),
                new Internal.AbstractLeastShardAllocationStrategy.RegionEntry(fakeLocalRegion, oldMember, ImmutableList<string>.Empty),
                new Internal.AbstractLeastShardAllocationStrategy.RegionEntry(fakeRegionC, newVersionMember2, ImmutableList.Create("ShardId1"))
                );

            var sortedRegions =
                shardsAndMembers.Sort(Internal.AbstractLeastShardAllocationStrategy.ShardSuitabilityOrdering.Instance).Select(i => i.Region);

            // only node b has the new version
            sortedRegions.Should().Equal(
                fakeRegionB, // fewest shards, newest version, up
                fakeRegionC, // newest version, up
                fakeRegionD, // most shards, up
                fakeLocalRegion, // old app version
                fakeRegionA); // leaving
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_rolling_update_in_progress()
        {
            var member1 = NewUpMember("127.0.0.1", version: AppVersion.Create("1.0.0"));
            var member2 = NewUpMember("127.0.0.1", version: AppVersion.Create("1.0.1"));

            // multiple versions to simulate rolling update in progress
            var allocationStrategy =
                new TestLeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 100, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(member1, member2)), () => member1);

            var allocations = CreateAllocations(aCount: 5, bCount: 5);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002")).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002", "051", "052")).Result.Should().BeEmpty();
        }


        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_regions_are_unreachable()
        {
            var member1 = NewUpMember("127.0.0.1");
            var member2 = NewUpMember("127.0.0.2");

            // multiple versions to simulate rolling update in progress
            var allocationStrategy =
                new TestLeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 100, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(member1, member2), unreachable: ImmutableHashSet.Create(member2)), () => member2);

            var allocations = CreateAllocations(aCount: 5, bCount: 5);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002")).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002", "051", "052")).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_members_are_joining_dc()
        {
            var member1 = NewUpMember("127.0.0.1");
            var member2 =
                Member.Create(
                    new UniqueAddress(new Address("akka", "myapp", "127.0.0.2", 252525), 1),
                    ImmutableHashSet<string>.Empty,
                    member1.AppVersion);

            // multiple versions to simulate rolling update in progress
            var allocationStrategy =
                new TestLeastShardAllocationStrategy(rebalanceThreshold: 2, maxSimultaneousRebalance: 100, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(member1, member2), unreachable: ImmutableHashSet.Create(member2)), () => member2);

            var allocations = CreateAllocations(aCount: 5, bCount: 5);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002")).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002", "051", "052")).Result.Should().BeEmpty();
        }
    }
}
