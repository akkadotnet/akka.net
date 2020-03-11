//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRememberEntitiesNewExtractorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class ClusterShardingRememberEntitiesNewExtractorSpecConfig : MultiNodeConfig
    {
        public string Mode { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        protected ClusterShardingRememberEntitiesNewExtractorSpecConfig(string mode)
        {
            Mode = mode;
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString($@"
                    akka.actor {{
                        serializers {{
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }}
                        serialization-bindings {{
                            ""System.Object"" = hyperion
                        }}
                    }}
                    akka.loglevel = INFO
                    akka.actor.provider = cluster
                    akka.remote.log-remote-lifecycle-events = off
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
                    akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""
                    akka.persistence.journal.MemoryJournal {{
                        class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }}
                    akka.persistence.journal.memory-journal-shared {{
                        class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests.MultiNode""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                        timeout = 5s
                    }}
                    akka.cluster.sharding.state-store-mode = ""{mode}""
                    akka.cluster.sharding.distributed-data.durable.lmdb {{
                      dir = ""target/ClusterShardingMinMembersSpec/sharding-ddata""
                      map-size = 10000000
                    }}
                "))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            var roleConfig = ConfigurationFactory.ParseString(@"akka.cluster.roles = [sharding]");

            // we pretend node 4 and 5 are new incarnations of node 2 and 3 as they never run in parallel
            // so we can use the same lmdb store for them and have node 4 pick up the persisted data of node 2
            var ddataNodeAConfig = ConfigurationFactory.ParseString(@"
              akka.cluster.sharding.distributed-data.durable.lmdb {
                dir = ""target/ShardingRememberEntitiesNewExtractorSpec/sharding-node-a""
              }");
            var ddataNodeBConfig = ConfigurationFactory.ParseString(@"
              akka.cluster.sharding.distributed-data.durable.lmdb {
                dir = ""target/ShardingRememberEntitiesNewExtractorSpec/sharding-node-b""
              }");

            NodeConfig(new[] { Second }, new[] { roleConfig.WithFallback(ddataNodeAConfig) });
            NodeConfig(new[] { Third }, new[] { roleConfig.WithFallback(ddataNodeBConfig) });
        }
    }
    public class PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig : ClusterShardingRememberEntitiesNewExtractorSpecConfig
    {
        public PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig() : base("persistence") { }
    }
    public class DDataClusterShardingRememberEntitiesNewExtractorSpecConfig : ClusterShardingRememberEntitiesNewExtractorSpecConfig
    {
        public DDataClusterShardingRememberEntitiesNewExtractorSpecConfig() : base("ddata") { }
    }

    public class PersistentClusterShardingRememberEntitiesNewExtractorSpec : ClusterShardingRememberEntitiesNewExtractorSpec
    {
        public PersistentClusterShardingRememberEntitiesNewExtractorSpec() : this(new PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig()) { }
        protected PersistentClusterShardingRememberEntitiesNewExtractorSpec(PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig config) : base(config, typeof(PersistentClusterShardingRememberEntitiesNewExtractorSpec)) { }
    }
    public class DDataClusterShardingRememberEntitiesNewExtractorSpec : ClusterShardingRememberEntitiesNewExtractorSpec
    {
        public DDataClusterShardingRememberEntitiesNewExtractorSpec() : this(new DDataClusterShardingRememberEntitiesNewExtractorSpecConfig()) { }
        protected DDataClusterShardingRememberEntitiesNewExtractorSpec(DDataClusterShardingRememberEntitiesNewExtractorSpecConfig config) : base(config, typeof(DDataClusterShardingRememberEntitiesNewExtractorSpec)) { }
    }
    public abstract class ClusterShardingRememberEntitiesNewExtractorSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class Started
        {
            public readonly IActorRef Ref;
            public Started(IActorRef @ref)
            {
                Ref = @ref;
            }
        }

        internal class TestEntity : ActorBase
        {
            public TestEntity(IActorRef probe)
            {
                probe?.Tell(new Started(Self));
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        static readonly int ShardCount = 3;

        internal ExtractEntityId extractEntityId = message => message is int ? (message.ToString(), message) : Option<(string, object)>.None;

        internal static ExtractShardId extractShardId1 = message =>
        {
            switch (message)
            {
                case int msg:
                    return (msg % ShardCount).ToString();
                case ShardRegion.StartEntity msg:
                    return extractShardId1(msg.EntityId);
            }
            return null;
        };

        internal static ExtractShardId extractShardId2 = message =>
        {
            switch (message)
            {
                case int msg:
                    return ((msg + 1) % ShardCount).ToString();
                case ShardRegion.StartEntity msg:
                    return extractShardId2(msg.EntityId);
            }
            return null;
        };

        static readonly string TypeName = "Entity";

        private readonly ClusterShardingRememberEntitiesNewExtractorSpecConfig _config;
        private readonly List<FileInfo> _storageLocations;

        protected ClusterShardingRememberEntitiesNewExtractorSpec(ClusterShardingRememberEntitiesNewExtractorSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;
            _storageLocations = new List<FileInfo>
            {
                new FileInfo(Sys.Settings.Config.GetString("akka.cluster.sharding.distributed-data.durable.lmdb.dir", null))
            };

            IsDDataMode = config.Mode == "ddata";
            DeleteStorageLocations();
            EnterBarrier("startup");
        }
        protected bool IsDDataMode { get; }

        protected override void AfterTermination()
        {
            base.AfterTermination();
            DeleteStorageLocations();
        }

        private void DeleteStorageLocations()
        {
            foreach (var fileInfo in _storageLocations)
            {
                if (fileInfo.Exists) fileInfo.Delete();
            }
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(to));
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartShardingWithExtractor1()
        {
            ClusterSharding.Get(Sys).Start(
                typeName: TypeName,
                entityProps: Props.Create(() => new TestEntity(null)),
                settings: ClusterShardingSettings.Create(Sys).WithRememberEntities(true).WithRole("sharding"),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId1);
        }

        private void StartShardingWithExtractor2(ActorSystem sys, IActorRef probe)
        {
            ClusterSharding.Get(sys).Start(
                typeName: TypeName,
                entityProps: Props.Create(() => new TestEntity(probe)),
                settings: ClusterShardingSettings.Create(Sys).WithRememberEntities(true).WithRole("sharding"),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId2);
        }

        private IActorRef Region(ActorSystem sys)
        {
            return ClusterSharding.Get(sys).ShardRegion(TypeName);
        }

        [MultiNodeFact]
        public void Cluster_sharding_with_remember_entities_specs()
        {
            if (!IsDDataMode) Cluster_sharding_with_remember_entities_should_setup_shared_journal();
            Cluster_sharding_with_remember_entities_should_start_up_first_cluster_and_sharding();
            Cluster_sharding_with_remember_entities_should_shutdown_sharding_nodes();
            Cluster_sharding_with_remember_entities_should_start_new_nodes_with_different_extractor_and_have_the_entities_running_on_the_right_shards();
        }

        public void Cluster_sharding_with_remember_entities_should_setup_shared_journal()
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
            }, _config.Second, _config.Third);
            EnterBarrier("after-1");

            RunOn(() =>
            {
                //check persistence running
                var probe = CreateTestProbe();
                var journal = Persistence.Persistence.Instance.Get(Sys).JournalFor(null);
                journal.Tell(new Persistence.ReplayMessages(0, 0, long.MaxValue, Guid.NewGuid().ToString(), probe.Ref));
                probe.ExpectMsg<Persistence.RecoverySuccess>(TimeSpan.FromSeconds(10));
            }, _config.Second, _config.Third);
            EnterBarrier("after-1-test");
        }

        public void Cluster_sharding_with_remember_entities_should_start_up_first_cluster_and_sharding()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);
                Join(_config.Third, _config.First);

                RunOn(() =>
                {
                    Within(Remaining, () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.State.Members.Count.Should().Be(3);
                            Cluster.State.Members.Should().OnlyContain(i => i.Status == MemberStatus.Up);
                        });
                    });
                }, _config.First, _config.Second, _config.Third);

                RunOn(() =>
                {
                    StartShardingWithExtractor1();
                }, _config.Second, _config.Third);
                EnterBarrier("first-cluster-up");

                RunOn(() =>
                {
                    // one entity for each shard id
                    foreach (var n in Enumerable.Range(1, 10))
                    {
                        Region(Sys).Tell(n);
                        ExpectMsg(n);
                    }
                }, _config.Second, _config.Third);
                EnterBarrier("first-cluster-entities-up");
            });
        }

        public void Cluster_sharding_with_remember_entities_should_shutdown_sharding_nodes()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    TestConductor.Exit(_config.Second, 0).Wait();
                    TestConductor.Exit(_config.Third, 0).Wait();
                }, _config.First);

                RunOn(() =>
                {
                    Within(Remaining, () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.State.Members.Count.Should().Be(1);
                            Cluster.State.Members.Should().OnlyContain(i => i.Status == MemberStatus.Up);
                        });
                    });
                }, _config.First);

            });
            EnterBarrier("first-sharding-cluster-stopped");
        }

        public void Cluster_sharding_with_remember_entities_should_start_new_nodes_with_different_extractor_and_have_the_entities_running_on_the_right_shards()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // start it with a new shard id extractor, which will put the entities
                // on different shards

                RunOn(() =>
                {
                    Watch(Region(Sys));
                    Cluster.Get(Sys).Leave(Cluster.Get(Sys).SelfAddress);
                    ExpectTerminated(Region(Sys));
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).IsTerminated.Should().BeTrue();
                    });

                }, _config.Second, _config.Third);
                EnterBarrier("first-cluster-terminated");

                // no sharding nodes left of the original cluster, start a new nodes
                RunOn(() =>
                {
                    var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
                    var probe2 = CreateTestProbe(sys2);

                    if (!IsDDataMode)
                    {
                        // setup Persistence
                        Persistence.Persistence.Instance.Apply(sys2);
                        sys2.ActorSelection(Node(_config.First) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null), probe2.Ref);
                        var sharedStore = probe2.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                        sharedStore.Should().NotBeNull();

                        MemoryJournalShared.SetStore(sharedStore, sys2);
                    }

                    Cluster.Get(sys2).Join(Node(_config.First).Address);
                    StartShardingWithExtractor2(sys2, probe2.Ref);
                    probe2.ExpectMsg<Started>(TimeSpan.FromSeconds(20));

                    CurrentShardRegionState stats = null;
                    Within(TimeSpan.FromSeconds(10), () =>
                    {
                        AwaitAssert(() =>
                        {
                            Region(sys2).Tell(GetShardRegionState.Instance);
                            var reply = ExpectMsg<CurrentShardRegionState>();
                            reply.Shards.Should().NotBeEmpty();
                            stats = reply;
                        });
                    });

                    foreach (var shardState in stats.Shards)
                    {
                        foreach (var entityId in shardState.EntityIds)
                        {
                            var calculatedShardId = extractShardId2(int.Parse(entityId));
                            calculatedShardId.ShouldAllBeEquivalentTo(shardState.ShardId);
                        }
                    }

                    EnterBarrier("verified");
                    Shutdown(sys2);
                }, _config.Second, _config.Third);

                RunOn(() =>
                {
                    EnterBarrier("verified");
                }, _config.First);

                EnterBarrier("done");
            });
        }
    }
}
