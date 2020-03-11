//-----------------------------------------------------------------------
// <copyright file="ClusterShardingCustomShardAllocationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingCustomShardAllocationSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingCustomShardAllocationSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.actor {
                        serializers {
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }
                        serialization-bindings {
                            ""System.Object"" = hyperion
                        }
                    }

                    akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                    akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""

                    akka.persistence.journal.MemoryJournal {
                        class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }

                    akka.persistence.journal.memory-journal-shared {
                        class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests.MultiNode""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        timeout = 5s
                    }
                "))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterShardingCustomShardAllocationSpec : MultiNodeClusterSpec
    {
        #region setup

        internal class Entity : ActorBase
        {
            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case int id:
                        Sender.Tell(id);
                        return true;
                }
                return false;
            }
        }

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

        class TestAllocationStrategy : IShardAllocationStrategy
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

        internal ExtractEntityId extractEntityId = message => message is int ? (message.ToString(), message) : Option<(string, object)>.None;

        internal ExtractShardId extractShardId = message => message is int ? message.ToString() : null;

        private Lazy<IActorRef> _region;
        private Lazy<IActorRef> _allocator;

        private readonly ClusterShardingCustomShardAllocationSpecConfig _config;

        public ClusterShardingCustomShardAllocationSpec()
            : this(new ClusterShardingCustomShardAllocationSpecConfig())
        {
        }

        protected ClusterShardingCustomShardAllocationSpec(ClusterShardingCustomShardAllocationSpecConfig config)
            : base(config, typeof(ClusterShardingCustomShardAllocationSpec))
        {
            _config = config;

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));

            _allocator = new Lazy<IActorRef>(() => Sys.ActorOf(Props.Create<Allocator>(), "allocator"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(to));
                StartSharding();
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding()
        {
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                allocationStrategy: new TestAllocationStrategy(_allocator.Value),
                handOffStopMessage: PoisonPill.Instance);
        }

        [MultiNodeFact]
        public void Cluster_sharding_with_custom_allocation_strategy_specs()
        {
            Cluster_sharding_with_custom_allocation_strategy_should_setup_shared_journal();
            Cluster_sharding_with_custom_allocation_strategy_should_use_specified_region();
            Cluster_sharding_with_custom_allocation_strategy_should_rebalance_specified_shards();
        }

        public void Cluster_sharding_with_custom_allocation_strategy_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.MemoryJournal");
            }, _config.First);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_config.First) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null));
                var sharedStore = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                sharedStore.Should().NotBeNull();

                MemoryJournalShared.SetStore(sharedStore, Sys);
            }, _config.First, _config.Second);
            EnterBarrier("after-1");

            RunOn(() =>
            {
                //check persistence running
                var probe = CreateTestProbe();
                var journal = Persistence.Persistence.Instance.Get(Sys).JournalFor(null);
                journal.Tell(new Persistence.ReplayMessages(0, 0, long.MaxValue, Guid.NewGuid().ToString(), probe.Ref));
                probe.ExpectMsg<Persistence.RecoverySuccess>(TimeSpan.FromSeconds(10));
            }, _config.First, _config.Second);
            EnterBarrier("after-1-test");
        }

        public void Cluster_sharding_with_custom_allocation_strategy_should_use_specified_region()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                Join(_config.First, _config.First);

                RunOn(() =>
                {
                    _allocator.Value.Tell(new UseRegion(_region.Value));
                    ExpectMsg<UseRegionAck>();
                    _region.Value.Tell(1);
                    ExpectMsg(1);
                    LastSender.Path.Should().Be(_region.Value.Path / "1" / "1");
                }, _config.First);
                EnterBarrier("first-started");

                Join(_config.Second, _config.First);

                _region.Value.Tell(2);
                ExpectMsg(2);

                RunOn(() =>
                {
                    LastSender.Path.Should().Be(_region.Value.Path / "2" / "2");
                }, _config.First);
                RunOn(() =>
                {
                    LastSender.Path.Should().Be(Node(_config.First) / "system" / "sharding" / "Entity" / "2" / "2");
                }, _config.Second);
                EnterBarrier("second-started");

                RunOn(() =>
                {
                    Sys.ActorSelection(Node(_config.Second) / "system" / "sharding" / "Entity").Tell(new Identify(null));
                    var secondRegion = ExpectMsg<ActorIdentity>().Subject;
                    _allocator.Value.Tell(new UseRegion(secondRegion));
                    ExpectMsg<UseRegionAck>();
                }, _config.First);
                EnterBarrier("second-active");

                _region.Value.Tell(3);
                ExpectMsg(3);
                RunOn(() =>
                {
                    LastSender.Path.Should().Be(_region.Value.Path / "3" / "3");
                }, _config.Second);

                RunOn(() =>
                {
                    LastSender.Path.Should().Be(Node(_config.Second) / "system" / "sharding" / "Entity" / "3" / "3");
                }, _config.First);

                EnterBarrier("after-2");
            });
        }

        public void Cluster_sharding_with_custom_allocation_strategy_should_rebalance_specified_shards()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    _allocator.Value.Tell(new RebalanceShards(new string[] { "2" }.ToImmutableHashSet()));
                    ExpectMsg<RebalanceShardsAck>();

                    AwaitAssert(() =>
                    {
                        var p = CreateTestProbe();
                        _region.Value.Tell(2, p.Ref);
                        p.ExpectMsg(2, TimeSpan.FromSeconds(2));

                        p.LastSender.Path.Should().Be(Node(_config.Second) / "system" / "sharding" / "Entity" / "2" / "2");
                    });

                    _region.Value.Tell(1);
                    ExpectMsg(1);
                    LastSender.Path.Should().Be(_region.Value.Path / "1" / "1");
                }, _config.First);
                EnterBarrier("after-2");
            });
        }
    }
}
