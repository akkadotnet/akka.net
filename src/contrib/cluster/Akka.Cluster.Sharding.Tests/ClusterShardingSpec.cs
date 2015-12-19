//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Remote.TestKit;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Cluster.Tools.Singleton;
using Akka.Pattern;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingSpecConfig : MultiNodeConfig
    {
        public ClusterShardingSpecConfig()
        {
            var controller = Role("controller");
            var first = Role("first");
            var second = Role("second");
            var third = Role("third");
            var fourth = Role("fourth");
            var fifth = Role("fifth");
            var sixth = Role("sixth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.down-removal-margin = 5s
                akka.cluster.roles = [""backend""]
                akka.persistence.journal.plugin = ""akka.persistence.journal.leveldb-shared""
                akka.persistence.journal.leveldb-shared.store {
                  native = off
                  dir = ""target/journal-ClusterShardingSpec""
                }
                akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
                akka.persistence.snapshot-store.local.dir = ""target/snapshots-ClusterShardingSpec""
                akka.cluster.sharding {
                  retry-interval = 1 s
                  handoff-timeout = 10 s
                  shard-start-timeout = 5s
                  entity-restart-backoff = 1s
                  rebalance-interval = 2 s
                  least-shard-allocation-strategy {
                    rebalance-threshold = 2
                    max-simultaneous-rebalance = 1
                  }
                }");

            NodeConfig(new[] { sixth }, new[] { ConfigurationFactory.ParseString(@"akka.cluster.roles = [""frontend""]") });
        }
    }

    internal class Counter : PersistentActor
    {
        #region messages

        [Serializable]
        public sealed class Increment
        {
            public static readonly Increment Instance = new Increment();

            private Increment()
            {
            }
        }

        [Serializable]
        public sealed class Decrement
        {
            public static readonly Decrement Instance = new Decrement();

            private Decrement()
            {
            }
        }

        [Serializable]
        public sealed class Get
        {
            public readonly long CounterId;
            public Get(long counterId)
            {
                CounterId = counterId;
            }
        }

        [Serializable]
        public sealed class EntityEnvelope
        {
            public readonly long Id;
            public readonly object Payload;
            public EntityEnvelope(long id, object payload)
            {
                Id = id;
                Payload = payload;
            }
        }

        [Serializable]
        public sealed class CounterChanged
        {
            public readonly int Delta;
            public CounterChanged(int delta)
            {
                Delta = delta;
            }
        }

        [Serializable]
        public sealed class Stop
        {
            public static readonly Stop Instance = new Stop();

            private Stop()
            {
            }
        }

        #endregion

        public static readonly IdExtractor ExtractEntityId = message =>
        {
            if (message is EntityEnvelope)
            {
                var env = (EntityEnvelope)message;
                return Tuple.Create(env.Id.ToString(), env.Payload);
            }
            if (message is Get)
            {
                return Tuple.Create(((Get)message).CounterId.ToString(), message);
            }
            return null;
        };

        public static readonly ShardResolver ExtractShardId = message =>
        {
            if (message is EntityEnvelope)
                return (((EntityEnvelope)message).Id % ShardsCount).ToString();
            if (message is Get)
                return (((Get)message).CounterId % ShardsCount).ToString();
            return null;

        };

        public const int ShardsCount = 12;
        private int _count = 0;

        public Counter()
        {
            Context.SetReceiveTimeout(TimeSpan.FromMinutes(2));
        }

        protected override void PostStop()
        {
            base.PostStop();
            // Simulate that the passivation takes some time, to verify passivation buffering
            Thread.Sleep(500);
        }

        public override string PersistenceId { get { return Self.Path.Parent.Name + "-" + Self.Path.Name; } }
        protected override bool ReceiveRecover(object message)
        {
            return message.Match()
                .With<CounterChanged>(UpdateState)
                .WasHandled;
        }

        protected override bool ReceiveCommand(object message)
        {
            return message.Match()
                .With<Increment>(_ => Persist(new CounterChanged(1), UpdateState))
                .With<Increment>(_ => Persist(new CounterChanged(-1), UpdateState))
                .With<Get>(_ => Sender.Tell(_count))
                .With<ReceiveTimeout>(_ => Context.Parent.Tell(new Passivate(Stop.Instance)))
                .With<Stop>(_ => Context.Stop(Self))
                .WasHandled;
        }

        private void UpdateState(CounterChanged e)
        {
            _count += e.Delta;
        }
    }
    public class ClusterShardingNode1 : ClusterShardingSpec { }
    public class ClusterShardingNode2 : ClusterShardingSpec { }
    public class ClusterShardingNode3 : ClusterShardingSpec { }

    public abstract class ClusterShardingSpec : MultiNodeClusterSpec
    {
        #region Setup

        private readonly DirectoryInfo[] _storageLocations;
        private readonly RoleName _controller;
        private readonly RoleName _second;
        private readonly RoleName _first;
        private readonly RoleName _third;
        private readonly RoleName _fourth;
        private readonly RoleName _sixth;
        private readonly RoleName _fifth;

        private readonly Lazy<IActorRef> _region;
        private readonly Lazy<IActorRef> _rebalancingRegion;
        private readonly Lazy<IActorRef> _persistentEntitiesRegion;
        private readonly Lazy<IActorRef> _anotherPersistentRegion;
        private readonly Lazy<IActorRef> _persistentRegion;
        private readonly Lazy<IActorRef> _rebalancingPersistentRegion;
        private readonly Lazy<IActorRef> _autoMigrateRegion;

        protected ClusterShardingSpec() : base(new ClusterShardingSpecConfig())
        {
            _storageLocations = new[]
            {
                "akka.persistence.journal.leveldb.dir",
                "akka.persistence.journal.leveldb-shared.store.dir",
                "akka.persistence.snapshot-store.local.dir"
            }.Select(s => new DirectoryInfo(Sys.Settings.Config.GetString(s))).ToArray();

            _controller = new RoleName("controller");
            _first = new RoleName("first");
            _second = new RoleName("second");
            _third = new RoleName("third");
            _fourth = new RoleName("fourth");
            _fifth = new RoleName("fifth");
            _sixth = new RoleName("sixth");

            _region = new Lazy<IActorRef>(() => CreateRegion("counter", false));
            _rebalancingRegion = new Lazy<IActorRef>(() => CreateRegion("rebalancingCounter", false));

            _persistentEntitiesRegion = new Lazy<IActorRef>(() => CreateRegion("PersistentCounterEntities", true));
            _anotherPersistentRegion = new Lazy<IActorRef>(() => CreateRegion("AnotherPersistentCounter", true));
            _persistentRegion = new Lazy<IActorRef>(() => CreateRegion("PersistentCounter", true));
            _rebalancingPersistentRegion = new Lazy<IActorRef>(() => CreateRegion("RebalancingPersistentCounter", true));
            _autoMigrateRegion = new Lazy<IActorRef>(() => CreateRegion("AutoMigrateRegionTest", true));
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        protected override void AtStartup()
        {
            base.AtStartup();
            RunOn(() =>
            {
                foreach (var dir in _storageLocations)
                {
                    if (dir.Exists) dir.Delete();
                }
            }, _controller);
        }

        protected override void AfterTermination()
        {
            base.AfterTermination();
            RunOn(() =>
            {
                foreach (var dir in _storageLocations)
                {
                    if (dir.Exists) dir.Delete();
                }
            }, _controller);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
            }, from);

            EnterBarrier(from.Name + "-joined");
        }

        private void CreateCoordinator()
        {
            var typeNames = new[]
            {
                "counter", "rebalancingCounter", "PersistentCounterEntities", "AnotherPersistentCounter",
                "PersistentCounter", "RebalancingPersistentCounter", "AutoMigrateRegionTest"
            };

            foreach (var typeName in typeNames)
            {
                var rebalanceEnabled = string.Equals(typeName, "rebalancing", StringComparison.InvariantCultureIgnoreCase);
                var singletonProps = Props.Create(() => new BackoffSupervisor(
                    CoordinatorProps(typeName, rebalanceEnabled),
                    "coordinator",
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(5),
                    0.1)).WithDeploy(Deploy.Local);

                Sys.ActorOf(ClusterSingletonManager.Props(
                    singletonProps,
                    PoisonPill.Instance,
                    ClusterSingletonManagerSettings.Create(Sys)),
                    typeName + "Coordinator");
            }
        }

        private Props CoordinatorProps(string typeName, bool rebalanceEntities)
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            var config = ConfigurationFactory.ParseString(string.Format(""));
            var settings = ClusterShardingSettings.Create(config, Sys.Settings.Config.GetConfig("akka.cluster.singleton"));

            return PersistentShardCoordinator.Props(typeName, settings, allocationStrategy);
        }

        private IActorRef CreateRegion(string typeName, bool rememberEntities)
        {
            var config = ConfigurationFactory.ParseString(@"
              retry-interval = 1s
              shard-failure-backoff = 1s
              entity-restart-backoff = 1s
              buffer-size = 1000").WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));
            var settings = ClusterShardingSettings.Create(config, Sys.Settings.Config.GetConfig("akka.cluster.singleton")).WithRememberEntities(rememberEntities);

            return Sys.ActorOf(Props.Create(() => new ShardRegion(
                typeName,
                Props.Create<Counter>(),
                settings,
                "/user/" + typeName + "Coordinator/singleton/coordinator",
                Counter.ExtractEntityId,
                Counter.ExtractShardId,
                PoisonPill.Instance)),
                typeName + "Region");
        }

        #endregion

        #region Cluster shardings specs

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_setup_shared_journal()
        {
            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() => Sys.ActorOf<MemoryJournal>("store"), _controller);
            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                Sys.ActorSelection(Node(_controller) / "user" / "store").Tell(new Identify(null));
                var sharedStore = ExpectMsg<ActorIdentity>().Subject;
                //SharedLeveldbJournal.setStore(sharedStore, system)
            }, _first, _second, _third, _fourth, _fifth, _sixth);

            EnterBarrier("after-1");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_work_in_single_node_cluster()
        {
            ClusterSharding_should_setup_shared_journal();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_first, _first);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(1, Counter.Decrement.Instance));
                    r.Tell(new Counter.Get(1));

                    ExpectMsg(2);
                    r.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>(m => m.Regions.Length == 1 && m.Regions[0].Equals(Cluster.SelfAddress));
                }, _first);

                EnterBarrier("after-2");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_use_second_node()
        {
            ClusterSharding_should_work_in_single_node_cluster();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_second, _first);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(2, Counter.Decrement.Instance));
                    r.Tell(new Counter.Get(2));

                    ExpectMsg(2);

                    r.Tell(new Counter.EntityEnvelope(11, Counter.Increment.Instance));
                    r.Tell(new Counter.EntityEnvelope(12, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(11));
                    ExpectMsg(1);
                    r.Tell(new Counter.Get(12));
                    ExpectMsg(1);
                }, _second);
                EnterBarrier("second-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(2));
                    ExpectMsg(3);
                    Assert.Equal(Node(_second) / "user" / "counterRegion" / "2" / "2", LastSender.Path);

                    r.Tell(new Counter.Get(11));
                    ExpectMsg(1);
                    // local on first
                    Assert.Equal(r.Path / "11" / "11", LastSender.Path);
                    r.Tell(new Counter.Get(12));
                    ExpectMsg(1);
                    Assert.Equal(Node(_second) / "user" / "counterRegion" / "0" / "12", LastSender.Path);
                }, _first);
                EnterBarrier("first-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.Get(2));
                    ExpectMsg(3);
                    Assert.Equal(r.Path / "2" / "2", LastSender.Path);

                    r.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>(x => x.Regions.Length == 2
                                                   && x.Regions[0].Equals(Cluster.SelfAddress)
                                                   && x.Regions[1].Equals(Node(_first).Address));
                }, _second);
                EnterBarrier("after-3");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_support_passivation_and_activation_of_entities()
        {
            ClusterSharding_should_use_second_node();

            RunOn(() =>
            {
                var r = _region.Value;
                r.Tell(new Counter.Get(2));
                ExpectMsg(3); //TODO: shouldn't we tell increment 3x first?
                r.Tell(new Counter.EntityEnvelope(2, ReceiveTimeout.Instance));
                // let the Passivate-Stop roundtrip begin to trigger buffering of subsequent messages
                Thread.Sleep(200);
                r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                r.Tell(new Counter.Get(2));
                ExpectMsg(4);
            }, _second);
            EnterBarrier("after-4");
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_support_proxy_only_mode()
        {
            ClusterSharding_should_support_passivation_and_activation_of_entities();

            Within(TimeSpan.FromSeconds(10), () =>
            {
                RunOn(() =>
                {
                    var cfg = ConfigurationFactory.ParseString(@"
                        retry-interval = 1s
                        buffer-size = 1000")
                        .WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));

                    var settings = ClusterShardingSettings.Create(cfg, Sys.Settings.Config.GetConfig("akka.cluster.singleton"));
                    var proxy = Sys.ActorOf(ShardRegion.ProxyProps(
                        typeName: "counter",
                        settings: settings,
                        coordinatorPath: "/user/counterCoordinator/singleton/coordinator",
                        extractEntityId: Counter.ExtractEntityId,
                        extractShardId: Counter.ExtractShardId));

                    proxy.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    proxy.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    proxy.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    proxy.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    proxy.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    proxy.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));

                    proxy.Tell(new Counter.Get(1));
                    ExpectMsg(2);
                    proxy.Tell(new Counter.Get(2));
                    ExpectMsg(4);
                }, _second);
                EnterBarrier("after-5");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_failover_shards_on_crashed_node()
        {
            ClusterSharding_should_support_proxy_only_mode();

            Within(TimeSpan.FromSeconds(30), () =>
            {
                // mute logging of deadLetters during shutdown of systems
                if (!Log.IsDebugEnabled) Sys.EventStream.Publish(new Mute(new DeadLettersFilter(new PredicateMatcher(x => true), new PredicateMatcher(x => true))));
                EnterBarrier("logs-muted");

                RunOn(() =>
                {
                    TestConductor.Exit(_second, 0).Wait();
                }, _controller);
                EnterBarrier("crash-second");

                RunOn(() =>
                {
                    var probe1 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            var r = _region.Value;
                            r.Tell(new Counter.Get(2), probe1.Ref);
                            probe1.ExpectMsg(4);
                            Assert.Equal(r.Path / "2" / "2", probe1.LastSender.Path);
                        });
                    });

                    var probe2 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            var r = _region.Value;
                            r.Tell(new Counter.Get(12), probe2.Ref);
                            probe2.ExpectMsg(1);
                            Assert.Equal(r.Path / "0" / "12", probe2.LastSender.Path);
                        });
                    });
                }, _first);
                EnterBarrier("after-6");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_use_third_and_fourth_node()
        {
            ClusterSharding_should_failover_shards_on_crashed_node();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                Join(_third, _first);
                Join(_fourth, _first);

                RunOn(() =>
                {
                    var r = _region.Value;
                    for (int i = 0; i < 10; i++)
                        r.Tell(new Counter.EntityEnvelope(3, Counter.Increment.Instance));

                    r.Tell(new Counter.Get(3));
                    ExpectMsg(10);
                    Assert.Equal(r.Path / "3" / "3", LastSender.Path);
                }, _third);
                EnterBarrier("third-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    for (int i = 0; i < 20; i++)
                        r.Tell(new Counter.EntityEnvelope(4, Counter.Increment.Instance));

                    r.Tell(new Counter.Get(4));
                    ExpectMsg(20);
                    Assert.Equal(r.Path / "4" / "4", LastSender.Path);
                }, _fourth);
                EnterBarrier("fourth-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.EntityEnvelope(3, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(3));
                    ExpectMsg(11);
                    Assert.Equal(Node(_third) / "user" / "counterRegion" / "3" / "3", LastSender.Path);

                    r.Tell(new Counter.EntityEnvelope(4, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(4));
                    ExpectMsg(21);
                    Assert.Equal(Node(_third) / "user" / "counterRegion" / "4" / "4", LastSender.Path);
                }, _first);
                EnterBarrier("first-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.Get(3));
                    ExpectMsg(11);
                    Assert.Equal(r.Path / "3" / "3", LastSender.Path);
                }, _third);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.Get(4));
                    ExpectMsg(21);
                    Assert.Equal(r.Path / "4" / "4", LastSender.Path);
                }, _fourth);
                EnterBarrier("after-7");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_recover_coordinator_state_after_coordinator_crash()
        {
            ClusterSharding_should_use_third_and_fourth_node();

            Within(TimeSpan.FromSeconds(60), () =>
            {
                Join(_fifth, _fourth);
                RunOn(() =>
                {
                    TestConductor.Exit(_first, 0).Wait();
                }, _controller);
                EnterBarrier("crash-first");

                RunOn(() =>
                {
                    var probe3 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            _region.Value.Tell(new Counter.Get(3), probe3.Ref);
                            probe3.ExpectMsg(11);
                            Assert.Equal(Node(_third) / "user" / "counterRegion" / "3" / "3", probe3.LastSender.Path);
                        });
                    });

                    var probe4 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            _region.Value.Tell(new Counter.Get(4), probe4.Ref);
                            probe4.ExpectMsg(21);
                            Assert.Equal(Node(_fourth) / "user" / "counterRegion" / "4" / "4", probe4.LastSender.Path);
                        });
                    });
                }, _fifth);
                EnterBarrier("after-8");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_rebalance_to_nodes_with_less_shards()
        {
            ClusterSharding_should_recover_coordinator_state_after_coordinator_crash();

            Within(TimeSpan.FromSeconds(60), () =>
            {
                RunOn(() =>
                {
                    for (int i = 1; i <= 10; i++)
                    {
                        var rebalancingRegion = _rebalancingRegion.Value;
                        rebalancingRegion.Tell(new Counter.EntityEnvelope(i, Counter.Increment.Instance));
                        rebalancingRegion.Tell(new Counter.Get(i));
                        ExpectMsg(1);
                    }
                }, _fourth);
                EnterBarrier("rebalancing-shards-allocated");

                Join(_sixth, _third);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        var probe = CreateTestProbe();
                        Within(TimeSpan.FromSeconds(3), () =>
                        {
                            var count = 0;
                            for (int i = 1; i <= 10; i++)
                            {
                                var rebalancingRegion = _rebalancingRegion.Value;
                                rebalancingRegion.Tell(new Counter.Get(i), probe.Ref);
                                probe.ExpectMsg<int>();
                                if (probe.LastSender.Path.Equals(rebalancingRegion.Path / (i % 12).ToString() / i.ToString()))
                                    count++;
                            }

                            Assert.True(count >= 2);
                        });
                    });
                }, _sixth);
                EnterBarrier("after-9");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_be_easy_to_use_with_extensions()
        {
            ClusterSharding_should_rebalance_to_nodes_with_less_shards();

            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    //#counter-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: "Counter",
                        entityProps: Props.Create<Counter>(),
                        settings: ClusterShardingSettings.Create(Sys),
                        idExtractor: Counter.ExtractEntityId,
                        shardResolver: Counter.ExtractShardId);

                    //#counter-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: "AnotherCounter",
                        entityProps: Props.Create<Counter>(),
                        settings: ClusterShardingSettings.Create(Sys),
                        idExtractor: Counter.ExtractEntityId,
                        shardResolver: Counter.ExtractShardId);
                }, _third, _fourth, _fifth, _sixth);
                EnterBarrier("extension-started");

                RunOn(() =>
                {
                    //#counter-usage
                    var counterRegion = ClusterSharding.Get(Sys).ShardRegion("Counter");
                    counterRegion.Tell(new Counter.Get(123));
                    ExpectMsg(0);

                    counterRegion.Tell(new Counter.EntityEnvelope(123, Counter.Increment.Instance));
                    counterRegion.Tell(new Counter.Get(123));
                    ExpectMsg(1);

                    //#counter-usage
                    var anotherCounterRegion = ClusterSharding.Get(Sys).ShardRegion("AnotherCounter");
                    anotherCounterRegion.Tell(new Counter.EntityEnvelope(123, Counter.Decrement.Instance));
                    anotherCounterRegion.Tell(new Counter.Get(123));
                    ExpectMsg(-1);
                }, _fifth);
                EnterBarrier("extension-used");

                // sixth is a frontend node, i.e. proxy only
                RunOn(() =>
                {
                    for (int i = 1000; i <= 1010; i++)
                    {
                        ClusterSharding.Get(Sys).ShardRegion("Counter").Tell(new Counter.EntityEnvelope(i, Counter.Increment.Instance));
                        ClusterSharding.Get(Sys).ShardRegion("Counter").Tell(new Counter.Get(i));
                        ExpectMsg(1);
                        Assert.NotEqual(Cluster.SelfAddress, LastSender.Path.Address);
                    }
                }, _sixth);
                EnterBarrier("after-10");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_be_easy_API_for_starting()
        {
            ClusterSharding_should_be_easy_to_use_with_extensions();

            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var counterRegionViaStart = ClusterSharding.Get(Sys).Start(
                        typeName: "ApiTest",
                        entityProps: Props.Create<Counter>(),
                        settings: ClusterShardingSettings.Create(Sys),
                        idExtractor: Counter.ExtractEntityId,
                        shardResolver: Counter.ExtractShardId);

                    var counterRegionViaGet = ClusterSharding.Get(Sys).ShardRegion("ApiTest");

                    Assert.Equal(counterRegionViaGet, counterRegionViaStart);
                }, _first);
                EnterBarrier("after-11");
            });
        }

        #endregion

        #region Persistent cluster shards specs

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_recover_entities_upon_restart()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var x = _persistentEntitiesRegion.Value;
                    var y = _anotherPersistentRegion.Value;
                }, _third, _fourth, _fifth);
                EnterBarrier("persistent-start");

                RunOn(() =>
                {
                    //Create an increment counter 1
                    _persistentEntitiesRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _persistentEntitiesRegion.Value.Tell(new Counter.EntityEnvelope(1, new Counter.Get(1)));
                    ExpectMsg(1);

                    //Shut down the shard and confirm it's dead
                    var shard = Sys.ActorSelection(LastSender.Path.Parent);
                    var region = Sys.ActorSelection(LastSender.Path.Parent.Parent);

                    // stop shard
                    region.Tell(new PersistentShardCoordinator.HandOff("1"));
                    ExpectMsg<PersistentShardCoordinator.ShardStopped>(s => s.Shard == "1", TimeSpan.FromSeconds(10));

                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        shard.Tell(new Identify(1), probe.Ref);
                        probe.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(1) && i.Subject == null, TimeSpan.FromSeconds(1));
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    //Get the path to where the shard now resides
                    _persistentEntitiesRegion.Value.Tell(new Counter.Get(13));
                    ExpectMsg(0);

                    //Check that counter 1 is now alive again, even though we have
                    // not sent a message to it via the ShardRegion
                    var counter1 = Sys.ActorSelection(LastSender.Path.Parent / "1");
                    counter1.Tell(new Identify(2));
                    Assert.NotNull(ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject);

                    counter1.Tell(new Counter.Get(1));
                    ExpectMsg(1);
                }, _third);
                EnterBarrier("after-shard-restart");

                RunOn(() =>
                {
                    //Check a second region does not share the same persistent shards

                    //Create a separate 13 counter
                    _anotherPersistentRegion.Value.Tell(new Counter.EntityEnvelope(13, Counter.Increment.Instance));
                    _anotherPersistentRegion.Value.Tell(new Counter.Get(13));
                    ExpectMsg(1);

                    //Check that no counter "1" exists in this shard
                    var secondCounter1 = Sys.ActorSelection(LastSender.Path.Parent / "1");
                    secondCounter1.Tell(new Identify(3));
                    ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(3) && i.Subject == null, TimeSpan.FromSeconds(3));
                }, _fourth);
                EnterBarrier("after-12");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_permanently_stop_entities_which_passivate()
        {
            Persistent_cluster_shards_should_recover_entities_upon_restart();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var x = _persistentRegion.Value;
                }, _third, _fourth, _fifth);
                EnterBarrier("cluster-started-12");

                RunOn(() =>
                {
                    //create and increment counter 1
                    _persistentRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _persistentRegion.Value.Tell(new Counter.Get(1));
                    ExpectMsg(1);

                    var counter1 = LastSender;
                    var shard = Sys.ActorSelection(counter1.Path.Parent);
                    var region = Sys.ActorSelection(counter1.Path.Parent.Parent);

                    //create and increment counter 13
                    _persistentRegion.Value.Tell(new Counter.EntityEnvelope(13, Counter.Increment.Instance));
                    _persistentRegion.Value.Tell(new Counter.Get(13));
                    ExpectMsg(1);

                    var counter13 = LastSender;

                    Assert.Equal(counter1.Path.Parent, counter13.Path.Parent);

                    //Send the shard the passivate message from the counter
                    Watch(counter1);
                    shard.Tell(new Passivate(Counter.Stop.Instance), counter1);

                    // watch for the Terminated message
                    ExpectTerminated(counter1, TimeSpan.FromSeconds(5));

                    var probe1 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        // check counter 1 is dead
                        counter1.Tell(new Identify(1), probe1.Ref);
                        probe1.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(1) && i.Subject == null, TimeSpan.FromSeconds(1));
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    // stop shard cleanly
                    region.Tell(new PersistentShardCoordinator.HandOff("1"));
                    ExpectMsg<PersistentShardCoordinator.ShardStopped>(s => s.Shard == "1", TimeSpan.FromSeconds(10));

                    var probe2 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        shard.Tell(new Identify(2), probe2.Ref);
                        probe1.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(1) && i.Subject == null, TimeSpan.FromSeconds(1));
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                }, _third);
                EnterBarrier("shard-shutdonw-12");

                RunOn(() =>
                {
                    // force shard backup
                    _persistentRegion.Value.Tell(new Counter.Get(25));
                    ExpectMsg(0);

                    var shard = LastSender.Path.Parent;

                    // check counter 1 is still dead
                    Sys.ActorSelection(shard / "1").Tell(new Identify(3));
                    ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(3) && i.Subject == null);

                    // check counter 13 is alive again
                    Sys.ActorSelection(shard / "13").Tell(new Identify(4));
                    ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(4) && i.Subject != null);

                }, _fourth);
                EnterBarrier("after-13");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_restart_entities_which_stop_without_passivation()
        {
            Persistent_cluster_shards_should_permanently_stop_entities_which_passivate();

            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var x = _persistentRegion.Value;
                }, _third, _fourth);
                EnterBarrier("cluster-started-12");

                RunOn(() =>
                {
                    //create and increment counter 1
                    _persistentRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _persistentRegion.Value.Tell(new Counter.Get(1));
                    ExpectMsg(2);

                    var counter1 = Sys.ActorSelection(LastSender.Path);
                    counter1.Tell(Counter.Stop.Instance);

                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        counter1.Tell(new Identify(1), probe.Ref);
                        Assert.NotNull(probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(1)).Subject);
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
                }, _third);
                EnterBarrier("after-14");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_be_migrated_to_new_regions_upon_region_failure()
        {
            Persistent_cluster_shards_should_restart_entities_which_stop_without_passivation();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                //Start only one region, and force an entity onto that region
                RunOn(() =>
                {
                    _autoMigrateRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _autoMigrateRegion.Value.Tell(new Counter.Get(1));
                    ExpectMsg(1);
                }, _third);
                EnterBarrier("shard1-region3");

                //Start another region and test it talks to node 3
                RunOn(() =>
                {
                    _autoMigrateRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _autoMigrateRegion.Value.Tell(new Counter.Get(1));
                    ExpectMsg(2);

                    Assert.Equal(Node(_third) / "user" / "AutoMigrateRegionTestRegion" / "1" / "1", LastSender.Path);

                    // kill region 3
                    Sys.ActorSelection(LastSender.Path.Parent.Parent).Tell(PoisonPill.Instance);
                }, _fourth);
                EnterBarrier("region4-up");

                // Wait for migration to happen
                //Test the shard, thus counter was moved onto node 4 and started.
                RunOn(() =>
                {
                    var counter1 = Sys.ActorSelection(ActorPath.Parse("user") / "AutoMigrateRegionTestRegion" / "1" / "1");
                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        counter1.Tell(new Identify(1), probe.Ref);
                        Assert.NotNull(probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(1)).Subject);
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    counter1.Tell(new Counter.Get(1));
                    ExpectMsg(2);
                }, _fourth);
                EnterBarrier("after-15");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_ensure_rebalance_restarts_shards()
        {
            Persistent_cluster_shards_should_be_migrated_to_new_regions_upon_region_failure();

            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    for (int i = 1; i <= 12; i++)
                        _rebalancingPersistentRegion.Value.Tell(new Counter.EntityEnvelope(i, Counter.Increment.Instance));

                    for (int i = 1; i <= 12; i++)
                    {
                        _rebalancingPersistentRegion.Value.Tell(new Counter.Get(i));
                        ExpectMsg(1);
                    }
                }, _fourth);
                EnterBarrier("entities-started");

                RunOn(() =>
                {
                    var r = _rebalancingPersistentRegion.Value;
                }, _fifth);
                EnterBarrier("fifth-joined-shard");

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        var count = 0;
                        for (int i = 2; i <= 12; i++)
                        {
                            var entity = Sys.ActorSelection(_rebalancingPersistentRegion.Value.Path / (i % 12).ToString() / i.ToString());
                            entity.Tell(new Identify(i));

                            var msg = ReceiveOne(TimeSpan.FromSeconds(3)) as ActorIdentity;
                            if (msg != null && msg.Subject != null)
                                count++;
                        }

                        Assert.True(count >= 2);
                    });
                }, _fifth);
                EnterBarrier("after-16");
            });
        }

        #endregion
    }
}