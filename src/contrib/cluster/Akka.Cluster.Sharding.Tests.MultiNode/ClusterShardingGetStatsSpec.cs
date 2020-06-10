//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGetStatsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGetStatsSpecConfig : MultiNodeConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingGetStatsSpecConfig()
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

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
                    akka.loglevel = INFO
                    akka.actor.provider = cluster
                    akka.remote.log-remote-lifecycle-events = off
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.sharding {
                        updating-state-timeout = 2s
                        waiting-for-state-timeout = 2s
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

            NodeConfig(new RoleName[] { First, Second, Third }, new Config[] {
                ConfigurationFactory.ParseString(@"akka.cluster.roles=[""shard""]")
            });
        }
    }

    public class ClusterShardingGetStatsSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class Stop
        {
            public static readonly Stop Instance = new Stop();
            private Stop()
            {
            }
        }

        [Serializable]
        internal sealed class Ping
        {
            public readonly int Id;

            public Ping(int id)
            {
                Id = id;
            }
        }

        [Serializable]
        internal sealed class Pong
        {
            public static readonly Pong Instance = new Pong();
            private Pong()
            {
            }
        }

        internal class ShardedActor : ActorBase
        {
            public ShardedActor()
            {
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Stop _:
                        Context.Stop(Self);
                        return true;
                    case Ping p:
                        Sender.Tell(Pong.Instance);
                        return true;
                }
                return false;
            }
        }

        readonly static int NumberOfShards = 3;
        readonly static string ShardTypeName = "Ping";

        internal ExtractEntityId extractEntityId = message => message is Ping p ? (p.Id.ToString(), message) : Option<(string, object)>.None;

        internal ExtractShardId extractShardId = message => message is Ping p ? (p.Id % NumberOfShards).ToString() : null;

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingGetStatsSpecConfig _config;

        public ClusterShardingGetStatsSpec()
            : this(new ClusterShardingGetStatsSpecConfig())
        {
        }

        protected ClusterShardingGetStatsSpec(ClusterShardingGetStatsSpecConfig config)
            : base(config, typeof(ClusterShardingGetStatsSpec))
        {
            _config = config;

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion(ShardTypeName));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        #endregion

        private void Join(RoleName from)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.Controller));
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartShard()
        {
            ClusterSharding.Get(Sys).Start(
               typeName: ShardTypeName,
               entityProps: Props.Create<ShardedActor>(),
               settings: ClusterShardingSettings.Create(Sys).WithRole("shard"),
               extractEntityId: extractEntityId,
               extractShardId: extractShardId);
        }

        private void StartProxy()
        {
            ClusterSharding.Get(Sys).StartProxy(
                typeName: ShardTypeName,
                role: "shard",
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        [MultiNodeFact]
        public void Inspecting_cluster_sharding_state_specs()
        {
            Inspecting_cluster_sharding_state_should_setup_shared_journal();
            Inspecting_cluster_sharding_state_should_join_cluster();
            Inspecting_cluster_sharding_state_should_return_empty_state_when_no_sharded_actors_has_started();
            Inspecting_cluster_sharding_state_should_trigger_sharded_actors();
            Inspecting_cluster_sharding_state_should_get_shard_state();
            Inspecting_cluster_sharding_state_should_return_stats_after_a_node_leaves();
        }

        public void Inspecting_cluster_sharding_state_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.MemoryJournal");
            }, _config.Controller);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_config.Controller) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null));
                var sharedStore = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                sharedStore.Should().NotBeNull();

                MemoryJournalShared.SetStore(sharedStore, Sys);
            }, _config.First, _config.Second, _config.Third);
            EnterBarrier("after-1");

            RunOn(() =>
            {
                //check persistence running
                var probe = CreateTestProbe();
                var journal = Persistence.Persistence.Instance.Get(Sys).JournalFor(null);
                journal.Tell(new Persistence.ReplayMessages(0, 0, long.MaxValue, Guid.NewGuid().ToString(), probe.Ref));
                probe.ExpectMsg<Persistence.RecoverySuccess>(TimeSpan.FromSeconds(10));
            }, _config.First, _config.Second, _config.Third);
            EnterBarrier("after-1-test");
        }

        public void Inspecting_cluster_sharding_state_should_join_cluster()
        {
            Join(_config.Controller);
            Join(_config.First);
            Join(_config.Second);
            Join(_config.Third);

            // make sure all nodes are up
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(Sys).State.Members.Count(i => i.Status == MemberStatus.Up).Should().Be(4);
                });
            });

            RunOn(() =>
            {
                StartProxy();
            }, _config.Controller);

            RunOn(() =>
            {
                StartShard();
            }, _config.First, _config.Second, _config.Third);

            EnterBarrier("sharding started");
        }

        public void Inspecting_cluster_sharding_state_should_return_empty_state_when_no_sharded_actors_has_started()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    _region.Value.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(10))), probe.Ref);
                    var shardStats = probe.ExpectMsg<ClusterShardingStats>();
                    shardStats.Regions.Count.Should().Be(3);
                    shardStats.Regions.Values.Sum(i => i.Stats.Count).Should().Be(0);
                    shardStats.Regions.Keys.Should().OnlyContain(i => i.HasGlobalScope);
                });
            });

            EnterBarrier("empty sharding");
        }

        public void Inspecting_cluster_sharding_state_should_trigger_sharded_actors()
        {
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        var pingProbe = CreateTestProbe();


                        // trigger starting of 2 entities on first and second node
                        // but leave third node without entities
                        foreach (var n in new int[] { 1, 2, 4, 6 })
                        {
                            _region.Value.Tell(new Ping(n), pingProbe.Ref);
                        }
                        pingProbe.ReceiveWhile(null, m => (Pong)m, 4);
                    });
                });
            }, _config.Controller);

            EnterBarrier("sharded actors started");
        }

        public void Inspecting_cluster_sharding_state_should_get_shard_state()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    _region.Value.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(10))), probe.Ref);
                    var regions = probe.ExpectMsg<ClusterShardingStats>().Regions;
                    regions.Count.Should().Be(3);
                    regions.Values.SelectMany(i => i.Stats.Values).Sum().Should().Be(4);
                    regions.Keys.Should().OnlyContain(i => i.HasGlobalScope);
                });
            });

            EnterBarrier("got shard state");
        }

        public void Inspecting_cluster_sharding_state_should_return_stats_after_a_node_leaves()
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Leave(Node(_config.Third).Address);
            }, _config.Controller);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).State.Members.Count.Should().Be(3);
                    });
                });
            }, _config.First, _config.Second);

            EnterBarrier("third node removed");


            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        var pingProbe = CreateTestProbe();
                        // make sure we have the 4 entities still alive across the fewer nodes
                        foreach (var n in new int[] { 1, 2, 4, 6 })
                        {
                            _region.Value.Tell(new Ping(n), pingProbe.Ref);
                        }
                        pingProbe.ReceiveWhile(null, m => (Pong)m, 4);
                    });
                });
            }, _config.Controller);

            EnterBarrier("shards revived");


            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        var probe = CreateTestProbe();
                        _region.Value.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(20))), probe.Ref);
                        var regions = probe.ExpectMsg<ClusterShardingStats>().Regions;
                        regions.Count.Should().Be(2);
                        regions.Values.SelectMany(i => i.Stats.Values).Sum().Should().Be(4);
                    });
                });
            }, _config.Controller);

            EnterBarrier("done");
        }
    }
}
