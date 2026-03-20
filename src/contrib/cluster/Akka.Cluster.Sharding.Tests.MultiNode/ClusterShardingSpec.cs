//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.MultiNode.TestAdapter;
using Akka.Pattern;
using Akka.Persistence;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util;
using FluentAssertions;
using static Akka.Cluster.Sharding.ShardCoordinator;

namespace Akka.Cluster.Sharding.Tests;

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
        // mode, then leverage the common _config and fallbacks after these specific test configs:
        CommonConfig = ConfigurationFactory.ParseString($@"
                akka.cluster.sharding.verbose-debug-logging = on
                #akka.loggers = [""akka.testkit.SilenceAllTestEventListener""]
                akka.cluster.auto-down-unreachable-after = 0s
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
                    distributed-data.durable.lmdb {{
                        dir = ""target/ClusterShardingSpec/sharding-ddata""
                        map-size = 10 MiB
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
        public static readonly Increment Instance = new();

        private Increment()
        {
        }
    }

    [Serializable]
    internal sealed class Decrement
    {
        public static readonly Decrement Instance = new();

        private Decrement()
        {
        }
    }

    [Serializable]
    internal sealed record Get(long CounterId);

    [Serializable]
    internal sealed record EntityEnvelope(long Id, object Payload);

    [Serializable]
    internal sealed class Stop
    {
        public static readonly Stop Instance = new();

        private Stop()
        {
        }
    }

    [Serializable]
    internal sealed record CounterChanged(int Delta);

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

    private sealed class MessageExtractor: IMessageExtractor
    {
        public string EntityId(object message)
            => message switch
            {
                EntityEnvelope env => env.Id.ToString(),
                Get msg => msg.CounterId.ToString(),
                _ => null
            };

        public object EntityMessage(object message)
            => message switch
            {
                EntityEnvelope env => env.Payload,
                _ => message
            };

        public string ShardId(object message)
            => message switch
            {
                EntityEnvelope env => (env.Id % NumberOfShards).ToString(),
                Get msg => (msg.CounterId % NumberOfShards).ToString(),
                _ => null
            };

        public string ShardId(string entityId, object messageHint = null)
            => (long.Parse(entityId) % NumberOfShards).ToString();
    }

    private const int NumberOfShards = 12;


    internal class QualifiedCounter : Counter
    {
        public static Props Props(string typeName)
        {
            return Actor.Props.Create(() => new QualifiedCounter(typeName));
        }

        private readonly string _typeName;

        public QualifiedCounter(string typeName)
            : base()
        {
            _typeName = typeName;
        }

        public override string PersistenceId => _typeName + "-" + Self.Path.Name;
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

        private readonly IActorRef _counter;

        public CounterSupervisor()
        {
            _counter = Context.ActorOf(Counter.Props(), "theCounter");
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
            _counter.Forward(message);
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


    private Task JoinAsync(RoleName from, RoleName to)
    {
        return JoinAsync(from, to, CreateCoordinator);
    }

    private DDataRememberEntitiesProvider DdataRememberEntitiesProvider(string typeName)
    {
        var majorityMinCap = Sys.Settings.Config.GetInt("akka.cluster.sharding.distributed-data.majority-min-cap");
        return new DDataRememberEntitiesProvider(typeName, Settings.Value, majorityMinCap, _replicator.Value);
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
                new MessageExtractor(),
                handOffStopMessage: PoisonPill.Instance,
                rememberEntitiesProvider: rememberEntitiesProvider),
            name: typeName + "Region");
    }

    #endregion

    #region Cluster shardings specs

    [MultiNodeFact]
    public async Task ClusterSharding_specs()
    {
        // must be done also in ddata mode since Counter is PersistentActor
        await ClusterSharding_should_setup_shared_journal();
        await ClusterSharding_should_work_in_single_node_cluster();
        await ClusterSharding_should_use_second_node();
        await ClusterSharding_should_support_passivation_and_activation_of_entities();
        await ClusterSharding_should_support_proxy_only_mode();
        await ClusterSharding_should_failover_shards_on_crashed_node();
        await ClusterSharding_should_use_third_and_fourth_node();
        await ClusterSharding_should_recover_coordinator_state_after_coordinator_crash();
        await ClusterSharding_should_rebalance_to_nodes_with_less_shards();
        await ClusterSharding_should_be_easy_to_use_with_extensions();
        await ClusterSharding_should_be_easy_API_for_starting();

        await PersistentClusterSharding_should_recover_entities_upon_restart();
        await PersistentClusterSharding_should_permanently_stop_entities_which_passivate();
        await PersistentClusterSharding_should_restart_entities_which_stop_without_passivation();
        await PersistentClusterSharding_should_be_migrated_to_new_regions_upon_region_failure();
        await PersistentClusterSharding_should_ensure_rebalance_restarts_shards();
    }

    private Task ClusterSharding_should_setup_shared_journal()
    {
        return StartPersistenceAsync(Config.Controller, CancellationToken.None,
            Config.First, Config.Second, Config.Third, Config.Fourth, Config.Fifth, Config.Sixth);
    }

    private async Task ClusterSharding_should_work_in_single_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            await JoinAsync(Config.First, Config.First);

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new EntityEnvelope(1, Increment.Instance));
                r.Tell(new EntityEnvelope(1, Increment.Instance));
                r.Tell(new EntityEnvelope(1, Increment.Instance));
                r.Tell(new EntityEnvelope(1, Decrement.Instance));
                r.Tell(new Get(1));
                await ExpectMsgAsync(2);

                r.Tell(GetCurrentRegions.Instance);
                await ExpectMsgAsync(new CurrentRegions(ImmutableHashSet.Create(Cluster.SelfAddress)));
            }, Config.First);

            await EnterBarrierAsync("after-2");
        });
    }

    private async Task ClusterSharding_should_use_second_node()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            await JoinAsync(Config.Second, Config.First);

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new EntityEnvelope(2, Increment.Instance));
                r.Tell(new EntityEnvelope(2, Increment.Instance));
                r.Tell(new EntityEnvelope(2, Increment.Instance));
                r.Tell(new EntityEnvelope(2, Decrement.Instance));
                r.Tell(new Get(2));
                await ExpectMsgAsync(2);

                r.Tell(new EntityEnvelope(11, Increment.Instance));
                r.Tell(new EntityEnvelope(12, Increment.Instance));
                r.Tell(new Get(11));
                await ExpectMsgAsync(1);
                r.Tell(new Get(12));
                await ExpectMsgAsync(1);
            }, Config.Second);
            await EnterBarrierAsync("second-update");

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new EntityEnvelope(2, Increment.Instance));
                r.Tell(new Get(2));
                await ExpectMsgAsync(3);
                LastSender.Path.Should().Be(await NodeAsync(Config.Second) / "user" / "counterRegion" / "2" / "2");

                r.Tell(new Get(11));
                await ExpectMsgAsync(1);
                // local on first
                var path11 = LastSender.Path;
                LastSender.Path.ToStringWithoutAddress().Should().Be((r.Path / "11" / "11").ToStringWithoutAddress());
                //LastSender.Path.Should().Be((r.Path / "11" / "11"));
                r.Tell(new Get(12));
                await ExpectMsgAsync(1);
                var path12 = LastSender.Path;
                LastSender.Path.ToStringWithoutAddress().Should().Be((r.Path / "0" / "12").ToStringWithoutAddress());
                //LastSender.Path.Should().Be(Node(_config.Second) / "user" / "counterRegion" / "0" / "12");

                //one has to be local, the other one remote
                (path11.Address.HasLocalScope && path12.Address.HasGlobalScope || path11.Address.HasGlobalScope && path12.Address.HasLocalScope).Should().BeTrue();
            }, Config.First);
            await EnterBarrierAsync("first-update");

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new Get(2));
                await ExpectMsgAsync(3);
                LastSender.Path.Should().Be(r.Path / "2" / "2");

                r.Tell(GetCurrentRegions.Instance);
                await ExpectMsgAsync(new CurrentRegions(ImmutableHashSet.Create(Cluster.SelfAddress, (await NodeAsync(Config.First)).Address)));
            }, Config.Second);
            await EnterBarrierAsync("after-3");
        });
    }

    private async Task ClusterSharding_should_support_passivation_and_activation_of_entities()
    {
        await RunOnAsync(async () =>
        {
            var r = _region.Value;
            r.Tell(new Get(2));
            await ExpectMsgAsync(3);
            r.Tell(new EntityEnvelope(2, ReceiveTimeout.Instance));
            // let the Passivate-Stop roundtrip begin to trigger buffering of subsequent messages
            await Task.Delay(200);
            r.Tell(new EntityEnvelope(2, Increment.Instance));
            r.Tell(new Get(2));
            await ExpectMsgAsync(4);
        }, Config.Second);
        await EnterBarrierAsync("after-4");
    }

    private async Task ClusterSharding_should_support_proxy_only_mode()
    {
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            await RunOnAsync(async () =>
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
                        new MessageExtractor()),
                    "regionProxy");

                proxy.Tell(new Get(1));
                await ExpectMsgAsync(2);
                proxy.Tell(new Get(2));
                await ExpectMsgAsync(4);
            }, Config.Second);
            await EnterBarrierAsync("after-5");
        });
    }

    private async Task ClusterSharding_should_failover_shards_on_crashed_node()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            // mute logging of deadLetters during shutdown of systems
            if (!Log.IsDebugEnabled)
                Sys.EventStream.Publish(new Mute(new DeadLettersFilter(new PredicateMatcher(_ => true), new PredicateMatcher(_ => true))));
            await EnterBarrierAsync("logs-muted");

            await RunOnAsync(async () =>
            {
                await TestConductor.ExitAsync(Config.Second, 0);
            }, Config.Controller);
            await EnterBarrierAsync("crash-second");

            await RunOnAsync(async () =>
            {
                var probe1 = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    await WithinAsync(TimeSpan.FromSeconds(1), async () =>
                    {
                        var r = _region.Value;
                        r.Tell(new Get(2), probe1.Ref);
                        await probe1.ExpectMsgAsync(4);
                        probe1.LastSender.Path.Should().Be(r.Path / "2" / "2");
                    });
                });

                var probe2 = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    await WithinAsync(TimeSpan.FromSeconds(1), async () =>
                    {
                        var r = _region.Value;
                        r.Tell(new Get(12), probe2.Ref);
                        await probe2.ExpectMsgAsync(1);
                        probe2.LastSender.Path.Should().Be(r.Path / "0" / "12");
                    });
                });
            }, Config.First);
            await EnterBarrierAsync("after-6");
        });
    }

    private async Task ClusterSharding_should_use_third_and_fourth_node()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await JoinAsync(Config.Third, Config.First);

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                for (var i = 1; i <= 10; i++)
                    r.Tell(new EntityEnvelope(3, Increment.Instance));

                r.Tell(new Get(3));
                await ExpectMsgAsync(10);
                LastSender.Path.Should().Be(r.Path / "3" / "3"); // local
            }, Config.Third);
            await EnterBarrierAsync("third-update");

            await JoinAsync(Config.Fourth, Config.First);

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                for (var i = 1; i <= 20; i++)
                    r.Tell(new EntityEnvelope(4, Increment.Instance));

                r.Tell(new Get(4));
                await ExpectMsgAsync(20);
                LastSender.Path.Should().Be(r.Path / "4" / "4"); // local
            }, Config.Fourth);
            await EnterBarrierAsync("fourth-update");

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new EntityEnvelope(3, Increment.Instance));
                r.Tell(new Get(3));
                await ExpectMsgAsync(11);
                LastSender.Path.Should().Be(await NodeAsync(Config.Third) / "user" / "counterRegion" / "3" / "3");

                r.Tell(new EntityEnvelope(4, Increment.Instance));
                r.Tell(new Get(4));
                await ExpectMsgAsync(21);
                LastSender.Path.Should().Be(await NodeAsync(Config.Fourth) / "user" / "counterRegion" / "4" / "4");
            }, Config.First);
            await EnterBarrierAsync("first-update");

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new Get(3));
                await ExpectMsgAsync(11);
                LastSender.Path.Should().Be(r.Path / "3" / "3");
            }, Config.Third);

            await RunOnAsync(async () =>
            {
                var r = _region.Value;
                r.Tell(new Get(4));
                await ExpectMsgAsync(21);
                LastSender.Path.Should().Be(r.Path / "4" / "4");
            }, Config.Fourth);
            await EnterBarrierAsync("after-7");
        });
    }

    private async Task ClusterSharding_should_recover_coordinator_state_after_coordinator_crash()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            await JoinAsync(Config.Fifth, Config.Fourth);
            await RunOnAsync(async () =>
            {
                await TestConductor.ExitAsync(Config.First, 0);
            }, Config.Controller);
            await EnterBarrierAsync("crash-first");

            await RunOnAsync(async () =>
            {
                var probe3 = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    await WithinAsync(TimeSpan.FromSeconds(1), async () =>
                    {
                        _region.Value.Tell(new Get(3), probe3.Ref);
                        await probe3.ExpectMsgAsync(11);
                        probe3.LastSender.Path.Should().Be(await NodeAsync(Config.Third) / "user" / "counterRegion" / "3" / "3");
                    });
                });

                var probe4 = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    await WithinAsync(TimeSpan.FromSeconds(1), async () =>
                    {
                        _region.Value.Tell(new Get(4), probe4.Ref);
                        await probe4.ExpectMsgAsync(21);
                        probe4.LastSender.Path.Should().Be(await NodeAsync(Config.Fourth) / "user" / "counterRegion" / "4" / "4");
                    });
                });
            }, Config.Fifth);
            await EnterBarrierAsync("after-8");
        });
    }

    private async Task ClusterSharding_should_rebalance_to_nodes_with_less_shards()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            await RunOnAsync(async () =>
            {
                for (int i = 1; i <= 10; i++)
                {
                    var rebalancingRegion = this._rebalancingRegion.Value;
                    rebalancingRegion.Tell(new EntityEnvelope(i, Increment.Instance));
                    rebalancingRegion.Tell(new Get(i));
                    await ExpectMsgAsync(1);
                }
            }, Config.Fourth);
            await EnterBarrierAsync("rebalancing-shards-allocated");

            await JoinAsync(Config.Sixth, Config.Third);

            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    await WithinAsync(TimeSpan.FromSeconds(3), async () =>
                    {
                        var count = 0;
                        for (int i = 1; i <= 10; i++)
                        {
                            var rebalancingRegion = this._rebalancingRegion.Value;
                            rebalancingRegion.Tell(new Get(i), probe.Ref);
                            await probe.ExpectMsgAsync<int>();
                            if (probe.LastSender.Path.Equals(rebalancingRegion.Path / (i % 12).ToString() / i.ToString()))
                                count++;
                        }

                        count.Should().BeGreaterOrEqualTo(2);
                    });
                });
            }, Config.Sixth);
            await EnterBarrierAsync("after-9");
        });
    }

    private async Task ClusterSharding_should_be_easy_to_use_with_extensions()
    {
        await WithinAsync(TimeSpan.FromSeconds(50), async () =>
        {
            RunOn(() =>
            {
                //#counter-start
                ClusterSharding.Get(Sys).Start(
                    typeName: "Counter",
                    entityProps: Counter.Props(),
                    settings: ClusterShardingSettings.Create(Sys),
                    messageExtractor: new MessageExtractor());

                //#counter-start
                ClusterSharding.Get(Sys).Start(
                    typeName: "AnotherCounter",
                    entityProps: AnotherCounter.Props(),
                    settings: ClusterShardingSettings.Create(Sys),
                    messageExtractor: new MessageExtractor());

                //#counter-supervisor-start
                ClusterSharding.Get(Sys).Start(
                    typeName: "SupervisedCounter",
                    entityProps: CounterSupervisor.Props(),
                    settings: ClusterShardingSettings.Create(Sys),
                    messageExtractor: new MessageExtractor());
            }, Config.Third, Config.Fourth, Config.Fifth, Config.Sixth);
            await EnterBarrierAsync("extension-started");

            await RunOnAsync(async () =>
            {
                //#counter-usage
                var counterRegion = ClusterSharding.Get(Sys).ShardRegion("Counter");
                var entityId = 123;
                counterRegion.Tell(new Get(entityId));
                await ExpectMsgAsync(0);

                counterRegion.Tell(new EntityEnvelope(entityId, Increment.Instance));
                counterRegion.Tell(new Get(entityId));
                await ExpectMsgAsync(1);
                //#counter-usage

                var anotherCounterRegion = ClusterSharding.Get(Sys).ShardRegion("AnotherCounter");
                anotherCounterRegion.Tell(new EntityEnvelope(entityId, Decrement.Instance));
                anotherCounterRegion.Tell(new Get(entityId));
                await ExpectMsgAsync(-1);
            }, Config.Fifth);
            await EnterBarrierAsync("extension-used");

            // sixth is a frontend node, i.e. proxy only
            await RunOnAsync(async () =>
            {
                for (int i = 1000; i <= 1010; i++)
                {
                    ClusterSharding.Get(Sys).ShardRegion("Counter").Tell(new EntityEnvelope(i, Increment.Instance));
                    ClusterSharding.Get(Sys).ShardRegion("Counter").Tell(new Get(i));
                    await ExpectMsgAsync(1);
                    LastSender.Path.Address.Should().NotBe(Cluster.SelfAddress);
                }
            }, Config.Sixth);
            await EnterBarrierAsync("after-10");
        });
    }

    private async Task ClusterSharding_should_be_easy_API_for_starting()
    {
        await WithinAsync(TimeSpan.FromSeconds(50), async () =>
        {
            RunOn(() =>
            {
                var counterRegionViaStart = ClusterSharding.Get(Sys).Start(
                    typeName: "ApiTest",
                    entityProps: Counter.Props(),
                    settings: ClusterShardingSettings.Create(Sys),
                    messageExtractor: new MessageExtractor());

                var counterRegionViaGet = ClusterSharding.Get(Sys).ShardRegion("ApiTest");

                counterRegionViaStart.Should().Be(counterRegionViaGet);
            }, Config.First);
            await EnterBarrierAsync("after-11");
        });
    }

    #endregion

    #region Persistent cluster shards specs

    private async Task PersistentClusterSharding_should_recover_entities_upon_restart()
    {
        await WithinAsync(TimeSpan.FromSeconds(50), async () =>
        {
            RunOn(() =>
            {
                _ = _persistentEntitiesRegion.Value;
                _ = _anotherPersistentRegion.Value;
            }, Config.Third, Config.Fourth, Config.Fifth);
            await EnterBarrierAsync("persistent-start");

            // watch-out, region var is only init on 3rd node
            ActorSelection region = null;
            await RunOnAsync(async () =>
            {
                //Create an increment counter 1
                _persistentEntitiesRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                _persistentEntitiesRegion.Value.Tell(new Get(1));
                await ExpectMsgAsync(1);

                region = Sys.ActorSelection(LastSender.Path.Parent.Parent);
            }, Config.Third);
            await EnterBarrierAsync("counter-incremented");


            // clean up shard cache everywhere
            await RunOnAsync(async () =>
            {
                _persistentEntitiesRegion.Value.Tell(new BeginHandOff("1"));
                await ExpectMsgAsync(new BeginHandOffAck("1"), TimeSpan.FromSeconds(10), "ShardStopped not received");
            }, Config.Third, Config.Fourth, Config.Fifth);
            await EnterBarrierAsync("everybody-hand-off-ack");



            await RunOnAsync(async () =>
            {
                //Stop the shard cleanly
                region.Tell(new HandOff("1"));
                await ExpectMsgAsync(new ShardStopped("1"), TimeSpan.FromSeconds(10), "ShardStopped not received");

                //Get the path to where the shard now resides
                await AwaitAssertAsync(async () =>
                {
                    _persistentEntitiesRegion.Value.Tell(new Get(13));
                    await ExpectMsgAsync(0);
                }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                //Check that counter 1 is now alive again, even though we have
                // not sent a message to it via the ShardRegion
                var counter1 = Sys.ActorSelection(LastSender.Path.Parent / "1");
                await WithinAsync(TimeSpan.FromSeconds(5), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        var probe2 = CreateTestProbe();
                        counter1.Tell(new Identify(2), probe2.Ref);
                        await probe2.ExpectMsgAsync<ActorIdentity>(i => i.Subject != null, TimeSpan.FromSeconds(2));
                    });
                });

                counter1.Tell(new Get(1));
                await ExpectMsgAsync(1);
            }, Config.Third);
            await EnterBarrierAsync("after-shard-restart");

            await RunOnAsync(async () =>
            {
                //Check a second region does not share the same persistent shards

                //Create a separate 13 counter
                _anotherPersistentRegion.Value.Tell(new EntityEnvelope(13, Increment.Instance));
                _anotherPersistentRegion.Value.Tell(new Get(13));
                await ExpectMsgAsync(1);

                //Check that no counter "1" exists in this shard
                var secondCounter1 = Sys.ActorSelection(LastSender.Path.Parent / "1");
                secondCounter1.Tell(new Identify(3));
                await ExpectMsgAsync(new ActorIdentity(3, null), TimeSpan.FromSeconds(3));
            }, Config.Fourth);
            await EnterBarrierAsync("after-12");
        });
    }

    private async Task PersistentClusterSharding_should_permanently_stop_entities_which_passivate()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            RunOn(() =>
            {
                _ = _persistentRegion.Value;
            }, Config.Third, Config.Fourth, Config.Fifth);
            await EnterBarrierAsync("cluster-started-12");

            await RunOnAsync(async () =>
            {
                //create and increment counter 1
                _persistentRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                _persistentRegion.Value.Tell(new Get(1));
                await ExpectMsgAsync(1);

                var counter1 = LastSender;
                var shard = Sys.ActorSelection(counter1.Path.Parent);
                var region = Sys.ActorSelection(counter1.Path.Parent.Parent);

                //create and increment counter 13
                _persistentRegion.Value.Tell(new EntityEnvelope(13, Increment.Instance));
                _persistentRegion.Value.Tell(new Get(13));
                await ExpectMsgAsync(1);

                var counter13 = LastSender;

                counter13.Path.Parent.Should().Be(counter1.Path.Parent);

                //Send the shard the passivate message from the counter
                await WatchAsync(counter1);
                shard.Tell(new Passivate(Stop.Instance), counter1);

                // watch for the Terminated message
                await ExpectTerminatedAsync(counter1, TimeSpan.FromSeconds(5));

                var probe1 = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    // check counter 1 is dead
                    counter1.Tell(new Identify(1), probe1.Ref);
                    await probe1.ExpectMsgAsync(new ActorIdentity(1, null), TimeSpan.FromSeconds(1), "Entity 1 was still around");
                }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                // stop shard cleanly
                region.Tell(new HandOff("1"));
                await ExpectMsgAsync(new ShardStopped("1"), TimeSpan.FromSeconds(10), "ShardStopped not received");

            }, Config.Third);
            await EnterBarrierAsync("shard-shutdonw-12");

            await RunOnAsync(async () =>
            {
                // force shard backup
                _persistentRegion.Value.Tell(new Get(25));
                await ExpectMsgAsync(0);

                var shard = LastSender.Path.Parent;

                // check counter 1 is still dead
                Sys.ActorSelection(shard / "1").Tell(new Identify(3));
                await ExpectMsgAsync(new ActorIdentity(3, null));

                // check counter 13 is alive again
                var probe3 = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    Sys.ActorSelection(shard / "13").Tell(new Identify(4), probe3.Ref);
                    await probe3.ExpectMsgAsync<ActorIdentity>(i => i.MessageId.Equals(4) && i.Subject != null);
                }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
            }, Config.Fourth);
            await EnterBarrierAsync("after-13");
        });
    }

    private async Task PersistentClusterSharding_should_restart_entities_which_stop_without_passivation()
    {
        await WithinAsync(TimeSpan.FromSeconds(50), async () =>
        {
            RunOn(() =>
            {
                _ = _persistentRegion.Value;
            }, Config.Third, Config.Fourth);
            await EnterBarrierAsync("cluster-started-12");

            await RunOnAsync(async () =>
            {
                //create and increment counter 1
                _persistentRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                _persistentRegion.Value.Tell(new Get(1));
                await ExpectMsgAsync(2);

                var counter1 = Sys.ActorSelection(LastSender.Path);
                counter1.Tell(Stop.Instance);

                var probe = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    counter1.Tell(new Identify(1), probe.Ref);
                    (await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(1))).Subject.Should().NotBeNull();
                }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
            }, Config.Third);
            await EnterBarrierAsync("after-14");
        });
    }

    private async Task PersistentClusterSharding_should_be_migrated_to_new_regions_upon_region_failure()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            //Start only one region, and force an entity onto that region
            await RunOnAsync(async () =>
            {
                _autoMigrateRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                _autoMigrateRegion.Value.Tell(new Get(1));
                await ExpectMsgAsync(1);
            }, Config.Third);
            await EnterBarrierAsync("shard1-region3");

            //Start another region and test it talks to node 3
            await RunOnAsync(async () =>
            {
                _autoMigrateRegion.Value.Tell(new EntityEnvelope(1, Increment.Instance));
                _autoMigrateRegion.Value.Tell(new Get(1));
                await ExpectMsgAsync(2);

                LastSender.Path.Should().Be(await NodeAsync(Config.Third) / "user" / "AutoMigrateRememberRegionTestRegion" / "1" / "1");

                // kill region 3
                Sys.ActorSelection(LastSender.Path.Parent.Parent).Tell(PoisonPill.Instance);
            }, Config.Fourth);
            await EnterBarrierAsync("region4-up");

            // Wait for migration to happen
            //Test the shard, thus counter was moved onto node 4 and started.
            await RunOnAsync(async () =>
            {
                var counter1 = Sys.ActorSelection("user/AutoMigrateRememberRegionTestRegion/1/1");
                var probe = CreateTestProbe();
                await AwaitAssertAsync(async () =>
                {
                    counter1.Tell(new Identify(1), probe.Ref);
                    (await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(1))).Subject.Should().NotBeNull();
                }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                counter1.Tell(new Get(1));
                await ExpectMsgAsync(2);
            }, Config.Fourth);
            await EnterBarrierAsync("after-15");
        });
    }

    private async Task PersistentClusterSharding_should_ensure_rebalance_restarts_shards()
    {
        await WithinAsync(TimeSpan.FromSeconds(50), async () =>
        {
            await RunOnAsync(async () =>
            {
                for (var i = 2; i <= 12; i++)
                    _rebalancingPersistentRegion.Value.Tell(new EntityEnvelope(i, Increment.Instance));

                for (var i = 2; i <= 12; i++)
                {
                    _rebalancingPersistentRegion.Value.Tell(new Get(i));
                    await ExpectMsgAsync(1);
                }
            }, Config.Fourth);
            await EnterBarrierAsync("entities-started");

            RunOn(() =>
            {
                _ = _rebalancingPersistentRegion.Value;
            }, Config.Fifth);
            await EnterBarrierAsync("fifth-joined-shard");

            await RunOnAsync(async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    var count = 0;
                    for (var i = 2; i <= 12; i++)
                    {
                        var entity = Sys.ActorSelection(_rebalancingPersistentRegion.Value.Path / (i % 12).ToString() / i.ToString());
                        entity.Tell(new Identify(i));

                        if (await ReceiveOneAsync(TimeSpan.FromSeconds(3)) is ActorIdentity { Subject: not null } msg && msg.MessageId.Equals(i))
                            count++;
                    }

                    count.Should().BeGreaterOrEqualTo(2);
                });
            }, Config.Fifth);
            await EnterBarrierAsync("after-16");
        });
    }

    #endregion
}