//-----------------------------------------------------------------------
// <copyright file="LeastShardAllocationStrategySpec.cs" company="Akka.NET Project">
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
using Akka.Util;
using static Akka.Cluster.ClusterEvent;

namespace Akka.Cluster.Sharding.Tests
{
    public class LeastShardAllocationStrategySpec : TestKit.Xunit2.TestKit
    {
        private class DummyActorRef : MinimalActorRef
        {
            public override IActorRefProvider Provider => throw new NotImplementedException();

            public override ActorPath Path => new RootActorPath(new Address("akka", "myapp")) / "system" / "fake";
        }

        internal static IImmutableDictionary<IActorRef, IImmutableList<string>> AfterRebalance(
            IShardAllocationStrategy allocationStrategy,
            IImmutableDictionary<IActorRef, IImmutableList<string>> allocations,
            IImmutableSet<string> rebalance)
        {
            var allocationsAfterRemoval = allocations.SetItems(allocations.Select(i => new KeyValuePair<IActorRef, IImmutableList<string>>(i.Key, i.Value.ToImmutableHashSet().Except(rebalance).OrderBy(j => j).ToImmutableList())));

            IImmutableDictionary<IActorRef, IImmutableList<string>> acc = allocationsAfterRemoval;
            foreach (var shard in rebalance.OrderBy(i => i))
            {
                var region = allocationStrategy.AllocateShard(new DummyActorRef(), shard, acc).Result;
                acc = acc.SetItem(region, acc[region].Add(shard));
            }
            return acc;
        }

        internal static ImmutableList<int> CountShardsPerRegion(IImmutableDictionary<IActorRef, IImmutableList<string>> newAllocations)
        {
            return newAllocations.Values.Select(i => i.Count).ToImmutableList();
        }

        internal static int CountShards(IImmutableDictionary<IActorRef, IImmutableList<string>> allocations)
        {
            return CountShardsPerRegion(allocations).Sum();
        }

        static ImmutableList<int> AllocationCountsAfterRebalance(
            IShardAllocationStrategy allocationStrategy,
            IImmutableDictionary<IActorRef, IImmutableList<string>> allocations,
            IImmutableSet<string> rebalance)
        {
            return CountShardsPerRegion(AfterRebalance(allocationStrategy, allocations, rebalance));
        }

        private class DummyActorRef2 : MinimalActorRef
        {
            public DummyActorRef2(ActorPath path)
            {
                Path = path;
            }
            public override IActorRefProvider Provider => throw new NotImplementedException();

            public override ActorPath Path { get; }
        }


        internal static Member NewUpMember(string host, int port = 252525, AppVersion version = null)
        {
            return Member.Create(
              new UniqueAddress(new Address("akka", "myapp", host, port), 1),
              ImmutableHashSet<string>.Empty,
              version ?? AppVersion.Create("1.0.0")).Copy(MemberStatus.Up);
        }

        internal static IActorRef NewFakeRegion(string idForDebug, Member member)
        {
            return new DummyActorRef2(new RootActorPath(member.Address) / "system" / "fake" / idForDebug);
        }


        /// <summary>
        /// Test dictionary, will keep the order of items as they were added
        /// Needed because scala Map is having similar behaviour and tests depends on it
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        internal sealed class ImmutableDictionaryKeepOrder<TKey, TValue> : IImmutableDictionary<TKey, TValue>
        {
            public static readonly ImmutableDictionaryKeepOrder<TKey, TValue> Empty = new ImmutableDictionaryKeepOrder<TKey, TValue>(ImmutableDictionary<TKey, TValue>.Empty, ImmutableList<KeyValuePair<TKey, TValue>>.Empty);

            private readonly ImmutableDictionary<TKey, TValue> _dictionary = ImmutableDictionary<TKey, TValue>.Empty;
            private readonly ImmutableList<KeyValuePair<TKey, TValue>> _items = ImmutableList<KeyValuePair<TKey, TValue>>.Empty;

            private ImmutableDictionaryKeepOrder(ImmutableDictionary<TKey, TValue> dictionary, ImmutableList<KeyValuePair<TKey, TValue>> items)
            {
                _dictionary = dictionary;
                _items = items;
            }

            public TValue this[TKey key] => _dictionary[key];

            public IEnumerable<TKey> Keys => _items.Select(i => i.Key);

            public IEnumerable<TValue> Values => _items.Select(i => i.Value);

            public int Count => _dictionary.Count;

            public IImmutableDictionary<TKey, TValue> Add(TKey key, TValue value)
            {
                return new ImmutableDictionaryKeepOrder<TKey, TValue>(
                    _dictionary.Add(key, value),
                    _items.Add(new KeyValuePair<TKey, TValue>(key, value))
                    );
            }

            public IImmutableDictionary<TKey, TValue> AddRange(IEnumerable<KeyValuePair<TKey, TValue>> pairs)
            {
                return new ImmutableDictionaryKeepOrder<TKey, TValue>(
                    _dictionary.AddRange(pairs),
                    _items.AddRange(pairs)
                    );
            }

            public IImmutableDictionary<TKey, TValue> Clear()
            {
                return Empty;
            }

            public bool Contains(KeyValuePair<TKey, TValue> pair)
            {
                return _dictionary.Contains(pair);
            }

            public bool ContainsKey(TKey key)
            {
                return _dictionary.ContainsKey(key);
            }

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            {
                return _items.GetEnumerator();
            }

            public IImmutableDictionary<TKey, TValue> Remove(TKey key)
            {
                return new ImmutableDictionaryKeepOrder<TKey, TValue>(
                    _dictionary.Remove(key),
                    _items.RemoveAll(i => EqualityComparer<TKey>.Default.Equals(key, i.Key))
                    );
            }

            public IImmutableDictionary<TKey, TValue> RemoveRange(IEnumerable<TKey> keys)
            {
                return new ImmutableDictionaryKeepOrder<TKey, TValue>(
                    _dictionary.RemoveRange(keys),
                    _items.RemoveAll(i => keys.Any(j => EqualityComparer<TKey>.Default.Equals(j, i.Key)))
                    );
            }

            public IImmutableDictionary<TKey, TValue> SetItem(TKey key, TValue value)
            {
                var index = _items.FindIndex(i => EqualityComparer<TKey>.Default.Equals(i.Key, key));

                return new ImmutableDictionaryKeepOrder<TKey, TValue>(
                    _dictionary.SetItem(key, value),
                    _items.SetItem(index, new KeyValuePair<TKey, TValue>(key, value))
                    );
            }

            public IImmutableDictionary<TKey, TValue> SetItems(IEnumerable<KeyValuePair<TKey, TValue>> items)
            {
                var itemList = _items;
                foreach (var item in items)
                {
                    var index = itemList.FindIndex(i => EqualityComparer<TKey>.Default.Equals(i.Key, item.Key));
                    itemList = itemList.SetItem(index, item);
                }

                return new ImmutableDictionaryKeepOrder<TKey, TValue>(
                    _dictionary.SetItems(items),
                    itemList
                    );
            }

            public bool TryGetKey(TKey equalKey, out TKey actualKey)
            {
                return _dictionary.TryGetKey(equalKey, out actualKey);
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                return _dictionary.TryGetValue(key, out value);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _items.GetEnumerator();
            }
        }

        private readonly Member memberA;
        private readonly Member memberB;
        private readonly Member memberC;

        private readonly IActorRef regionA;
        private readonly IActorRef regionB;
        private readonly IActorRef regionC;

        private readonly IImmutableList<string> shards = Enumerable.Range(1, 999).Select(n => n.ToString("000")).ToImmutableList();
        private readonly IShardAllocationStrategy strategyWithoutLimits;

        public LeastShardAllocationStrategySpec()
        {
            memberA = NewUpMember("127.0.0.1");
            memberB = NewUpMember("127.0.0.2");
            memberC = NewUpMember("127.0.0.3");

            regionA = NewFakeRegion("regionA", memberA);
            regionB = NewFakeRegion("regionB", memberB);
            regionC = NewFakeRegion("regionC", memberC);

            strategyWithoutLimits = StrategyWithFakeCluster(absoluteLimit: 1000, relativeLimit: 1.0);
        }

        internal class TestLeastShardAllocationStrategy : Internal.LeastShardAllocationStrategy
        {
            private readonly Func<CurrentClusterState> clusterState;
            private readonly Func<Member> selfMember;

            public TestLeastShardAllocationStrategy(int absoluteLimit, double relativeLimit, Func<CurrentClusterState> clusterState, Func<Member> selfMember) :
                base(absoluteLimit, relativeLimit)
            {
                this.clusterState = clusterState;
                this.selfMember = selfMember;
            }

            protected override CurrentClusterState ClusterState => clusterState();
            protected override Member SelfMember => selfMember();
        }

        private IShardAllocationStrategy StrategyWithFakeCluster(int absoluteLimit, double relativeLimit)
        {
            // we don't really "start" it as we fake the cluster access
            return new TestLeastShardAllocationStrategy(absoluteLimit, relativeLimit, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(memberA, memberB, memberC)), () => memberA);
        }

        private IImmutableDictionary<IActorRef, IImmutableList<string>> CreateAllocations(int aCount, int bCount = 0, int cCount = 0)
        {
            IImmutableDictionary<IActorRef, IImmutableList<string>> allocations = ImmutableDictionaryKeepOrder<IActorRef, IImmutableList<string>>.Empty;
            allocations = allocations.Add(regionA, shards.Take(aCount).ToImmutableList());
            allocations = allocations.Add(regionB, shards.Skip(aCount).Take(bCount).ToImmutableList());
            allocations = allocations.Add(regionC, shards.Skip(aCount + bCount).Take(cCount).ToImmutableList());
            return allocations;
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_allocate_to_region_with_least_number_of_shards()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 1, bCount: 1);
            allocationStrategy.AllocateShard(regionA, "003", allocations).Result.Should().Be(regionC);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_1_2_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 1, bCount: 2);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("002");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).Should().Equal(1, 1, 1);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_2_0_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 2);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("001");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).Should().Equal(1, 1, 0);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_shards_1_1_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 1, bCount: 1);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_3_0_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 3);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("001", "002");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).Should().Equal(1, 1, 1);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_4_4_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 4, bCount: 4);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("001", "005");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).Should().Equal(3, 3, 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_4_4_2()
        {
            // this is handled by phase 2, to find diff of 2
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 4, bCount: 4, cCount: 2);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("001");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).OrderBy(i => i).Should().Equal(new[] { 3, 4, 3 }.OrderBy(i => i));
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_5_5_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 5, bCount: 5);
            var result1 = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result1.Should().BeEquivalentTo("001", "006");

            // so far [4, 4, 2]
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result1).Should().Equal(4, 4, 2);
            var allocations2 = AfterRebalance(allocationStrategy, allocations, result1);
            // second phase will find the diff of 2, resulting in [3, 4, 3]
            var result2 = allocationStrategy.Rebalance(allocations2, ImmutableHashSet<string>.Empty).Result;
            result2.Should().BeEquivalentTo("002");
            AllocationCountsAfterRebalance(allocationStrategy, allocations2, result2).OrderBy(i => i).Should().Equal(new[] { 3, 4, 3 }.OrderBy(i => i));
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_rebalance_shards_50_50_0()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 50, cCount: 50);
            var result1 = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result1.Should().BeEquivalentTo(shards.Take(50 - 34).Union(shards.Skip(50).Take(50 - 34)));

            // so far [34, 34, 32]
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result1).OrderBy(i => i).Should().Equal(new[] { 34, 34, 32 }.OrderBy(i => i));
            var allocations2 = AfterRebalance(allocationStrategy, allocations, result1);
            // second phase will find the diff of 2, resulting in [33, 34, 33]
            var result2 = allocationStrategy.Rebalance(allocations2, ImmutableHashSet<string>.Empty).Result;
            result2.Should().BeEquivalentTo("017");
            AllocationCountsAfterRebalance(allocationStrategy, allocations2, result2).OrderBy(i => i).Should().Equal(new[] { 33, 34, 33 }.OrderBy(i => i));
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_respect_absolute_limit_of_number_shards()
        {
            var allocationStrategy = StrategyWithFakeCluster(absoluteLimit: 3, relativeLimit: 1.0);
            var allocations = CreateAllocations(aCount: 1, bCount: 9);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("002", "003", "004");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).Should().Equal(2, 6, 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_respect_relative_limit_of_number_shards()
        {
            var allocationStrategy = StrategyWithFakeCluster(absoluteLimit: 5, relativeLimit: 0.3);
            var allocations = CreateAllocations(aCount: 1, bCount: 9);
            var result = allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result;
            result.Should().BeEquivalentTo("002", "003", "004");
            AllocationCountsAfterRebalance(allocationStrategy, allocations, result).Should().Equal(2, 6, 2);
        }

        [Fact]
        public void LeastShardAllocationStrategy_must_not_rebalance_when_in_progress()
        {
            var allocationStrategy = strategyWithoutLimits;
            var allocations = CreateAllocations(aCount: 10);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("002", "003")).Result.Should().BeEmpty();
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
                new TestLeastShardAllocationStrategy(absoluteLimit: 1000, relativeLimit: 1.0, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(member1, member2)), () => member1);

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
                new TestLeastShardAllocationStrategy(absoluteLimit: 1000, relativeLimit: 1.0, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(member1, member2), unreachable: ImmutableHashSet.Create(member2)), () => member2);


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
                new TestLeastShardAllocationStrategy(absoluteLimit: 1000, relativeLimit: 1.0, () => new CurrentClusterState().Copy(members: ImmutableSortedSet.Create(member1, member2), unreachable: ImmutableHashSet.Create(member2)), () => member2);

            var allocations = CreateAllocations(aCount: 5, bCount: 5);
            allocationStrategy.Rebalance(allocations, ImmutableHashSet<string>.Empty).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002")).Result.Should().BeEmpty();
            allocationStrategy.Rebalance(allocations, ImmutableHashSet.Create("001", "002", "051", "052")).Result.Should().BeEmpty();
        }
    }
}

