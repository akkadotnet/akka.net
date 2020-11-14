//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesShardIdExtractorChangeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
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
    /// Covers that remembered entities is correctly migrated when used and the shard id extractor
    /// is changed so that entities should live on other shards after a full restart of the cluster.
    /// </summary>
    public class RememberEntitiesShardIdExtractorChangeSpec : AkkaSpec
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

        private ExtractShardId firstExtractShardId = message =>
        {
            switch (message)
            {
                case Message m:
                    return (m.Id % 10).ToString();
                case ShardRegion.StartEntity se:
                    return (int.Parse(se.EntityId) % 10).ToString();
            }
            return null;
        };

        private ExtractShardId secondExtractShardId = message =>
        {
            switch (message)
            {
                case Message m:
                    return (m.Id % 10 + 1).ToString();
                case ShardRegion.StartEntity se:
                    return (int.Parse(se.EntityId) % 10 + 1).ToString();
            }
            return null;
        };

        private const string TypeName = "ShardIdExtractorChange";

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
                    state-store-mode = ""ddata""
                }
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                akka.cluster.sharding.verbose-debug-logging = on")
                    .WithFallback(ClusterSingletonManager.DefaultConfig())
                    .WithFallback(ClusterSharding.DefaultConfig());
            }
        }

        public RememberEntitiesShardIdExtractorChangeSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        protected override void AtStartup()
        {
            this.StartPersistence(Sys);
        }

        [Fact]
        public void Sharding_with_remember_entities_enabled_should_allow_a_change_to_the_shard_id_extractor()
        {
            WithSystem("FirstShardIdExtractor", firstExtractShardId, (system, region) =>
            {
                AssertRegionRegistrationComplete(region);
                region.Tell(new Message(1));
                ExpectMsg("ack");
                region.Tell(new Message(11));
                ExpectMsg("ack");
                region.Tell(new Message(21));
                ExpectMsg("ack");

                var probe = CreateTestProbe(system);

                AwaitAssert(() =>
                {
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = probe.ExpectMsg<CurrentShardRegionState>();
                    // shards should have been remembered but migrated over to shard 2
                    state.Shards.Where(s => s.ShardId == "1").SelectMany(i => i.EntityIds).Should().BeEquivalentTo("1", "11", "21");
                    state.Shards.Where(s => s.ShardId == "2").SelectMany(i => i.EntityIds).Should().BeEmpty();
                });
            });

            WithSystem("SecondShardIdExtractor", secondExtractShardId, (system, region) =>
            {
                var probe = CreateTestProbe(system);

                AwaitAssert(() =>
                {
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = probe.ExpectMsg<CurrentShardRegionState>();
                    // shards should have been remembered but migrated over to shard 2
                    state.Shards.Where(s => s.ShardId == "1").SelectMany(i => i.EntityIds).Should().BeEmpty();
                    state.Shards.Where(s => s.ShardId == "2").SelectMany(i => i.EntityIds).Should().BeEquivalentTo("1", "11", "21");
                });
            });

            WithSystem("ThirdIncarnation", secondExtractShardId, (system, region) =>
            {
                var probe = CreateTestProbe(system);
                // Only way to verify that they were "normal"-remember-started here is to look at debug logs, will show
                // [akka://ThirdIncarnation@127.0.0.1:51533/system/sharding/ShardIdExtractorChange/1/RememberEntitiesStore] Recovery completed for shard [1] with [0] entities
                // [akka://ThirdIncarnation@127.0.0.1:51533/system/sharding/ShardIdExtractorChange/2/RememberEntitiesStore] Recovery completed for shard [2] with [3] entities
                AwaitAssert(() =>
                {
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = probe.ExpectMsg<CurrentShardRegionState>();
                    state.Shards.Where(s => s.ShardId == "1").SelectMany(i => i.EntityIds).Should().BeEmpty();
                    state.Shards.Where(s => s.ShardId == "2").SelectMany(i => i.EntityIds).Should().BeEquivalentTo("1", "11", "21");
                });
            });
        }

        private void WithSystem(string systemName, ExtractShardId extractShardId, Action<ActorSystem, IActorRef> f)
        {
            var system = ActorSystem.Create(systemName, Sys.Settings.Config);
            InitializeLogger(system, $"[{systemName}]");
            this.SetStore(system, Sys);
            Cluster.Get(system).Join(Cluster.Get(system).SelfAddress);
            try
            {
                var region = ClusterSharding.Get(system).Start(TypeName, Props.Create(() => new PA()), ClusterShardingSettings.Create(system), extractEntityId, extractShardId);
                f(system, region);
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
