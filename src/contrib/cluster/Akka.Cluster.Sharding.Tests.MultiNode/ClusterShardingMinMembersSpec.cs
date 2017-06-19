//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMinMembersSpec.cs" company="Akka.NET Project">
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
using Akka.Remote.Transport;
using Xunit;
using Akka.Event;
using Akka.TestKit.TestActors;
using System.Collections.Immutable;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingMinMembersSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public RoleName Third { get; private set; }

        public ClusterShardingMinMembersSpecConfig()
        {
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
                    akka.cluster.min-nr-of-members = 3
                    akka.cluster.sharding {
                        rebalance-interval = 120s #disable rebalance
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

    public class ClusterShardingMinMembersSpec : MultiNodeClusterSpec
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

        internal IdExtractor extractEntityId = message => message is int ? Tuple.Create(message.ToString(), message) : null;

        internal ShardResolver extractShardId = message => message is int ? message.ToString() : null;

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingMinMembersSpecConfig _config;

        public ClusterShardingMinMembersSpec()
            : this(new ClusterShardingMinMembersSpecConfig())
        {
        }

        protected ClusterShardingMinMembersSpec(ClusterShardingMinMembersSpecConfig config)
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
                idExtractor: extractEntityId,
                shardResolver: extractShardId,
                allocationStrategy: allocationStrategy,
                handOffStopMessage: StopEntity.Instance);
        }

        [MultiNodeFact]
        public void Cluster_with_min_nr_of_members_using_sharding_specs()
        {
            Cluster_with_min_nr_of_members_using_sharding_should_setup_shared_journal();
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
                Assert.NotNull(sharedStore);

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
                        Assert.Equal(3, Cluster.State.Members.Count);
                        Assert.True(Cluster.State.Members.All(i => i.Status == MemberStatus.Up));
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

                Assert.True(stats.Regions.Keys.ToImmutableHashSet().SetEquals(new Address[] { firstAddress, secondAddress, thirdAddress }));
                Assert.Equal(1, stats.Regions[firstAddress].Stats.Values.Sum());
                EnterBarrier("after-2");
            });
        }
    }
}