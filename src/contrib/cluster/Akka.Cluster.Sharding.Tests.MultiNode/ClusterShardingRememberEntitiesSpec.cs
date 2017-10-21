//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRememberEntitiesSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingRememberEntitiesSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public RoleName Third { get; private set; }

        public ClusterShardingRememberEntitiesSpecConfig()
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
                    akka.cluster.auto-down-unreachable-after = 0s

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
        }
    }

    public class ClusterShardingRememberEntitiesSpec : MultiNodeClusterSpec
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
                probe.Tell(new Started(Self));
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        internal ExtractEntityId extractEntityId = message => message is int ? Tuple.Create(message.ToString(), message) : null;

        internal ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case int msg:
                    return msg.ToString();
                case ShardRegion.StartEntity msg:
                    return msg.EntityId.ToString();
            }
            return null;
        };

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingRememberEntitiesSpecConfig _config;

        public ClusterShardingRememberEntitiesSpec()
            : this(new ClusterShardingRememberEntitiesSpecConfig())
        {
        }

        protected ClusterShardingRememberEntitiesSpec(ClusterShardingRememberEntitiesSpecConfig config)
            : base(config, typeof(ClusterShardingRememberEntitiesSpec))
        {
            _config = config;

            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(to));
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding(ActorSystem sys, IActorRef probe)
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            ClusterSharding.Get(sys).Start(
                typeName: "Entity",
                entityProps: Props.Create(() => new TestEntity(probe)),
                settings: ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        [MultiNodeFact]
        public void Cluster_sharding_with_remember_entities_specs()
        {
            Cluster_sharding_with_remember_entities_should_setup_shared_journal();
            Cluster_sharding_with_remember_entities_should_start_remembered_entities_when_coordinator_fail_over();
            Cluster_sharding_with_remember_entities_should_start_remembered_entities_in_new_cluster();
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

        public void Cluster_sharding_with_remember_entities_should_start_remembered_entities_when_coordinator_fail_over()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                Join(_config.Second, _config.Second);
                RunOn(() =>
                {
                    StartSharding(Sys, TestActor);
                    _region.Value.Tell(1);
                    ExpectMsg<Started>();
                }, _config.Second);
                EnterBarrier("second-started");

                Join(_config.Third, _config.Second);
                RunOn(() =>
                {
                    StartSharding(Sys, TestActor);
                }, _config.Third);

                RunOn(() =>
                {
                    Within(Remaining, () =>
                    {
                        AwaitAssert(() =>
                        {
                            Cluster.State.Members.Count.Should().Be(2);
                            Cluster.State.Members.Should().OnlyContain(i => i.Status == MemberStatus.Up);
                        });
                    });
                }, _config.Second, _config.Third);
                EnterBarrier("all-up");

                RunOn(() =>
                {
                    TestConductor.Exit(_config.Second, 0).Wait();
                }, _config.First);

                EnterBarrier("crash-second");

                RunOn(() =>
                {
                    ExpectMsg<Started>(Remaining);
                }, _config.Third);

                EnterBarrier("after-2");
            });
        }

        public void Cluster_sharding_with_remember_entities_should_start_remembered_entities_in_new_cluster()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    Watch(_region.Value);

                    Cluster.Get(Sys).Leave(Cluster.Get(Sys).SelfAddress);
                    ExpectTerminated(_region.Value);
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).IsTerminated.Should().BeTrue();
                    });
                    // no nodes left of the original cluster, start a new cluster

                    var sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
                    var probe2 = CreateTestProbe(sys2);

                    {
                        // setup Persistence
                        Persistence.Persistence.Instance.Apply(sys2);
                        sys2.ActorSelection(Node(_config.First) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null), probe2.Ref);
                        var sharedStore = probe2.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
                        sharedStore.Should().NotBeNull();

                        MemoryJournalShared.SetStore(sharedStore, sys2);
                    }

                    Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);
                    StartSharding(sys2, probe2.Ref);
                    probe2.ExpectMsg<Started>(TimeSpan.FromSeconds(20));

                    Shutdown(sys2);
                }, _config.Third);
                EnterBarrier("after-3");
            });
        }
    }
}