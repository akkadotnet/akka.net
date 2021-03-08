//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMinMembersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingMinMembersSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingMinMembersSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.sharding.rebalance-interval = 120s #disable rebalance
            akka.cluster.min-nr-of-members = 3
            ")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
        }
    }

    public class PersistentClusterShardingMinMembersSpecConfig : ClusterShardingMinMembersSpecConfig
    {
        public PersistentClusterShardingMinMembersSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingMinMembersSpecConfig : ClusterShardingMinMembersSpecConfig
    {
        public DDataClusterShardingMinMembersSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingMinMembersSpec : ClusterShardingMinMembersSpec
    {
        public PersistentClusterShardingMinMembersSpec()
            : base(new PersistentClusterShardingMinMembersSpecConfig(), typeof(PersistentClusterShardingMinMembersSpec))
        {
        }
    }

    public class DDataClusterShardingMinMembersSpec : ClusterShardingMinMembersSpec
    {
        public DDataClusterShardingMinMembersSpec()
            : base(new DDataClusterShardingMinMembersSpecConfig(), typeof(DDataClusterShardingMinMembersSpec))
        {
        }
    }

    public abstract class ClusterShardingMinMembersSpec : MultiNodeClusterShardingSpec<ClusterShardingMinMembersSpecConfig>
    {
        #region setup

        private readonly Lazy<IActorRef> _region;

        protected ClusterShardingMinMembersSpec(ClusterShardingMinMembersSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        private void StartSharding()
        {
            StartSharding(
                Sys,
                typeName: "Entity",
                entityProps: SimpleEchoActor.Props(),
                extractEntityId: IntExtractEntityId,
                extractShardId: IntExtractShardId,
                allocationStrategy: ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 2, relativeLimit: 1.0),
                handOffStopMessage: ShardedEntity.Stop.Instance);
        }

        #endregion

        [MultiNodeFact]
        public void Cluster_with_min_nr_of_members_using_sharding_specs()
        {
            Cluster_with_min_nr_of_members_using_sharding_must_use_all_nodes();
        }

        private void Cluster_with_min_nr_of_members_using_sharding_must_use_all_nodes()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.First, config.Second, config.Third);

                // the only test not asserting join status before starting to shard
                Join(config.First, config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);
                Join(config.Second, config.First, onJoinedRunOnFrom: StartSharding, assertNodeUp: false);
                Join(config.Third, config.First, assertNodeUp: false);
                // wait with starting sharding on third
                Within(Remaining, () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count.Should().Be(3);
                        Cluster.State.Members.Should().OnlyContain(i => i.Status == MemberStatus.Up);
                    });
                });
                EnterBarrier("all-up");

                RunOn(() =>
                {
                    _region.Value.Tell(1);
                    // not allocated because third has not registered yet
                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                }, config.First);
                EnterBarrier("verified");

                RunOn(() =>
                {
                    StartSharding();
                }, config.Third);

                RunOn(() =>
                {
                    // the 1 was sent above
                    ExpectMsg(1);
                    _region.Value.Tell(2);
                    ExpectMsg(2);
                    _region.Value.Tell(3);
                    ExpectMsg(3);
                }, config.First);
                EnterBarrier("shards-allocated");

                _region.Value.Tell(new GetClusterShardingStats(Remaining));
                var stats = ExpectMsg<ClusterShardingStats>();
                var firstAddress = Node(config.First).Address;
                var secondAddress = Node(config.Second).Address;
                var thirdAddress = Node(config.Third).Address;

                stats.Regions.Keys.Should().BeEquivalentTo(firstAddress, secondAddress, thirdAddress);
                stats.Regions[firstAddress].Stats.Values.Sum().Should().Be(1);
                EnterBarrier("after-2");
            });
        }
    }
}
