//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRememberEntitiesNewExtractorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingRememberEntitiesNewExtractorSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingRememberEntitiesNewExtractorSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            var roleConfig = ConfigurationFactory.ParseString(@"akka.cluster.roles = [sharding]");

            // we pretend node 4 and 5 are new incarnations of node 2 and 3 as they never run in parallel
            // so we can use the same lmdb store for them and have node 4 pick up the persisted data of node 2
            var ddataNodeAConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.sharding.distributed-data.durable.lmdb {
                    dir = ""target/ShardingRememberEntitiesNewExtractorSpec/sharding-node-a""
                }
                ");
            var ddataNodeBConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.sharding.distributed-data.durable.lmdb {
                dir = ""target/ShardingRememberEntitiesNewExtractorSpec/sharding-node-b""
                }
                ");

            NodeConfig(new[] { Second }, new[] { roleConfig.WithFallback(ddataNodeAConfig) });
            NodeConfig(new[] { Third }, new[] { roleConfig.WithFallback(ddataNodeBConfig) });
        }
    }

    public class PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig : ClusterShardingRememberEntitiesNewExtractorSpecConfig
    {
        public PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingRememberEntitiesNewExtractorSpecConfig : ClusterShardingRememberEntitiesNewExtractorSpecConfig
    {
        public DDataClusterShardingRememberEntitiesNewExtractorSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingRememberEntitiesNewExtractorSpec : ClusterShardingRememberEntitiesNewExtractorSpec
    {
        public PersistentClusterShardingRememberEntitiesNewExtractorSpec()
            : base(new PersistentClusterShardingRememberEntitiesSpecNewExtractorConfig(), typeof(PersistentClusterShardingRememberEntitiesNewExtractorSpec))
        {
        }
    }

    public class DDataClusterShardingRememberEntitiesNewExtractorSpec : ClusterShardingRememberEntitiesNewExtractorSpec
    {
        public DDataClusterShardingRememberEntitiesNewExtractorSpec()
            : base(new DDataClusterShardingRememberEntitiesNewExtractorSpecConfig(), typeof(DDataClusterShardingRememberEntitiesNewExtractorSpec))
        {
        }
    }

    public abstract class ClusterShardingRememberEntitiesNewExtractorSpec : MultiNodeClusterShardingSpec<ClusterShardingRememberEntitiesNewExtractorSpecConfig>
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
            private readonly ILoggingAdapter log = Context.GetLogger();

            public TestEntity(IActorRef probe)
            {
                log.Info("Entity started: " + Self.Path);
                probe?.Tell(new Started(Self));
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        private static ExtractEntityId extractEntityId = message =>
        {
            switch (message)
            {
                case int id:
                    return (id.ToString(), id);
            }
            return Option<(string, object)>.None;
        };

        private static ExtractShardId extractShardId1 = message =>
        {
            switch (message)
            {
                case int id:
                    return (id % ShardCount).ToString();
                case ShardRegion.StartEntity msg:
                    return extractShardId1(msg.EntityId);
            }
            return null;
        };

        private static ExtractShardId extractShardId2 = message =>
        {
            // always bump it one shard id
            switch (message)
            {
                case int id:
                    return ((id + 1) % ShardCount).ToString();
                case ShardRegion.StartEntity msg:
                    return extractShardId2(msg.EntityId);
            }
            return null;
        };

        private const int ShardCount = 3;
        private const string TypeName = "Entity";

        protected ClusterShardingRememberEntitiesNewExtractorSpec(ClusterShardingRememberEntitiesNewExtractorSpecConfig config, Type type)
            : base(config, type)
        {
        }

        private void StartShardingWithExtractor1()
        {
            StartSharding(
                Sys,
                typeName: TypeName,
                entityProps: Props.Create(() => new TestEntity(null)),
                settings: settings.Value.WithRole("sharding"),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId1);
        }

        private void StartShardingWithExtractor2(ActorSystem sys, IActorRef probe)
        {
            StartSharding(
                sys,
                typeName: TypeName,
                entityProps: Props.Create(() => new TestEntity(probe)),
                settings: ClusterShardingSettings.Create(sys).WithRememberEntities(config.RememberEntities).WithRole("sharding"),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId2);
        }

        private IActorRef Region(ActorSystem sys = null)
        {
            return ClusterSharding.Get(sys ?? Sys).ShardRegion(TypeName);
        }


        #endregion

        [MultiNodeFact]
        public void Cluster_sharding_with_remember_entities_specs()
        {
            Cluster_with_min_nr_of_members_using_sharding_must_start_up_first_cluster_and_sharding();
            Cluster_with_min_nr_of_members_using_sharding_must_shutdown_sharding_nodes();
            Cluster_with_min_nr_of_members_using_sharding_must_start_new_nodes_with_different_extractor_and_have_the_entities_running_on_the_right_shards();
        }

        private void Cluster_with_min_nr_of_members_using_sharding_must_start_up_first_cluster_and_sharding()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.Second, config.Third);

                Join(config.First, config.First);
                Join(config.Second, config.First);
                Join(config.Third, config.First);

                RunOn(() =>
                {
                    Within(Remaining, () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3);
                        });
                    });
                }, config.First, config.Second, config.Third);

                RunOn(() =>
                {
                    StartShardingWithExtractor1();
                }, config.Second, config.Third);
                EnterBarrier("first-cluster-up");

                RunOn(() =>
                {
                    // one entity for each shard id
                    foreach (var n in Enumerable.Range(1, 10))
                    {
                        Region().Tell(n);
                        ExpectMsg(n);
                    }
                }, config.Second, config.Third);
                EnterBarrier("first-cluster-entities-up");
            });
        }

        private void Cluster_with_min_nr_of_members_using_sharding_must_shutdown_sharding_nodes()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    TestConductor.Exit(config.Second, 0).Wait();
                    TestConductor.Exit(config.Third, 0).Wait();
                }, config.First);

                RunOn(() =>
                {
                    Within(Remaining, () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
                        });
                    });
                }, config.First);

            });
            EnterBarrier("first-sharding-cluster-stopped");
        }

        private void Cluster_with_min_nr_of_members_using_sharding_must_start_new_nodes_with_different_extractor_and_have_the_entities_running_on_the_right_shards()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // start it with a new shard id extractor, which will put the entities
                // on different shards

                RunOn(() =>
                {
                    Watch(Region());
                    Cluster.Get(Sys).Leave(Cluster.Get(Sys).SelfAddress);
                    ExpectTerminated(Region());
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).IsTerminated.Should().BeTrue();
                    });

                }, config.Second, config.Third);
                EnterBarrier("first-cluster-terminated");

                // no sharding nodes left of the original cluster, start a new nodes
                RunOn(() =>
                {
                    var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
                    var probe2 = CreateTestProbe(sys2);

                    if (PersistenceIsNeeded)
                    {
                        SetStore(sys2, storeOn: config.First);

                        ////Persistence.Persistence.Instance.Apply(sys2);
                        //sys2.ActorSelection(Node(config.First) / "system" / "akka.persistence.journal.sqlite").Tell(new Identify(null), probe2.Ref);
                        //var sharedStore = probe2.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                        //sharedStore.Should().NotBeNull();
                        //SqliteJournalShared.SetStore(sharedStore, sys2);
                    }

                    Cluster.Get(sys2).Join(Node(config.First).Address);
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
                            calculatedShardId.Should().BeEquivalentTo(shardState.ShardId);
                        }
                    }

                    EnterBarrier("verified");
                    Shutdown(sys2);
                }, config.Second, config.Third);

                RunOn(() =>
                {
                    EnterBarrier("verified");
                }, config.First);

                EnterBarrier("done");
            });
        }
    }
}
