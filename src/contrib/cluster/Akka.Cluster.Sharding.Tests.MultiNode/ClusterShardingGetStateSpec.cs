//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGetStateSpec.cs" company="Akka.NET Project">
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
using System.Collections.Immutable;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGetStateSpecConfig : MultiNodeConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingGetStateSpecConfig()
        {
            Controller = Role("controller");
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
                    akka.loglevel = INFO
                    akka.actor.provider = cluster
                    akka.remote.log-remote-lifecycle-events = off
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.sharding {
                        coordinator-failure-backoff = 3s
                        shard-failure-backoff = 3s
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

            NodeConfig(new RoleName[] { First, Second }, new Config[] {
                ConfigurationFactory.ParseString(@"akka.cluster.roles=[""shard""]")
            });
        }
    }

    public class ClusterShardingGetStateSpec : MultiNodeClusterSpec
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

        readonly static int NumberOfShards = 2;
        readonly static string ShardTypeName = "Ping";

        internal ExtractEntityId extractEntityId = message => message is Ping p ? (p.Id.ToString(), message) : Option<(string, object)>.None;

        internal ExtractShardId extractShardId = message => message is Ping p ? (p.Id % NumberOfShards).ToString() : null;

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingGetStateSpecConfig _config;

        public ClusterShardingGetStateSpec()
            : this(new ClusterShardingGetStateSpecConfig())
        {
        }

        protected ClusterShardingGetStateSpec(ClusterShardingGetStateSpecConfig config)
            : base(config, typeof(ClusterShardingGetStateSpec))
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

        public void Inspecting_cluster_sharding_state_should_join_cluster()
        {
            Join(_config.Controller);
            Join(_config.First);
            Join(_config.Second);

            // make sure all nodes are up
            AwaitAssert(() =>
            {
                Cluster.Get(Sys).SendCurrentClusterState(TestActor);
                ExpectMsg<ClusterEvent.CurrentClusterState>().Members.Count.Should().Be(3);
            });

            RunOn(() =>
            {
                StartProxy();
            }, _config.Controller);

            RunOn(() =>
            {
                StartShard();
            }, _config.First, _config.Second);

            EnterBarrier("sharding started");
        }

        public void Inspecting_cluster_sharding_state_should_return_empty_state_when_no_sharded_actors_has_started()
        {
            AwaitAssert(() =>
            {
                var probe = CreateTestProbe();
                _region.Value.Tell(GetCurrentRegions.Instance, probe.Ref);
                probe.ExpectMsg<CurrentRegions>().Regions.Count.Should().Be(0);
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
                        // trigger starting of 4 entities
                        foreach (var n in Enumerable.Range(1, 4))
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
                    _region.Value.Tell(GetCurrentRegions.Instance, probe.Ref);
                    var regions = probe.ExpectMsg<CurrentRegions>().Regions;
                    regions.Count.Should().Be(2);

                    foreach (var region in regions)
                    {
                        var path = new RootActorPath(region) / "system" / "sharding" / ShardTypeName;
                        Sys.ActorSelection(path).Tell(GetShardRegionState.Instance, probe.Ref);
                    }

                    var states = probe.ReceiveWhile(null, m => (CurrentShardRegionState)m, regions.Count);
                    var allEntityIds = states.SelectMany(i => i.Shards).SelectMany(j => j.EntityIds).ToImmutableHashSet();
                    allEntityIds.Should().BeEquivalentTo(new string[] { "1", "2", "3", "4" });
                });
            });

            EnterBarrier("done");
        }
    }
}
