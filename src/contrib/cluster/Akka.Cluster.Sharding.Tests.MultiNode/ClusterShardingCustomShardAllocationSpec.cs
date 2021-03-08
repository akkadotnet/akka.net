//-----------------------------------------------------------------------
// <copyright file="ClusterShardingCustomShardAllocationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingCustomShardAllocationSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingCustomShardAllocationSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
                akka.cluster.sharding.rebalance-interval = 1 s
            ")
        {
            First = Role("first");
            Second = Role("second");
        }
    }

    public class PersistentClusterShardingCustomShardAllocationSpecConfig : ClusterShardingCustomShardAllocationSpecConfig
    {
        public PersistentClusterShardingCustomShardAllocationSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingCustomShardAllocationSpecConfig : ClusterShardingCustomShardAllocationSpecConfig
    {
        public DDataClusterShardingCustomShardAllocationSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingCustomShardAllocationSpec : ClusterShardingCustomShardAllocationSpec
    {
        public PersistentClusterShardingCustomShardAllocationSpec()
            : base(new PersistentClusterShardingCustomShardAllocationSpecConfig(), typeof(PersistentClusterShardingCustomShardAllocationSpec))
        {
        }
    }

    public class DDataClusterShardingCustomShardAllocationSpec : ClusterShardingCustomShardAllocationSpec
    {
        public DDataClusterShardingCustomShardAllocationSpec()
            : base(new DDataClusterShardingCustomShardAllocationSpecConfig(), typeof(DDataClusterShardingCustomShardAllocationSpec))
        {
        }
    }

    public abstract class ClusterShardingCustomShardAllocationSpec : MultiNodeClusterShardingSpec<ClusterShardingCustomShardAllocationSpecConfig>
    {
        #region setup

        internal class AllocateReq
        {
            public static readonly AllocateReq Instance = new AllocateReq();

            private AllocateReq()
            {
            }
        }

        internal class UseRegion
        {
            public readonly IActorRef Region;

            public UseRegion(IActorRef region)
            {
                Region = region;
            }
        }

        internal class UseRegionAck
        {
            public static readonly UseRegionAck Instance = new UseRegionAck();

            private UseRegionAck()
            {
            }
        }

        internal class RebalanceReq
        {
            public static readonly RebalanceReq Instance = new RebalanceReq();

            private RebalanceReq()
            {
            }
        }

        internal class RebalanceShards
        {
            public readonly IImmutableSet<string> Shards;

            public RebalanceShards(IImmutableSet<string> shards)
            {
                Shards = shards;
            }
        }

        internal class RebalanceShardsAck
        {
            public static readonly RebalanceShardsAck Instance = new RebalanceShardsAck();

            private RebalanceShardsAck()
            {
            }
        }

        internal class Allocator : ActorBase
        {
            IActorRef UseRegion;
            IImmutableSet<string> Rebalance = ImmutableHashSet<string>.Empty;

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case UseRegion r:
                        UseRegion = r.Region;
                        Sender.Tell(UseRegionAck.Instance);
                        return true;
                    case AllocateReq _:
                        if (UseRegion != null)
                            Sender.Tell(UseRegion);
                        return true;
                    case RebalanceShards rs:
                        Rebalance = rs.Shards;
                        Sender.Tell(RebalanceShardsAck.Instance);
                        return true;
                    case RebalanceReq _:
                        Sender.Tell(Rebalance);
                        Rebalance = ImmutableHashSet<string>.Empty;
                        return true;
                }
                return false;
            }
        }

        internal class TestAllocationStrategy : IShardAllocationStrategy
        {
            public readonly IActorRef Ref;

            public TestAllocationStrategy(IActorRef @ref)
            {
                Ref = @ref;
            }

            public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations)
            {
                return Ref.Ask<IActorRef>(AllocateReq.Instance);
            }

            public Task<IImmutableSet<string>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations, IImmutableSet<string> rebalanceInProgress)
            {
                return Ref.Ask<IImmutableSet<string>>(RebalanceReq.Instance);
            }
        }

        private readonly Lazy<IActorRef> _region;
        private readonly Lazy<IActorRef> _allocator;

        protected ClusterShardingCustomShardAllocationSpec(ClusterShardingCustomShardAllocationSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
            _allocator = new Lazy<IActorRef>(() => Sys.ActorOf(Props.Create<Allocator>(), "allocator"));
        }

        private void Join(RoleName from, RoleName to)
        {
            Join(from, to, () =>
                StartSharding(
                    Sys,
                    typeName: "Entity",
                    entityProps: SimpleEchoActor.Props(),
                    extractEntityId: IntExtractEntityId,
                    extractShardId: IntExtractShardId,
                    allocationStrategy: new TestAllocationStrategy(_allocator.Value))
                );
        }

        #endregion

        [MultiNodeFact]
        public void Cluster_sharding_with_custom_allocation_strategy_specs()
        {
            Cluster_sharding_with_custom_allocation_strategy_must_use_specified_region();
            Cluster_sharding_with_custom_allocation_strategy_must_rebalance_specified_shards();
        }

        private void Cluster_sharding_with_custom_allocation_strategy_must_use_specified_region()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.First, config.Second);

                Join(config.First, config.First);

                RunOn(() =>
                {
                    _allocator.Value.Tell(new UseRegion(_region.Value));
                    ExpectMsg<UseRegionAck>();
                    _region.Value.Tell(1);
                    ExpectMsg(1);
                    LastSender.Path.Should().Be(_region.Value.Path / "1" / "1");
                }, config.First);
                EnterBarrier("first-started");

                Join(config.Second, config.First);

                _region.Value.Tell(2);
                ExpectMsg(2);
                RunOn(() =>
                {
                    LastSender.Path.Should().Be(_region.Value.Path / "2" / "2");
                }, config.First);
                RunOn(() =>
                {
                    LastSender.Path.Should().Be(Node(config.First) / "system" / "sharding" / "Entity" / "2" / "2");
                }, config.Second);
                EnterBarrier("second-started");

                RunOn(() =>
                {
                    Sys.ActorSelection(Node(config.Second) / "system" / "sharding" / "Entity").Tell(new Identify(null));
                    var secondRegion = ExpectMsg<ActorIdentity>().Subject;
                    _allocator.Value.Tell(new UseRegion(secondRegion));
                    ExpectMsg<UseRegionAck>();
                }, config.First);
                EnterBarrier("second-active");

                _region.Value.Tell(3);
                ExpectMsg(3);
                RunOn(() =>
                {
                    LastSender.Path.Should().Be(_region.Value.Path / "3" / "3");
                }, config.Second);

                RunOn(() =>
                {
                    LastSender.Path.Should().Be(Node(config.Second) / "system" / "sharding" / "Entity" / "3" / "3");
                }, config.First);

                EnterBarrier("after-2");
            });
        }

        private void Cluster_sharding_with_custom_allocation_strategy_must_rebalance_specified_shards()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    _allocator.Value.Tell(new RebalanceShards(ImmutableHashSet.Create("2")));
                    ExpectMsg<RebalanceShardsAck>();

                    AwaitAssert(() =>
                    {
                        var p = CreateTestProbe();
                        _region.Value.Tell(2, p.Ref);
                        p.ExpectMsg(2, TimeSpan.FromSeconds(2));

                        p.LastSender.Path.Should().Be(Node(config.Second) / "system" / "sharding" / "Entity" / "2" / "2");
                    });

                    _region.Value.Tell(1);
                    ExpectMsg(1);
                    LastSender.Path.Should().Be(_region.Value.Path / "1" / "1");
                }, config.First);
                EnterBarrier("after-2");
            });
        }
    }
}
