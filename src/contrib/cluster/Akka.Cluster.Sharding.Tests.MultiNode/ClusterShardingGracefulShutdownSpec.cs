//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGracefulShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using System.Collections.Immutable;
using System.IO;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class ClusterShardingGracefulShutdownSpecConfig : MultiNodeConfig
    {
        public string Mode { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        protected ClusterShardingGracefulShutdownSpecConfig(string mode)
        {
            Mode = mode;
            First = Role("first");
            Second = Role("second");

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
                      dir = ""target/ClusterShardingGracefulShutdownSpec/sharding-ddata""
                      map-size = 10000000
                    }}"))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }
    public class PersistentClusterShardingGracefulShutdownSpecConfig : ClusterShardingGracefulShutdownSpecConfig
    {
        public PersistentClusterShardingGracefulShutdownSpecConfig() : base("persistence") { }
    }
    public class DDataClusterShardingGracefulShutdownSpecConfig : ClusterShardingGracefulShutdownSpecConfig
    {
        public DDataClusterShardingGracefulShutdownSpecConfig() : base("ddata") { }
    }

    public class PersistentClusterShardingGracefulShutdownSpec : ClusterShardingGracefulShutdownSpec
    {
        public PersistentClusterShardingGracefulShutdownSpec() : this(new PersistentClusterShardingGracefulShutdownSpecConfig()) { }
        protected PersistentClusterShardingGracefulShutdownSpec(PersistentClusterShardingGracefulShutdownSpecConfig config) : base(config, typeof(PersistentClusterShardingGracefulShutdownSpec)) { }
    }
    public class DDataClusterShardingGracefulShutdownSpec : ClusterShardingGracefulShutdownSpec
    {
        public DDataClusterShardingGracefulShutdownSpec() : this(new DDataClusterShardingGracefulShutdownSpecConfig()) { }
        protected DDataClusterShardingGracefulShutdownSpec(DDataClusterShardingGracefulShutdownSpecConfig config) : base(config, typeof(DDataClusterShardingGracefulShutdownSpec)) { }
    }
    public abstract class ClusterShardingGracefulShutdownSpec : MultiNodeClusterSpec
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

        internal ExtractEntityId extractEntityId = message => message is int ? (message.ToString(), message) : Option<(string, object)>.None;

        internal ExtractShardId extractShardId = message => message is int ? message.ToString() : null;

        private readonly Lazy<IActorRef> _region;

        private readonly ClusterShardingGracefulShutdownSpecConfig _config;

        private readonly List<FileInfo> _storageLocations;

        protected ClusterShardingGracefulShutdownSpec(ClusterShardingGracefulShutdownSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
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
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                allocationStrategy: allocationStrategy,
                handOffStopMessage: StopEntity.Instance);
        }

        [MultiNodeFact]
        public void ClusterShardingGracefulShutdownSpecs()
        {
            if (!IsDDataMode)
            {
                ClusterSharding_should_setup_shared_journal();
            }
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

                    regionAddresses.Count.Should().Be(2);
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
                            probe.LastSender.Path.Should().Be(_region.Value.Path / i.ToString() / i.ToString());
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
                        extractEntityId: extractEntityId,
                        extractShardId: extractShardId,
                        allocationStrategy: allocationStrategy,
                        handOffStopMessage: StopEntity.Instance);

                    Watch(regionEmpty);
                    regionEmpty.Tell(GracefulShutdown.Instance);
                    ExpectTerminated(regionEmpty, TimeSpan.FromSeconds(5));
                }, _config.First);
            });
        }
    }
}
