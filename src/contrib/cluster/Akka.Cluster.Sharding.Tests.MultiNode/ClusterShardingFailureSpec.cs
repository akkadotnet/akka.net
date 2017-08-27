//-----------------------------------------------------------------------
// <copyright file="ClusterShardingFailureSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingFailureSpecConfig : MultiNodeConfig
    {
        public RoleName Controller { get; private set; }

        public RoleName First { get; private set; }

        public RoleName Second { get; private set; }

        public ClusterShardingFailureSpecConfig()
        {
            Controller = Role("controller");
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

                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.roles = [""backend""]
                    akka.cluster.sharding {
                        coordinator-failure-backoff = 3s
                        shard-failure-backoff = 3s
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
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class ClusterShardingFailureSpec : MultiNodeClusterSpec
    {
        #region setup

        [Serializable]
        internal sealed class Get
        {
            public readonly string Id;
            public Get(string id)
            {
                Id = id;
            }
        }

        [Serializable]
        internal sealed class Add
        {
            public readonly string Id;
            public readonly int I;
            public Add(string id, int i)
            {
                Id = id;
                I = i;
            }
        }

        [Serializable]
        internal sealed class Value
        {
            public readonly string Id;
            public readonly int N;
            public Value(string id, int n)
            {
                Id = id;
                N = n;
            }
        }

        internal class Entity : ReceiveActor
        {
            private int _n = 0;

            public Entity()
            {
                Receive<Get>(get => Sender.Tell(new Value(get.Id, _n)));
                Receive<Add>(add => _n += add.I);
            }
        }

        internal ExtractEntityId extractEntityId = message =>
        {
            switch (message)
            {
                case Get msg:
                    return Tuple.Create(msg.Id, message);
                case Add msg:
                    return Tuple.Create(msg.Id, message);
            }
            return null;
        };

        internal ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case Get msg:
                    return msg.Id[0].ToString();
                case Add msg:
                    return msg.Id[0].ToString();
            }
            return null;
        };

        private Lazy<IActorRef> _region;

        private readonly ClusterShardingFailureSpecConfig _config;

        public ClusterShardingFailureSpec()
            : this(new ClusterShardingFailureSpecConfig())
        {
        }

        protected ClusterShardingFailureSpec(ClusterShardingFailureSpecConfig config)
            : base(config, typeof(ClusterShardingFailureSpec))
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

                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.UniqueAddress).Should().Contain(Cluster.SelfUniqueAddress);
                    Cluster.State.Members.Select(i => i.Status).Should().OnlyContain(i => i == MemberStatus.Up);
                });
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding()
        {
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        [MultiNodeFact]
        public void ClusterSharding_with_flaky_journal_network_specs()
        {
            ClusterSharding_with_flaky_journal_network_should_setup_shared_journal();
            ClusterSharding_with_flaky_journal_network_should_join_cluster();
            ClusterSharding_with_flaky_journal_network_should_recover_after_journal_network_failure();
        }

        public void ClusterSharding_with_flaky_journal_network_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.MemoryJournal");
            }, _config.Controller);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_config.Controller) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null));
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

        public void ClusterSharding_with_flaky_journal_network_should_join_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Add("10", 1));
                    region.Tell(new Add("20", 2));
                    region.Tell(new Add("21", 3));
                    region.Tell(new Get("10"));
                    ExpectMsg<Value>(v => v.Id == "10" && v.N == 1);
                    region.Tell(new Get("20"));
                    ExpectMsg<Value>(v => v.Id == "20" && v.N == 2);
                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 3);
                }, _config.First);
                EnterBarrier("after-2");
            });
        }

        public void ClusterSharding_with_flaky_journal_network_should_recover_after_journal_network_failure()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    TestConductor.Blackhole(_config.Controller, _config.First, ThrottleTransportAdapter.Direction.Both).Wait();
                    TestConductor.Blackhole(_config.Controller, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                }, _config.Controller);
                EnterBarrier("journal-backholded");

                RunOn(() =>
                {
                    // try with a new shard, will not reply until journal is available again
                    var region = _region.Value;
                    region.Tell(new Add("40", 4));
                    var probe = CreateTestProbe();
                    region.Tell(new Get("40"), probe.Ref);
                    probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
                }, _config.First);
                EnterBarrier("first-delayed");

                RunOn(() =>
                {
                    TestConductor.PassThrough(_config.Controller, _config.First, ThrottleTransportAdapter.Direction.Both).Wait();
                    TestConductor.PassThrough(_config.Controller, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
                }, _config.Controller);
                EnterBarrier("journal-ok");

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 3);
                    var entity21 = LastSender;
                    var shard2 = Sys.ActorSelection(entity21.Path.Parent);


                    //Test the PersistentShardCoordinator allocating shards during a journal failure
                    region.Tell(new Add("30", 3));

                    //Test the Shard starting entities and persisting during a journal failure
                    region.Tell(new Add("11", 1));

                    //Test the Shard passivate works during a journal failure
                    shard2.Tell(new Passivate(PoisonPill.Instance), entity21);
                    region.Tell(new Add("21", 1));

                    region.Tell(new Get("21"));
                    ExpectMsg<Value>(v => v.Id == "21" && v.N == 1);

                    region.Tell(new Get("30"));
                    ExpectMsg<Value>(v => v.Id == "30" && v.N == 3);

                    region.Tell(new Get("11"));
                    ExpectMsg<Value>(v => v.Id == "11" && v.N == 1);

                    region.Tell(new Get("40"));
                    ExpectMsg<Value>(v => v.Id == "40" && v.N == 4);
                }, _config.First);
                EnterBarrier("verified-first");

                RunOn(() =>
                {
                    var region = _region.Value;
                    region.Tell(new Add("10", 1));
                    region.Tell(new Add("20", 2));
                    region.Tell(new Add("30", 3));
                    region.Tell(new Add("11", 4));
                    region.Tell(new Get("10"));
                    ExpectMsg<Value>(v => v.Id == "10" && v.N == 2);
                    region.Tell(new Get("11"));
                    ExpectMsg<Value>(v => v.Id == "11" && v.N == 5);
                    region.Tell(new Get("20"));
                    ExpectMsg<Value>(v => v.Id == "20" && v.N == 4);
                    region.Tell(new Get("30"));
                    ExpectMsg<Value>(v => v.Id == "30" && v.N == 6);
                }, _config.Second);
                EnterBarrier("after-3");
            });
        }
    }
}