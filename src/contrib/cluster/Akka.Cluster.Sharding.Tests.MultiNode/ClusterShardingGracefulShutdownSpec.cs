//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGracefulShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tests.MultiNode;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Remote.TestKit;
using Xunit;
using System.Collections.Immutable;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGracefulShutdownSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public ClusterShardingGracefulShutdownSpecConfig()
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
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterShardingGracefulShutdownSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class StopEntity
        {
            public static readonly StopEntity Instance = new StopEntity();

            private StopEntity()
            {
            }
        }

        internal class Entity : ReceiveActor
        {
            public Entity()
            {
                Receive<int>(id => Sender.Tell(id));
                Receive<StopEntity>(_ => Context.Stop(Self));
            }
        }

        internal IdExtractor extractEntityId = message => message is int ? Tuple.Create(message.ToString(), message) : null;

        internal ShardResolver extractShardId = message => message is int ? message.ToString() : null;

        private readonly Lazy<IActorRef> _region;

        private readonly ClusterShardingGracefulShutdownSpecConfig _config;

        public ClusterShardingGracefulShutdownSpec()
            : this(new ClusterShardingGracefulShutdownSpecConfig())
        {
        }

        protected ClusterShardingGracefulShutdownSpec(ClusterShardingGracefulShutdownSpecConfig config)
            : base(config)
        {
            _config = config;

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

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
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                idExtractor: extractEntityId,
                shardResolver: extractShardId,
                allocationStrategy: allocationStrategy,
                handOffStopMessage: StopEntity.Instance);
        }

        //[MultiNodeFact]
        public void ClusterShardingGracefulShutdownSpecs()
        {
            ClusterSharding_should_setup_shared_journal();
            ClusterSharding_should_start_some_shards_in_both_regions();
            ClusterSharding_should_gracefully_shutdown_a_region();
            ClusterSharding_should_gracefully_shutdown_empty_region();
        }

        public void ClusterSharding_should_setup_shared_journal()
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
                Assert.NotNull(sharedStore);

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

        public void ClusterSharding_should_start_some_shards_in_both_regions()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);

                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    var regionAddresses = Enumerable.Range(1, 100)
                        .Select(n =>
                        {
                            _region.Value.Tell(n, probe.Ref);
                            probe.ExpectMsg(n, TimeSpan.FromSeconds(1));
                            return probe.LastSender.Path.Address;
                        })
                        .ToImmutableHashSet();

                    Assert.Equal(2, regionAddresses.Count);
                });
                EnterBarrier("after-2");
            });
        }

        public void ClusterSharding_should_gracefully_shutdown_a_region()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    _region.Value.Tell(GracefulShutdown.Instance);
                }, _config.Second);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        var probe = CreateTestProbe();
                        for (int i = 1; i <= 200; i++)
                        {
                            _region.Value.Tell(i, probe.Ref);
                            probe.ExpectMsg(i, TimeSpan.FromSeconds(1));
                            Assert.Equal(_region.Value.Path / i.ToString() / i.ToString(), probe.LastSender.Path);
                        }
                    });
                }, _config.First);
                EnterBarrier("handoff-completed");

                RunOn(() =>
                {
                    var region = _region.Value;
                    Watch(region);
                    ExpectTerminated(region);
                }, _config.Second);
                EnterBarrier("after-3");
            });
        }

        public void ClusterSharding_should_gracefully_shutdown_empty_region()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
                    var regionEmpty = ClusterSharding.Get(Sys).Start(
                        typeName: "EntityEmpty",
                        entityProps: Props.Create<Entity>(),
                        settings: ClusterShardingSettings.Create(Sys),
                        idExtractor: extractEntityId,
                        shardResolver: extractShardId,
                        allocationStrategy: allocationStrategy,
                        handOffStopMessage: StopEntity.Instance);

                    Watch(regionEmpty);
                    regionEmpty.Tell(GracefulShutdown.Instance);
                    ExpectTerminated(regionEmpty);
                }, _config.First);
            });
        }
    }
}