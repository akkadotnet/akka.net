//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMinMembersSpec.cs" company="Akka.NET Project">
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
    public abstract class ClusterShardingMinMembersSpecConfig : MultiNodeConfig
    {
        public string Mode { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        protected ClusterShardingMinMembersSpecConfig(string mode)
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
                    akka.cluster.min-nr-of-members = 3
                    akka.cluster.sharding {{
                        rebalance-interval = 120s #disable rebalance
                    }}
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
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }
    public class PersistentClusterShardingMinMembersSpecConfig : ClusterShardingMinMembersSpecConfig
    {
        public PersistentClusterShardingMinMembersSpecConfig() : base("persistence") { }
    }
    public class DDataClusterShardingMinMembersSpecConfig : ClusterShardingMinMembersSpecConfig
    {
        public DDataClusterShardingMinMembersSpecConfig() : base("ddata") { }
    }

    public class PersistentClusterShardingMinMembersSpec : ClusterShardingMinMembersSpec
    {
        public PersistentClusterShardingMinMembersSpec() :this(new PersistentClusterShardingMinMembersSpecConfig()) { }
        protected PersistentClusterShardingMinMembersSpec(PersistentClusterShardingMinMembersSpecConfig config) : base(config, typeof(PersistentClusterShardingMinMembersSpec)) { }
    }
    public class DDataClusterShardingMinMembersSpec : ClusterShardingMinMembersSpec
    {
        public DDataClusterShardingMinMembersSpec() : this(new DDataClusterShardingMinMembersSpecConfig()) { }
        protected DDataClusterShardingMinMembersSpec(DDataClusterShardingMinMembersSpecConfig config) : base(config, typeof(DDataClusterShardingMinMembersSpec)) { }
    }
    public abstract class ClusterShardingMinMembersSpec : MultiNodeClusterSpec
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

        internal class EchoActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        internal ExtractEntityId extractEntityId = message => message is int ? (message.ToString(), message) : Option<(string, object)>.None;

        internal ExtractShardId extractShardId = message => message is int ? message.ToString() : null;

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingMinMembersSpecConfig _config;
        
        private readonly List<FileInfo> _storageLocations;

        protected ClusterShardingMinMembersSpec(ClusterShardingMinMembersSpecConfig config, Type type)
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
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<EchoActor>(),
                settings: ClusterShardingSettings.Create(Sys),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                allocationStrategy: allocationStrategy,
                handOffStopMessage: StopEntity.Instance);
        }

        [MultiNodeFact]
        public void Cluster_with_min_nr_of_members_using_sharding_specs()
        {
            if (!IsDDataMode)
            {
                Cluster_with_min_nr_of_members_using_sharding_should_setup_shared_journal();
            }
            Cluster_with_min_nr_of_members_using_sharding_should_use_all_nodes();
        }

        public void Cluster_with_min_nr_of_members_using_sharding_should_setup_shared_journal()
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

        public void Cluster_with_min_nr_of_members_using_sharding_should_use_all_nodes()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                Join(_config.First, _config.First);
                RunOn(() =>
                {
                    StartSharding();
                }, _config.First);

                Join(_config.Second, _config.First);
                RunOn(() =>
                {
                    StartSharding();
                }, _config.Second);

                Join(_config.Third, _config.First);
                // wait with starting sharding on third
                Within(Remaining, () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.State.Members.Count.Should().Be(3);
                        Cluster.State.Members.Should().OnlyContain(i => i.Status == MemberStatus.Up);
                    });
                });
                EnterBarrier("all-up");

                RunOn(() =>
                {
                    _region.Value.Tell(1);
                    // not allocated because third has not registered yet
                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                }, _config.First);
                EnterBarrier("verified");

                RunOn(() =>
                {
                    StartSharding();
                }, _config.Third);

                RunOn(() =>
                {
                    // the 1 was sent above
                    ExpectMsg(1);
                    _region.Value.Tell(2);
                    ExpectMsg(2);
                    _region.Value.Tell(3);
                    ExpectMsg(3);
                }, _config.First);
                EnterBarrier("shards-allocated");

                _region.Value.Tell(new GetClusterShardingStats(Remaining));

                var stats = ExpectMsg<ClusterShardingStats>();
                var firstAddress = Node(_config.First).Address;
                var secondAddress = Node(_config.Second).Address;
                var thirdAddress = Node(_config.Third).Address;

                stats.Regions.Keys.Should().BeEquivalentTo(new Address[] { firstAddress, secondAddress, thirdAddress });
                stats.Regions[firstAddress].Stats.Values.Sum().Should().Be(1);
                EnterBarrier("after-2");
            });
        }
    }
}
