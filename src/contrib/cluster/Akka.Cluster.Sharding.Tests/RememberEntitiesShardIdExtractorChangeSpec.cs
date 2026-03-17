//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesShardIdExtractorChangeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Persistence;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    /// <summary>
    /// Covers that remembered entities is correctly migrated when used and the shard id messageExtractor
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

        private class FirstExtractor : IMessageExtractor
        {
            public string? EntityId(object message)
            {
                if (message is Message m)
                    return m.Id.ToString();
                return null;
            }

            public object? EntityMessage(object message)
            {
                return message;
            }

            public string ShardId(object message)
            {
                throw new NotImplementedException();
            }

            public virtual string ShardId(string entityId, object? messageHint = null)
            {
                return (int.Parse(entityId) % 10).ToString();
            }
        }

        private class SecondExtractor : FirstExtractor
        {
            public override string ShardId(string entityId, object? messageHint = null)
            {
                return (int.Parse(entityId) % 10 + 1).ToString();
            }
        }

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
                    state-store-mode = ""persistence""
                }
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on
                akka.cluster.sharding.verbose-debug-logging = on")
                    .WithFallback(ClusterSingleton.DefaultConfig())
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
        public async Task Sharding_with_remember_entities_enabled_should_allow_a_change_to_the_shard_id_extractor()
        {
            await WithSystemAsync("FirstShardIdExtractor", new FirstExtractor(), async (system, region) =>
            {
                await AssertRegionRegistrationCompleteAsync(region);
                region.Tell(new Message(1));
                await ExpectMsgAsync<string>("ack");
                region.Tell(new Message(11));
                await ExpectMsgAsync<string>("ack");
                region.Tell(new Message(21));
                await ExpectMsgAsync<string>("ack");

                var probe = CreateTestProbe(system);

                await AwaitAssertAsync(async () =>
                {
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = await probe.ExpectMsgAsync<CurrentShardRegionState>();
                    // shards should have been remembered but migrated over to shard 2
                    state.Shards.Where(s => s.ShardId == "1").SelectMany(i => i.EntityIds).Should().BeEquivalentTo("1", "11", "21");
                    state.Shards.Where(s => s.ShardId == "2").SelectMany(i => i.EntityIds).Should().BeEmpty();
                });
            });

            await WithSystemAsync("SecondShardIdExtractor", new SecondExtractor(), async (system, region) =>
            {
                var probe = CreateTestProbe(system);

                await AwaitAssertAsync(async () =>
                {
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = await probe.ExpectMsgAsync<CurrentShardRegionState>();
                    // shards should have been remembered but migrated over to shard 2
                    state.Shards.Where(s => s.ShardId == "1").SelectMany(i => i.EntityIds).Should().BeEmpty();
                    state.Shards.Where(s => s.ShardId == "2").SelectMany(i => i.EntityIds).Should().BeEquivalentTo("1", "11", "21");
                });
            });

            await WithSystemAsync("ThirdIncarnation", new SecondExtractor(), async (system, region) =>
            {
                var probe = CreateTestProbe(system);
                // Only way to verify that they were "normal"-remember-started here is to look at debug logs, will show
                // [akka://ThirdIncarnation@127.0.0.1:51533/system/sharding/ShardIdExtractorChange/1/RememberEntitiesStore] Recovery completed for shard [1] with [0] entities
                // [akka://ThirdIncarnation@127.0.0.1:51533/system/sharding/ShardIdExtractorChange/2/RememberEntitiesStore] Recovery completed for shard [2] with [3] entities
                await AwaitAssertAsync(async () =>
                {
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = await probe.ExpectMsgAsync<CurrentShardRegionState>();
                    state.Shards.Where(s => s.ShardId == "1").SelectMany(i => i.EntityIds).Should().BeEmpty();
                    state.Shards.Where(s => s.ShardId == "2").SelectMany(i => i.EntityIds).Should().BeEquivalentTo("1", "11", "21");
                });
            });
        }

        private async Task WithSystemAsync(string systemName, IMessageExtractor extractor, Func<ActorSystem, IActorRef, Task> f)
        {
            var system = ActorSystem.Create(systemName, Sys.Settings.Config);
            InitializeLogger(system, $"[{systemName}]");
            this.SetStore(system, Sys);

            var cluster = Cluster.Get(system);
            cluster.Join(cluster.SelfAddress);

            // Wait for cluster to form before starting sharding - fixes race condition
            // where sharding coordinator singleton may not be elected in time
            await AwaitAssertAsync(() =>
            {
                cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
            });

            try
            {
                var region = ClusterSharding.Get(system).Start(TypeName, Props.Create(() => new PA()), ClusterShardingSettings.Create(system), extractor);
                await f(system, region);
            }
            finally
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(20));
            }
        }

        private async Task AssertRegionRegistrationCompleteAsync(IActorRef region)
        {
            await AwaitAssertAsync(async () =>
            {
                region.Tell(GetCurrentRegions.Instance);
                var response = await ExpectMsgAsync<CurrentRegions>();
                response.Regions.Should().HaveCount(1);
            });
        }
    }
}
