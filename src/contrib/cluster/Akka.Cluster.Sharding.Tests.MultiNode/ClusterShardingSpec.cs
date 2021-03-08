//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.Pattern;
using Akka.Persistence;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util;
using FluentAssertions;
using static Akka.Cluster.Sharding.ShardCoordinator;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }
        public RoleName Sixth { get; }

        public ClusterShardingSpecConfig(
            StateStoreMode mode,
            RememberEntitiesStore rememberEntitiesStore,
            string entityRecoveryStrategy = "all")
            : base(mode: mode, rememberEntitiesStore: rememberEntitiesStore, loglevel: "DEBUG")
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
            Sixth = Role("sixth");

            // This is the only test that creates the shared store regardless of mode,
            // because it uses a PersistentActor. So unlike all other uses of
            // `MultiNodeClusterShardingConfig`, we use `MultiNodeConfig.commonConfig` here,
            // and call `MultiNodeClusterShardingConfig.persistenceConfig` which does not check
            // mode, then leverage the common config and fallbacks after these specific test configs:
            CommonConfig = ConfigurationFactory.ParseString($@"
                akka.cluster.sharding.verbose-debug-logging = on
                #akka.loggers = [""akka.testkit.SilenceAllTestEventListener""]

                akka.cluster.roles = [""backend""]
                akka.cluster.distributed-data.gossip-interval = 1s
                akka.persistence.journal.sqlite-shared.timeout = 10s #the original default, base test uses 5s
                akka.cluster.sharding {{
                    retry-interval = 1 s
                    handoff-timeout = 10 s
                    shard-start-timeout = 5s
                    entity-restart-backoff = 1s
                    rebalance-interval = 2 s
                    entity-recovery-strategy = ""{entityRecoveryStrategy}""
                    entity-recovery-constant-rate-strategy {{
                        frequency = 1 ms
                        number-of-entities = 1
                    }}
                    least-shard-allocation-strategy {{
                        rebalance-absolute-limit = 1
                        rebalance-relative-limit = 1.0
                    }}
                }}
                akka.testconductor.barrier-timeout = 70s
              ").WithFallback(PersistenceConfig()).WithFallback(Common);

            NodeConfig(new[] { Sixth }, new[] { ConfigurationFactory.ParseString(@"akka.cluster.roles = [""frontend""]") });
        }
    }

    public class PersistentClusterShardingSpecConfig : ClusterShardingSpecConfig
    {
        public PersistentClusterShardingSpecConfig()
            : base(StateStoreMode.Persistence, RememberEntitiesStore.Eventsourced)
        {
        }
    }

    public class DDataClusterShardingSpecConfig : ClusterShardingSpecConfig
    {
        public DDataClusterShardingSpecConfig()
            : base(StateStoreMode.DData, RememberEntitiesStore.DData)
        {
        }
    }

    public class PersistentClusterShardingWithEntityRecoverySpecConfig : ClusterShardingSpecConfig
    {
        public PersistentClusterShardingWithEntityRecoverySpecConfig()
            : base(StateStoreMode.Persistence, RememberEntitiesStore.Eventsourced, "constant")
        {
        }
    }

    public class DDataClusterShardingWithEntityRecoverySpecConfig : ClusterShardingSpecConfig
    {
        public DDataClusterShardingWithEntityRecoverySpecConfig()
            : base(StateStoreMode.DData, RememberEntitiesStore.DData, "constant")
        {
        }
    }

    public class PersistentClusterShardingSpec : ClusterShardingSpec
    {
        public PersistentClusterShardingSpec()
            : base(new PersistentClusterShardingSpecConfig(), typeof(PersistentClusterShardingSpec))
        {
        }
    }

    public class DDataClusterShardingSpec : ClusterShardingSpec
    {
        public DDataClusterShardingSpec()
            : base(new DDataClusterShardingSpecConfig(), typeof(DDataClusterShardingSpec))
        {
        }
    }

    public class PersistentClusterShardingWithEntityRecoverySpec : ClusterShardingSpec
    {
        public PersistentClusterShardingWithEntityRecoverySpec()
            : base(new PersistentClusterShardingWithEntityRecoverySpecConfig(), typeof(PersistentClusterShardingWithEntityRecoverySpec))
        {
        }
    }

    public class DDataClusterShardingWithEntityRecoverySpec : ClusterShardingSpec
    {
        public DDataClusterShardingWithEntityRecoverySpec()
            : base(new DDataClusterShardingWithEntityRecoverySpecConfig(), typeof(DDataClusterShardingWithEntityRecoverySpec))
        {
        }
    }

    public abstract class ClusterShardingSpec : MultiNodeClusterShardingSpec<ClusterShardingSpecConfig>
    {
        #region Setup

        [Serializable]
        internal sealed class Increment
        {
            public static readonly Increment Instance = new Increment();

            private Increment()
            {
            }
        }

        [Serializable]
        internal sealed class Decrement
        {
            public static readonly Decrement Instance = new Decrement();

            private Decrement()
            {
            }
        }

        [Serializable]
        internal sealed class Get
        {
            public readonly long CounterId;
            public Get(long counterId)
            {
                CounterId = counterId;
            }
        }

        [Serializable]
        internal sealed class EntityEnvelope
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
        internal sealed class Stop
        {
            public static readonly Stop Instance = new Stop();

            private Stop()
            {
            }
        }

        [Serializable]
        internal sealed class CounterChanged
        {
            public readonly int Delta;
            public CounterChanged(int delta)
            {
                Delta = delta;
            }
        }

        internal class Counter : PersistentActor
        {
            private int _count = 0;

            public static Props Props() => Actor.Props.Create(() => new Counter());

            public Counter()
            {
                Context.SetReceiveTimeout(TimeSpan.FromSeconds(120));
            }

            protected override void PostStop()
            {
                base.PostStop();
                // Simulate that the passivation takes some time, to verify passivation buffering
                Thread.Sleep(500);
            }

            public override string PersistenceId { get { return $"Counter-{Self.Path.Name}"; } }

            protected override bool ReceiveRecover(object message)
            {
                switch (message)
                {
                    case CounterChanged cc:
                        UpdateState(cc);
                        return true;
                }
                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                switch (message)
                {
                    case Increment _:
                        Persist(new CounterChanged(1), UpdateState);
                        return true;
                    case Decrement _:
                        Persist(new CounterChanged(-1), UpdateState);
                        return true;
                    case Get _:
                        Sender.Tell(_count);
                        return true;
                    case ReceiveTimeout _:
                        Context.Parent.Tell(new Passivate(Stop.Instance));
                        return true;
                    case Stop _:
                        Context.Stop(Self);
                        return true;
                }
                return false;
            }

            private void UpdateState(CounterChanged e)
            {
                _count += e.Delta;
            }
        }

        private static readonly ExtractEntityId ExtractEntityId = message =>
        {
            switch (message)
            {
                case EntityEnvelope env:
                    return (env.Id.ToString(), env.Payload);
                case Get msg:
                    return (msg.CounterId.ToString(), message);
            }
            return Option<(string, object)>.None;
        };

        private static readonly ExtractShardId ExtractShardId = message =>
        {
            switch (message)
            {
                case EntityEnvelope msg:
                    return (msg.Id % NumberOfShards).ToString();
                case Get msg:
                    return (msg.CounterId % NumberOfShards).ToString();
                case ShardRegion.StartEntity msg:
                    return (long.Parse(msg.EntityId) % NumberOfShards).ToString();
            }
            return null;
        };

        private const int NumberOfShards = 12;


        internal class QualifiedCounter : Counter
        {
            public static Props Props(string typeName)
            {
                return Actor.Props.Create(() => new QualifiedCounter(typeName));
            }

            private readonly string typeName;

            public QualifiedCounter(string typeName)
                : base()
            {
                this.typeName = typeName;
            }

            public override string PersistenceId => typeName + "-" + Self.Path.Name;
        }

        internal class AnotherCounter : QualifiedCounter
        {
            public static new Props Props()
            {
                return Actor.Props.Create(() => new AnotherCounter());
            }

            public AnotherCounter()
                : base("AnotherCounter")
            {
            }
        }

        internal class CounterSupervisor : ActorBase
        {
            public static Props Props()
            {
                return Actor.Props.Create(() => new CounterSupervisor());
            }

            public readonly IActorRef counter;

            public CounterSupervisor()
            {
                counter = Context.ActorOf(Counter.Props(), "theCounter");
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(Decider.From(ex =>
                {
                    switch (ex)
                    {
                        //case _: IllegalArgumentException     ⇒ SupervisorStrategy.Resume
                        //case _: ActorInitializationException ⇒ SupervisorStrategy.Stop
                        //case _: DeathPactException           ⇒ SupervisorStrategy.Stop
                        //case _: Exception                    ⇒ SupervisorStrategy.Restart
                        case ActorInitializationException _:
                        case DeathPactException _:
                            return Directive.Stop;
                        default:
                            return Directive.Restart;
                    }
                }));
            }

            protected override bool Receive(object message)
            {
                counter.Forward(message);
                return true;
            }
        }

        private readonly Lazy<IActorRef> _replicator;
        private readonly Lazy<IActorRef> _region;
        private readonly Lazy<IActorRef> _rebalancingRegion;
        private readonly Lazy<IActorRef> _persistentEntitiesRegion;
        private readonly Lazy<IActorRef> _anotherPersistentRegion;
        private readonly Lazy<IActorRef> _persistentRegion;
        private readonly Lazy<IActorRef> _rebalancingPersistentRegion;
        private readonly Lazy<IActorRef> _autoMigrateRegion;

        protected ClusterShardingSpec(ClusterShardingSpecConfig config, Type type)
            : base(config, type)
        {
            _replicator = new Lazy<IActorRef>(() => Sys.ActorOf(
                Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1))
                .WithMaxDeltaElements(10)),
                "replicator")
            );

            _region = new Lazy<IActorRef>(() => CreateRegion("counter", false));
            _rebalancingRegion = new Lazy<IActorRef>(() => CreateRegion("rebalancingCounter", rememberEntities: false));

            _persistentEntitiesRegion = new Lazy<IActorRef>(() => CreateRegion("RememberCounterEntities", rememberEntities: true));
            _anotherPersistentRegion = new Lazy<IActorRef>(() => CreateRegion("AnotherRememberCounter", rememberEntities: true));
            _persistentRegion = new Lazy<IActorRef>(() => CreateRegion("RememberCounter", rememberEntities: true));
            _rebalancingPersistentRegion = new Lazy<IActorRef>(() => CreateRegion("RebalancingRememberCounter", rememberEntities: true));
            _autoMigrateRegion = new Lazy<IActorRef>(() => CreateRegion("AutoMigrateRememberRegionTest", rememberEntities: true));
        }


        private void Join(RoleName from, RoleName to)
        {
            Join(from, to, CreateCoordinator);
        }

        private DDataRememberEntitiesProvider DdataRememberEntitiesProvider(string typeName)
        {
            var majorityMinCap = Sys.Settings.Config.GetInt("akka.cluster.sharding.distributed-data.majority-min-cap");
            return new DDataRememberEntitiesProvider(typeName, settings.Value, majorityMinCap, _replicator.Value);
        }

        private EventSourcedRememberEntitiesProvider EventSourcedRememberEntitiesProvider(string typeName, ClusterShardingSettings settings)
        {
            return new EventSourcedRememberEntitiesProvider(typeName, settings);
        }

        private void CreateCoordinator()
        {

            Props CoordinatorProps(string typeName, bool rebalanceEnabled, bool rememberEntities)
            {
                var allocationStrategy =
                    ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 2, relativeLimit: 1.0);
                var cfg = ConfigurationFactory.ParseString($@"
                    handoff-timeout = 10s
                    shard-start-timeout = 10s
                    rebalance-interval = {(rebalanceEnabled ? "2s" : "3600s")}
                ").WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));
                var settings = ClusterShardingSettings.Create(cfg, Sys.Settings.Config.GetConfig("akka.cluster.singleton"))
                    .WithRememberEntities(rememberEntities);

                if (settings.StateStoreMode == StateStoreMode.Persistence)
                    return PersistentShardCoordinator.Props(typeName, settings, allocationStrategy);
                else
                {
                    var majorityMinCap = Sys.Settings.Config.GetInt("akka.cluster.sharding.distributed-data.majority-min-cap");

                    // only store provider if ddata for now, persistence uses all-in-one-coordinator
                    var rememberEntitiesStore = (settings.RememberEntities) ? DdataRememberEntitiesProvider(typeName) : null;

                    return DDataShardCoordinator.Props(
                        typeName,
                        settings,
                        allocationStrategy,
                        _replicator.Value,
                        majorityMinCap,
                        rememberEntitiesStore);
                }
            }

            var typeNames = new[]
            {
                "counter",
                "rebalancingCounter",
                "RememberCounterEntities",
                "AnotherRememberCounter",
                "RememberCounter",
                "RebalancingRememberCounter",
                "AutoMigrateRememberRegionTest"
            };

            foreach (var typeName in typeNames)
            {
                var rebalanceEnabled = typeName.ToLowerInvariant().StartsWith("rebalancing");
                var rememberEnabled = typeName.ToLowerInvariant().Contains("remember");
                var singletonProps =
                    BackoffSupervisor.Props(
                        childProps: CoordinatorProps(typeName, rebalanceEnabled, rememberEnabled),
                        childName: "coordinator",
                        minBackoff: TimeSpan.FromSeconds(5),
                        maxBackoff: TimeSpan.FromSeconds(5),
                        randomFactor: 0.1)
                        .WithDeploy(Deploy.Local);

                Sys.ActorOf(
                    ClusterSingletonManager.Props(
                        singletonProps,
                        terminationMessage: Terminate.Instance,
                        settings: ClusterSingletonManagerSettings.Create(Sys)),
                        name: typeName + "Coordinator");
            }
        }


        private IActorRef CreateRegion(string typeName, bool rememberEntities)
        {
            var cfg = ConfigurationFactory.ParseString(@"
                retry-interval = 1s
                shard-failure-backoff = 1s
                entity-restart-backoff = 1s
                buffer-size = 1000
            ").WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));
            var settings = ClusterShardingSettings.Create(cfg, Sys.Settings.Config.GetConfig("akka.cluster.singleton"))
                .WithRememberEntities(rememberEntities);

            IRememberEntitiesProvider rememberEntitiesProvider = null;
            if (rememberEntities)
            {
                switch (settings.RememberEntitiesStore)
                {
                    case RememberEntitiesStore.DData:
                        rememberEntitiesProvider = DdataRememberEntitiesProvider(typeName);
                        break;
                    case RememberEntitiesStore.Eventsourced:
                        rememberEntitiesProvider = EventSourcedRememberEntitiesProvider(typeName, settings);
                        break;
                }
            }

            return Sys.ActorOf(
                ShardRegion.Props(
                    typeName: typeName,
                    entityProps: _ => QualifiedCounter.Props(typeName),
                    settings: settings,
                    coordinatorPath: "/user/" + typeName + "Coordinator/singleton/coordinator",
                    extractEntityId: ExtractEntityId,
                    extractShardId: ExtractShardId,
                    handOffStopMessage: PoisonPill.Instance,
                    rememberEntitiesProvider: rememberEntitiesProvider),
                name: typeName + "Region");
        }

        #endregion

        #region Cluster shardings specs

        [MultiNodeFact]
        public void ClusterSharding_specs()
        {
            // must be done also in ddata mode since Counter is PersistentActor
            Cluster_sharding_must_setup_shared_journal();
            Cluster_sharding_must_work_in_single_node_cluster();
            Cluster_sharding_must_use_second_node();
            Cluster_sharding_must_support_passivation_and_activation_of_entities();
            Cluster_sharding_must_support_proxy_only_mode();
            Cluster_sharding_must_failover_shards_on_crashed_node();
            Cluster_sharding_must_use_third_and_fourth_node();
            Cluster_sharding_must_recover_coordinator_state_after_coordinator_crash();
            Cluster_sharding_must_rebalance_to_nodes_with_less_shards();
            Cluster_sharding_must_be_easy_to_use_with_extensions();
            Cluster_sharding_must_be_easy_API_for_starting();

            if (!IsDdataMode)
            {
                Persistent_Cluster_Shards_must_recover_entities_upon_restart();
                Persistent_Cluster_Shards_must_permanently_stop_entities_which_passivate();
                Persistent_Cluster_Shards_must_restart_entities_which_stop_without_passivation();
                Persistent_Cluster_Shards_must_be_migrated_to_new_regions_upon_region_failure();
                Persistent_Cluster_Shards_must_ensure_rebalance_restarts_shards();
            }
        }

        private void Cluster_sharding_must_setup_shared_journal()
        {
            StartPersistence(config.Controller,
                config.First, config.Second, config.Third, config.Fourth, config.Fifth, config.Sixth);
        }

        private void Cluster_sharding_must_work_in_single_node_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(config.First, config.First);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new EntityEnvelope(1, Increment.Instance));
                    r.Tell(new EntityEnvelope(1, Increment.Instance));
                    r.Tell(new EntityEnvelope(1, Increment.Instance));
                    r.Tell(new EntityEnvelope(1, Decrement.Instance));
                    r.Tell(new Get(1));
                    ExpectMsg(2);

                    r.Tell(GetCurrentRegions.Instance);
                    ExpectMsg(new CurrentRegions(ImmutableHashSet.Create(Cluster.SelfAddress)));
                }, config.First);

                EnterBarrier("after-2");
            });
        }

        private void Cluster_sharding_must_use_second_node()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(config.Second, config.First);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new EntityEnvelope(2, Increment.Instance));
                    r.Tell(new EntityEnvelope(2, Increment.Instance));
                    r.Tell(new EntityEnvelope(2, Increment.Instance));
                    r.Tell(new EntityEnvelope(2, Decrement.Instance));
                    r.Tell(new Get(2));
                    ExpectMsg(2);

                    r.Tell(new EntityEnvelope(11, Increment.Instance));
                    r.Tell(new EntityEnvelope(12, Increment.Instance));
                    r.Tell(new Get(11));
                    ExpectMsg(1);
                    r.Tell(new Get(12));
                    ExpectMsg(1);
                }, config.Second);
                EnterBarrier("second-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new EntityEnvelope(2, Increment.Instance));
                    r.Tell(new Get(2));
                    ExpectMsg(3);
                    LastSender.Path.Should().Be(Node(config.Second) / "user" / "counterRegion" / "2" / "2");

                    r.Tell(new Get(11));
                    ExpectMsg(1);
                    // local on first
                    var path11 = LastSender.Path;
                    LastSender.Path.ToStringWithoutAddress().Should().Be((r.Path / "11" / "11").ToStringWithoutAddress());
                    //LastSender.Path.Should().Be((r.Path / "11" / "11"));
                    r.Tell(new Get(12));
                    ExpectMsg(1);
                    var path12 = LastSender.Path;
                    LastSender.Path.ToStringWithoutAddress().Should().Be((r.Path / "0" / "12").ToStringWithoutAddress());
                    //LastSender.Path.Should().Be(Node(config.Second) / "user" / "counterRegion" / "0" / "12");

                    //one has to be local, the other one remote
                    (path11.Address.HasLocalScope && path12.Address.HasGlobalScope || path11.Address.HasGlobalScope && path12.Address.HasLocalScope).Should().BeTrue();
                }, config.First);
                EnterBarrier("first-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Get(2));
                    ExpectMsg(3);
                    LastSender.Path.Should().Be(r.Path / "2" / "2");

                    r.Tell(GetCurrentRegions.Instance);
                    ExpectMsg(new CurrentRegions(ImmutableHashSet.Create(Cluster.SelfAddress, Node(config.First).Address)));
                }, config.Second);
                EnterBarrier("after-3");
            });
        }

        private void Cluster_sharding_must_support_passivation_and_activation_of_entities()
        {
            RunOn(() =>
            {
                var r = _region.Value;
                r.Tell(new Get(2));
                ExpectMsg(3);
                r.Tell(new EntityEnvelope(2, ReceiveTimeout.Instance));
                // let the Passivate-Stop roundtrip begin to trigger buffering of subsequent messages
                Thread.Sleep(200);
                r.Tell(new EntityEnvelope(2, Increment.Instance));
                r.Tell(new Get(2));
                ExpectMsg(4);
            }, config.Second);
            EnterBarrier("after-4");
        }

        private void Cluster_sharding_must_support_proxy_only_mode()
        {
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
                        extractEntityId: ExtractEntityId,
                        extractShardId: ExtractShardId),
                        "regionProxy");

                    proxy.Tell(new Get(1));
                    ExpectMsg(2);
                    proxy.Tell(new Get(2));
                    ExpectMsg(4);
                }, config.Second);
                EnterBarrier("after-5");
            });
        }

        private void Cluster_sharding_must_failover_shards_on_crashed_node()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // mute logging of deadLetters during shutdown of systems
                if (!Log.IsDebugEnabled)
                    Sys.EventStream.Publish(new Mute(new DeadLettersFilter(new PredicateMatcher(x => true), new PredicateMatcher(x => true))));
                EnterBarrier("logs-muted");

                RunOn(() =>
                {
                    TestConductor.Exit(config.Second, 0).Wait();
                }, config.Controller);
                EnterBarrier("crash-second");

                RunOn(() =>
                {
                    var probe1 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            var r = _region.Value;
                            r.Tell(new Get(2), probe1.Ref);
                            probe1.ExpectMsg(4);
                            probe1.LastSender.Path.Should().Be(r.Path / "2" / "2");
                        });
                    });

                    var probe2 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            var r = _region.Value;
                            r.Tell(new Get(12), probe2.Ref);
                            probe2.ExpectMsg(1);
                            probe2.LastSender.Path.Should().Be(r.Path / "0" / "12");
                        });
                    });
                }, config.First);
                EnterBarrier("after-6");
            });
        }

        private void Cluster_sharding_must_use_third_and_fourth_node()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                Join(config.Third, config.First);

                RunOn(() =>
                {
                    var r = _region.Value;
                    for (int i = 1; i <= 10; i++)
                        r.Tell(new EntityEnvelope(3, Increment.Instance));

                    r.Tell(new Get(3));
                    ExpectMsg(10);
                    LastSender.Path.Should().Be(r.Path / "3" / "3"); // local
                }, config.Third);
                EnterBarrier("third-update");

                Join(config.Fourth, config.First);

                RunOn(() =>
                {
                    var r = _region.Value;
                    for (int i = 1; i <= 20; i++)
                        r.Tell(new EntityEnvelope(4, Increment.Instance));

                    r.Tell(new Get(4));
                    ExpectMsg(20);
                    LastSender.Path.Should().Be(r.Path / "4" / "4"); // local
                }, config.Fourth);
                EnterBarrier("fourth-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new EntityEnvelope(3, Increment.Instance));
                    r.Tell(new Get(3));
                    ExpectMsg(11);
                    LastSender.Path.Should().Be(Node(config.Third) / "user" / "counterRegion" / "3" / "3");

                    r.Tell(new EntityEnvelope(4, Increment.Instance));
                    r.Tell(new Get(4));
                    ExpectMsg(21);
                    LastSender.Path.Should().Be(Node(config.Fourth) / "user" / "counterRegion" / "4" / "4");
                }, config.First);
                EnterBarrier("first-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Get(3));
                    ExpectMsg(11);
                    LastSender.Path.Should().Be(r.Path / "3" / "3");
                }, config.Third);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Get(4));
                    ExpectMsg(21);
                    LastSender.Path.Should().Be(r.Path / "4" / "4");
                }, config.Fourth);
                EnterBarrier("after-7");
            });
        }

        private void Cluster_sharding_must_recover_coordinator_state_after_coordinator_crash()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                Join(config.Fifth, config.Fourth);
                RunOn(() =>
                {
                    TestConductor.Exit(config.First, 0).Wait();
                }, config.Controller);
                EnterBarrier("crash-first");

                RunOn(() =>
                {
                    var probe3 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            _region.Value.Tell(new Get(3), probe3.Ref);
                            probe3.ExpectMsg(11);
                            probe3.LastSender.Path.Should().Be(Node(config.Third) / "user" / "counterRegion" / "3" / "3");
                        });
                    });

                    var probe4 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            _region.Value.Tell(new Get(4), probe4.Ref);
                            probe4.ExpectMsg(21);
                            probe4.LastSender.Path.Should().Be(Node(config.Fourth) / "user" / "counterRegion" / "4" / "4");
                        });
                    });
                }, config.Fifth);
                EnterBarrier("after-8");
            });
        }

        private void Cluster_sharding_must_rebalance_to_nodes_with_less_shards()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                RunOn(() =>
                {
                    for (int i = 1; i <= 10; i++)
                    {
                        var rebalancingRegion = this._rebalancingRegion.Value;
                        rebalancingRegion.Tell(new EntityEnvelope(i, Increment.Instance));
                        rebalancingRegion.Tell(new Get(i));
                        ExpectMsg(1);
                    }
                }, config.Fourth);
                EnterBarrier("rebalancing-shards-allocated");

                Join(config.Sixth, config.Third);

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
                                var rebalancingRegion = this._rebalancingRegion.Value;
                                rebalancingRegion.Tell(new Get(i), probe.Ref);
                                probe.ExpectMsg<int>();
                                if (probe.LastSender.Path.Equals(rebalancingRegion.Path / (i % 12).ToString() / i.ToString()))
                                    count++;
                            }

                            count.Should().BeGreaterOrEqualTo(2);
                        });
                    });
                }, config.Sixth);
                EnterBarrier("after-9");
            });
        }

        private void Cluster_sharding_must_be_easy_to_use_with_extensions()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    //#counter-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: "Counter",
                        entityProps: Counter.Props(),
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: ExtractEntityId,
                        extractShardId: ExtractShardId);

                    //#counter-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: "AnotherCounter",
                        entityProps: AnotherCounter.Props(),
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: ExtractEntityId,
                        extractShardId: ExtractShardId);

                    //#counter-supervisor-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: "SupervisedCounter",
                        entityProps: CounterSupervisor.Props(),
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: ExtractEntityId,
                        extractShardId: ExtractShardId);
                }, config.Third, config.Fourth, config.Fifth, config.Sixth);
                EnterBarrier("extension-started");

                RunOn(() =>
                {
                    //#counter-usage
                    var counterRegion = ClusterSharding.Get(Sys).ShardRegion("Counter");
                    var entityId = 123;
                    counterRegion.Tell(new Get(entityId));
                    ExpectMsg(0);

                    counterRegion.Tell(new EntityEnvelope(entityId, Increment.Instance));
                    counterRegion.Tell(new Get(entityId));
                    ExpectMsg(1);
                    //#counter-usage

                    var anotherCounterRegion = ClusterSharding.Get(Sys).ShardRegion("AnotherCounter");
                    anotherCounterRegion.Tell(new EntityEnvelope(entityId, Decrement.Instance));
                    anotherCounterRegion.Tell(new Get(entityId));
                    ExpectMsg(-1);
                }, config.Fifth);
                EnterBarrier("extension-used");

                // sixth is a frontend node, i.e. proxy only
                RunOn(() =>
            {
                for (int i = 1000; i <= 1010; i++)
                {
                    ClusterSharding.Get(Sys).ShardRegion("Counter").Tell(new EntityEnvelope(i, Increment.Instance));
                    ClusterSharding.Get(Sys).ShardRegion("Counter").Tell(new Get(i));
                    ExpectMsg(1);
                    LastSender.Path.Address.Should().NotBe(Cluster.SelfAddress);
                }
            }, config.Sixth);
                EnterBarrier("after-10");
            });
        }

        private void Cluster_sharding_must_be_easy_API_for_starting()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var counterRegionViaStart = ClusterSharding.Get(Sys).Start(
                        typeName: "ApiTest",
                        entityProps: Counter.Props(),
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: ExtractEntityId,
                        extractShardId: ExtractShardId);

                    var counterRegionViaGet = ClusterSharding.Get(Sys).ShardRegion("ApiTest");

                    counterRegionViaStart.Should().Be(counterRegionViaGet);
                }, config.First);
                EnterBarrier("after-11");
            });
        }

        #endregion

        #region Persistent cluster shards specs

        private void Persistent_Cluster_Shards_must_recover_entities_upon_restart()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    _ = _persistentEntitiesRegion.Value;
                    _ = _anotherPersistentRegion.Value;
                }, config.Third, config.Fourth, config.Fifth);
                EnterBarrier("persistent-start");

                // watch-out, these two var are only init on 3rd node
                ActorSelection shard = null;
                ActorSelection region = null;
                RunOn(() =>
                {
                    //Create an increment counter 1
                    _persistentEntitiesRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                    _persistentEntitiesRegion.Value.Tell(new Get(1));
                    ExpectMsg(1);

                    shard = Sys.ActorSelection(LastSender.Path.Parent);
                    region = Sys.ActorSelection(LastSender.Path.Parent.Parent);
                }, config.Third);
                EnterBarrier("counter-incremented");


                // clean up shard cache everywhere
                RunOn(() =>
                {
                    _persistentEntitiesRegion.Value.Tell(new BeginHandOff("1"));
                    ExpectMsg(new BeginHandOffAck("1"), TimeSpan.FromSeconds(10), "ShardStopped not received");
                }, config.Third, config.Fourth, config.Fifth);
                EnterBarrier("everybody-hand-off-ack");



                RunOn(() =>
                {
                    //Stop the shard cleanly
                    region.Tell(new HandOff("1"));
                    ExpectMsg(new ShardStopped("1"), TimeSpan.FromSeconds(10), "ShardStopped not received");

                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        shard.Tell(new Identify(1), probe.Ref);
                        probe.ExpectMsg(new ActorIdentity(1, null), TimeSpan.FromSeconds(1), "Shard was still around");
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    //Get the path to where the shard now resides
                    AwaitAssert(() =>
                    {
                        _persistentEntitiesRegion.Value.Tell(new Get(13));
                        ExpectMsg(0);
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    //Check that counter 1 is now alive again, even though we have
                    // not sent a message to it via the ShardRegion
                    var counter1 = Sys.ActorSelection(LastSender.Path.Parent / "1");
                    Within(TimeSpan.FromSeconds(5), () =>
                    {
                        AwaitAssert(() =>
                        {
                            var probe2 = CreateTestProbe();
                            counter1.Tell(new Identify(2), probe2.Ref);
                            probe2.ExpectMsg<ActorIdentity>(i => i.Subject != null, TimeSpan.FromSeconds(2));
                        });
                    });

                    counter1.Tell(new Get(1));
                    ExpectMsg(1);
                }, config.Third);
                EnterBarrier("after-shard-restart");

                RunOn(() =>
                {
                    //Check a second region does not share the same persistent shards

                    //Create a separate 13 counter
                    _anotherPersistentRegion.Value.Tell(new EntityEnvelope(13, Increment.Instance));
                    _anotherPersistentRegion.Value.Tell(new Get(13));
                    ExpectMsg(1);

                    //Check that no counter "1" exists in this shard
                    var secondCounter1 = Sys.ActorSelection(LastSender.Path.Parent / "1");
                    secondCounter1.Tell(new Identify(3));
                    ExpectMsg(new ActorIdentity(3, null), TimeSpan.FromSeconds(3));
                }, config.Fourth);
                EnterBarrier("after-12");
            });
        }

        private void Persistent_Cluster_Shards_must_permanently_stop_entities_which_passivate()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    _ = _persistentRegion.Value;
                }, config.Third, config.Fourth, config.Fifth);
                EnterBarrier("cluster-started-12");

                RunOn(() =>
                {
                    //create and increment counter 1
                    _persistentRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                    _persistentRegion.Value.Tell(new Get(1));
                    ExpectMsg(1);

                    var counter1 = LastSender;
                    var shard = Sys.ActorSelection(counter1.Path.Parent);
                    var region = Sys.ActorSelection(counter1.Path.Parent.Parent);

                    //create and increment counter 13
                    _persistentRegion.Value.Tell(new EntityEnvelope(13, Increment.Instance));
                    _persistentRegion.Value.Tell(new Get(13));
                    ExpectMsg(1);

                    var counter13 = LastSender;

                    counter13.Path.Parent.Should().Be(counter1.Path.Parent);

                    //Send the shard the passivate message from the counter
                    Watch(counter1);
                    shard.Tell(new Passivate(Stop.Instance), counter1);

                    // watch for the Terminated message
                    ExpectTerminated(counter1, TimeSpan.FromSeconds(5));

                    var probe1 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        // check counter 1 is dead
                        counter1.Tell(new Identify(1), probe1.Ref);
                        probe1.ExpectMsg(new ActorIdentity(1, null), TimeSpan.FromSeconds(1), "Entity 1 was still around");
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    // stop shard cleanly
                    region.Tell(new HandOff("1"));
                    ExpectMsg(new ShardStopped("1"), TimeSpan.FromSeconds(10), "ShardStopped not received");

                    var probe2 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        shard.Tell(new Identify(2), probe2.Ref);
                        probe2.ExpectMsg(new ActorIdentity(2, null), TimeSpan.FromSeconds(1), "Shard was still around");
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                }, config.Third);
                EnterBarrier("shard-shutdonw-12");

                RunOn(() =>
                {
                    // force shard backup
                    _persistentRegion.Value.Tell(new Get(25));
                    ExpectMsg(0);

                    var shard = LastSender.Path.Parent;

                    // check counter 1 is still dead
                    Sys.ActorSelection(shard / "1").Tell(new Identify(3));
                    ExpectMsg(new ActorIdentity(3, null));

                    // check counter 13 is alive again
                    var probe3 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection(shard / "13").Tell(new Identify(4), probe3.Ref);
                        probe3.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(4) && i.Subject != null);
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
                }, config.Fourth);
                EnterBarrier("after-13");
            });
        }

        private void Persistent_Cluster_Shards_must_restart_entities_which_stop_without_passivation()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    _ = _persistentRegion.Value;
                }, config.Third, config.Fourth);
                EnterBarrier("cluster-started-12");

                RunOn(() =>
                {
                    //create and increment counter 1
                    _persistentRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                    _persistentRegion.Value.Tell(new Get(1));
                    ExpectMsg(2);

                    var counter1 = Sys.ActorSelection(LastSender.Path);
                    counter1.Tell(Stop.Instance);

                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        counter1.Tell(new Identify(1), probe.Ref);
                        probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(1)).Subject.Should().NotBeNull();
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
                }, config.Third);
                EnterBarrier("after-14");
            });
        }

        private void Persistent_Cluster_Shards_must_be_migrated_to_new_regions_upon_region_failure()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                //Start only one region, and force an entity onto that region
                RunOn(() =>
                {
                    _autoMigrateRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                    _autoMigrateRegion.Value.Tell(new Get(1));
                    ExpectMsg(1);
                }, config.Third);
                EnterBarrier("shard1-region3");

                //Start another region and test it talks to node 3
                RunOn(() =>
                {
                    _autoMigrateRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                    _autoMigrateRegion.Value.Tell(new Get(1));
                    ExpectMsg(2);

                    LastSender.Path.Should().Be(Node(config.Third) / "user" / "AutoMigrateRememberRegionTestRegion" / "1" / "1");

                    // kill region 3
                    Sys.ActorSelection(LastSender.Path.Parent.Parent).Tell(PoisonPill.Instance);
                }, config.Fourth);
                EnterBarrier("region4-up");

                // Wait for migration to happen
                //Test the shard, thus counter was moved onto node 4 and started.
                RunOn(() =>
                {
                    var counter1 = Sys.ActorSelection("user/AutoMigrateRememberRegionTestRegion/1/1");
                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        counter1.Tell(new Identify(1), probe.Ref);
                        probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(1)).Subject.Should().NotBeNull();
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    counter1.Tell(new Get(1));
                    ExpectMsg(2);
                }, config.Fourth);
                EnterBarrier("after-15");
            });
        }

        private void Persistent_Cluster_Shards_must_ensure_rebalance_restarts_shards()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    for (int i = 2; i <= 12; i++)
                        _rebalancingPersistentRegion.Value.Tell(new EntityEnvelope(i, Increment.Instance));

                    for (int i = 2; i <= 12; i++)
                    {
                        _rebalancingPersistentRegion.Value.Tell(new Get(i));
                        ExpectMsg(1);
                    }
                }, config.Fourth);
                EnterBarrier("entities-started");

                RunOn(() =>
                {
                    _ = _rebalancingPersistentRegion.Value;
                }, config.Fifth);
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
                            if (msg != null && msg.Subject != null && msg.MessageId.Equals(i))
                                count++;
                        }

                        count.Should().BeGreaterOrEqualTo(2);
                    });
                }, config.Fifth);
                EnterBarrier("after-16");
            });
        }

        #endregion
    }
}
