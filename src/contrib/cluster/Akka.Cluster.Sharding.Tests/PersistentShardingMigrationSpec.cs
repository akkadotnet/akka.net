//-----------------------------------------------------------------------
// <copyright file="PersistentShardingMigrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Persistence;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// Test migration from old persistent shard coordinator with remembered
    /// entities to using a ddatabacked shard coordinator with an event sourced
    /// replicated entity store.
    /// </summary>
    public class PersistentShardingMigrationSpec : AkkaSpec
    {
        private class Message
        {
            public Message(long id)
            {
                Id = id;
            }

            public long Id { get; }
        }

        private class PA : PersistentActor
        {
            public override string PersistenceId => "pa-" + Self.Path.Name;

            protected override bool ReceiveRecover(object message)
            {
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                Sender.Tell("ack");
                return true;
            }
        }

        private ExtractEntityId extractEntityId = message =>
        {
            if (message is Message m)
                return (m.Id.ToString(), m);
            return Option<(string, object)>.None;
        };

        private ExtractShardId ExtractShardId(IActorRef probe)
        {
            return message =>
            {
                switch (message)
                {
                    case Message m:
                        return m.Id.ToString();
                    case ShardRegion.StartEntity se:
                        // StartEntity is used by remembering entities feature
                        probe.Tell(se.EntityId);
                        return se.EntityId;
                }
                return null;
            };
        }

        private static Config SpecConfig
        {
            get
            {
                return ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster

                akka.persistence.journal.plugin = ""akka.persistence.journal.memory-journal-shared""
                akka.persistence.journal.memory-journal-shared {
                    class = ""Akka.Cluster.Sharding.Tests.MemoryJournalShared, Akka.Cluster.Sharding.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    timeout = 5s
                }

                akka.persistence.snapshot-store.plugin = ""akka.persistence.memory-snapshot-store-shared""
                akka.persistence.memory-snapshot-store-shared {
                    class = ""Akka.Cluster.Sharding.Tests.MemorySnapshotStoreShared, Akka.Cluster.Sharding.Tests""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    timeout = 5s
                }

                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding {
                    remember-entities = on
                    remember-entities-store = ""eventsourced""
                    state-store-mode = ""persistence""

                    # make sure we test snapshots
                    snapshot-after = 5

                    verbose-debug-logging = on
                    fail-on-invalid-entity-state-transition = on

                    # Lots of sharding setup, make it quicker
                    retry-interval = 500ms
                }
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                akka.cluster.sharding.verbose-debug-logging = on")
                    .WithFallback(ClusterSingletonManager.DefaultConfig())
                    .WithFallback(ClusterSharding.DefaultConfig());
            }
        }

        private static Config ConfigForNewMode =>
            ConfigurationFactory.ParseString(@"
                akka.cluster.sharding {
                    remember-entities = on
                    remember-entities-store = ""eventsourced""
                    state-store-mode = ""ddata""
                }

                akka.persistence.journal.memory-journal-shared {
                    event-adapters {
                        coordinator-migration = ""Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding""
                    }

                    event-adapter-bindings {
                        ""Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding"" = coordinator-migration
                    }
                }");


        private Config configForNewMode;

        public PersistentShardingMigrationSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            configForNewMode = ConfigForNewMode.WithFallback(Sys.Settings.Config);
        }

        protected override void AtStartup()
        {
            this.StartPersistence(Sys);
        }


        [Fact]
        public void Migration_should_allow_migration_of_remembered_shards_and_not_allow_going_back()
        {
            var typeName = "Migration";

            WithSystem(Sys.Settings.Config, typeName, "OldMode", (s, region, p) =>
            {
                AssertRegionRegistrationComplete(region);
                region.Tell(new Message(1));
                ExpectMsg("ack");
                region.Tell(new Message(2));
                ExpectMsg("ack");
                region.Tell(new Message(3));
                ExpectMsg("ack");
            });

            WithSystem(configForNewMode, typeName, "NewMode", (system, region, rememberedEntitiesProbe) =>
            {
                AssertRegionRegistrationComplete(region);
                var probe = CreateTestProbe(system);
                region.Tell(new Message(1), probe.Ref);
                probe.ExpectMsg("ack");
                ImmutableHashSet.Create(
                    rememberedEntitiesProbe.ExpectMsg<string>(),
                    rememberedEntitiesProbe.ExpectMsg<string>(),
                    rememberedEntitiesProbe.ExpectMsg<string>()).Should().BeEquivalentTo("1", "2", "3"); // 1-2 from the snapshot, 3 from a replayed message
                rememberedEntitiesProbe.ExpectNoMsg();
            });

            WithSystem(Sys.Settings.Config, typeName, "OldModeAfterMigration", (system, region, _) =>
            {
                var probe = CreateTestProbe(system);
                region.Tell(new Message(1), probe.Ref);
                probe.ExpectNoMsg(TimeSpan.FromSeconds(5)); // sharding should have failed to start
            });
        }

        [Fact]
        public void Migration_should_not_allow_going_back_to_persistence_mode_based_on_a_snapshot()
        {
            var typeName = "Snapshots";
            WithSystem(configForNewMode, typeName, "NewMode", (system, region, _) =>
            {
                var probe = CreateTestProbe(system);
                for (int i = 1; i <= 5; i++)
                {
                    region.Tell(new Message(i), probe.Ref);
                    probe.ExpectMsg("ack");
                }
            });

            WithSystem(Sys.Settings.Config, typeName, "OldModeShouldNotWork", (system, region, _) =>
            {
                var probe = CreateTestProbe(system);
                region.Tell(new Message(1), probe.Ref);
                probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
            });
        }

        private void WithSystem(Config config, string typeName, string systemName, Action<ActorSystem, IActorRef, TestProbe> f)
        {
            var system = ActorSystem.Create(systemName, config);
            InitializeLogger(system, $"[{systemName}]");
            this.SetStore(system, Sys);
            var cluster = Cluster.Get(system);
            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.SelfMember.Status.Should().Be(MemberStatus.Up);
            });

            try
            {
                var rememberedEntitiesProbe = CreateTestProbe(system);
                var region = ClusterSharding.Get(system).Start(
                    typeName,
                    Props.Create(() => new PA()),
                    ClusterShardingSettings.Create(system),
                    extractEntityId,
                    ExtractShardId(rememberedEntitiesProbe.Ref));

                f(system, region, rememberedEntitiesProbe);
            }
            finally
            {
                system.Terminate().Wait(TimeSpan.FromSeconds(20));
            }
        }

        private void AssertRegionRegistrationComplete(IActorRef region)
        {
            AwaitAssert(() =>
            {
                region.Tell(GetCurrentRegions.Instance);
                ExpectMsg<CurrentRegions>().Regions.Should().HaveCount(1);
            });
        }
    }
}
