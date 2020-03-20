//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Remote.TestKit;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.DistributedData;
using Akka.Pattern;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class ClusterShardingSpecConfig : MultiNodeConfig
    {
        public string Mode { get; }
        public string EntityRecoveryStrategy { get; }
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }
        public RoleName Sixth { get; }

        protected ClusterShardingSpecConfig(string mode, string entityRecoveryStrategy = "all")
        {
            Mode = mode;
            EntityRecoveryStrategy = entityRecoveryStrategy;
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
            Sixth = Role("sixth");

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
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.roles = [""backend""]
                    akka.cluster.distributed-data.gossip-interval = 1s
                    akka.cluster.sharding {{
                        retry-interval = 1 s
                        handoff-timeout = 10 s
                        shard-start-timeout = 5s
                        entity-restart-backoff = 1s
                        rebalance-interval = 2 s
                        state-store-mode = ""{mode}""
                        entity-recovery-strategy = ""{entityRecoveryStrategy}""
                        entity-recovery-constant-rate-strategy {{
                            frequency = 1 ms
                            number-of-entities = 1
                        }}
                        least-shard-allocation-strategy {{
                            rebalance-threshold = 1
                            max-simultaneous-rebalance = 1
                        }}
                        distributed-data.durable.lmdb {{
                          dir = ""target/ClusterShardingSpec/sharding-ddata""
                          map-size = 10000000
                        }}
                    }}
                    akka.testconductor.barrier-timeout = 70s
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
                "))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new[] { Sixth }, new[] { ConfigurationFactory.ParseString(@"akka.cluster.roles = [""frontend""]") });
        }
    }
    public class PersistentClusterShardingSpecConfig : ClusterShardingSpecConfig
    {
        public PersistentClusterShardingSpecConfig() : base("persistence") { }
    }
    public class DDataClusterShardingSpecConfig : ClusterShardingSpecConfig
    {
        public DDataClusterShardingSpecConfig() : base("ddata") { }
    }
    public class PersistentClusterShardingWithEntityRecoverySpecConfig : ClusterShardingSpecConfig
    {
        public PersistentClusterShardingWithEntityRecoverySpecConfig() : base("persistence", "constant") { }
    }
    public class DDataClusterShardingWithEntityRecoverySpecConfig : ClusterShardingSpecConfig
    {
        public DDataClusterShardingWithEntityRecoverySpecConfig() : base("ddata", "constant") { }
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

        public static readonly ExtractEntityId ExtractEntityId = message =>
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

        public static readonly ExtractShardId ExtractShardId = message =>
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

        public const int NumberOfShards = 12;
        private int _count = 0;
        private readonly string id;

        public static Props Props(string id) => Actor.Props.Create(() => new Counter(id));

        public static string ShardingTypeName => "Counter";

        public Counter(string id)
        {
            this.id = id;
            Context.SetReceiveTimeout(TimeSpan.FromMinutes(2));
        }

        protected override void PostStop()
        {
            base.PostStop();
            // Simulate that the passivation takes some time, to verify passivation buffering
            Thread.Sleep(500);
        }

        public override string PersistenceId { get { return $"Counter.{ShardingTypeName}-{id}"; } }

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

    internal class QualifiedCounter : Counter
    {
        public static Props Props(string typeName, string id)
        {
            return Actor.Props.Create(() => new QualifiedCounter(typeName, id));
        }

        public readonly string TypeName;

        public override string PersistenceId { get { return TypeName + "-" + Self.Path.Name; } }

        public QualifiedCounter(string typeName, string id)
            : base(id)
        {
            TypeName = typeName;
        }
    }

    internal class AnotherCounter : QualifiedCounter
    {
        public static new Props Props(string id)
        {
            return Actor.Props.Create(() => new AnotherCounter(id));
        }
        public static new string ShardingTypeName => nameof(AnotherCounter);

        public AnotherCounter(string id)
            : base(AnotherCounter.ShardingTypeName, id)
        {
        }
    }

    internal class CounterSupervisor : ActorBase
    {
        public static string ShardingTypeName => nameof(CounterSupervisor);

        public static Props Props(string id)
        {
            return Actor.Props.Create(() => new CounterSupervisor(id));
        }

        public readonly string entityId;
        public readonly IActorRef counter;

        public CounterSupervisor(string entityId)
        {
            this.entityId = entityId;
            counter = Context.ActorOf(Counter.Props(entityId), "theCounter");
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new AllForOneStrategy(Decider.From(ex =>
            {
                switch (ex)
                {
                    //case _: IllegalArgumentException     ⇒ SupervisorStrategy.Resume
                    //case _: ActorInitializationException ⇒ SupervisorStrategy.Stop
                    //case _: DeathPactException           ⇒ SupervisorStrategy.Stop
                    //case _: Exception                    ⇒ SupervisorStrategy.Restart

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

    public class PersistentClusterShardingSpec : ClusterShardingSpec
    {
        public PersistentClusterShardingSpec() : this(new PersistentClusterShardingSpecConfig()) { }
        protected PersistentClusterShardingSpec(PersistentClusterShardingSpecConfig config) : base(config, typeof(PersistentClusterShardingSpec)) { }
    }
    public class PersistentClusterShardingWithEntityRecoverySpec : ClusterShardingSpec
    {
        public PersistentClusterShardingWithEntityRecoverySpec() : this(new PersistentClusterShardingWithEntityRecoverySpecConfig()) { }
        protected PersistentClusterShardingWithEntityRecoverySpec(PersistentClusterShardingWithEntityRecoverySpecConfig config) : base(config, typeof(PersistentClusterShardingWithEntityRecoverySpec)) { }
    }
    public class DDataClusterShardingSpec : ClusterShardingSpec
    {
        public DDataClusterShardingSpec() : this(new DDataClusterShardingSpecConfig()) { }
        protected DDataClusterShardingSpec(DDataClusterShardingSpecConfig config) : base(config, typeof(DDataClusterShardingSpec)) { }
    }
    public class DDataClusterShardingWithEntityRecoverySpec : ClusterShardingSpec
    {
        public DDataClusterShardingWithEntityRecoverySpec() : this(new DDataClusterShardingWithEntityRecoverySpecConfig()) { }
        protected DDataClusterShardingWithEntityRecoverySpec(DDataClusterShardingWithEntityRecoverySpecConfig config) : base(config, typeof(DDataClusterShardingWithEntityRecoverySpec)) { }
    }
    public abstract class ClusterShardingSpec : MultiNodeClusterSpec
    {
        // must use different unique name for some tests than the one used in API tests
        public static string TestCounterShardingTypeName => $"Test{Counter.ShardingTypeName}";

        #region Setup

        private readonly Lazy<IActorRef> _region;
        private readonly Lazy<IActorRef> _rebalancingRegion;
        private readonly Lazy<IActorRef> _persistentEntitiesRegion;
        private readonly Lazy<IActorRef> _anotherPersistentRegion;
        private readonly Lazy<IActorRef> _persistentRegion;
        private readonly Lazy<IActorRef> _rebalancingPersistentRegion;
        private readonly Lazy<IActorRef> _autoMigrateRegion;

        private readonly ClusterShardingSpecConfig _config;
        private readonly List<FileInfo> _storageLocations;

        protected ClusterShardingSpec(ClusterShardingSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;

            _region = new Lazy<IActorRef>(() => CreateRegion(TestCounterShardingTypeName, false));
            _rebalancingRegion = new Lazy<IActorRef>(() => CreateRegion("rebalancingCounter", false));

            _persistentEntitiesRegion = new Lazy<IActorRef>(() => CreateRegion("RememberCounterEntities", true));
            _anotherPersistentRegion = new Lazy<IActorRef>(() => CreateRegion("AnotherRememberCounter", true));
            _persistentRegion = new Lazy<IActorRef>(() => CreateRegion("RememberCounter", true));
            _rebalancingPersistentRegion = new Lazy<IActorRef>(() => CreateRegion("RebalancingRememberCounter", true));
            _autoMigrateRegion = new Lazy<IActorRef>(() => CreateRegion("AutoMigrateRememberRegionTest", true));
            _storageLocations = new List<FileInfo>
            {
                new FileInfo(Sys.Settings.Config.GetString("akka.cluster.sharding.distributed-data.durable.lmdb.dir", null))
            };

            IsDDataMode = config.Mode == "ddata";

            DeleteStorageLocations();

            ReplicatorRef = Sys.ActorOf(Replicator.Props(ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1))
                .WithMaxDeltaElements(10)), "replicator");

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

        protected override int InitialParticipantsValueFactory => Roles.Count;
        public IActorRef ReplicatorRef { get; }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                CreateCoordinator();
            }, from);

            EnterBarrier(from.Name + "-joined");
        }

        private void CreateCoordinator()
        {
            var typeNames = new[]
            {
                TestCounterShardingTypeName, "rebalancingCounter", "RememberCounterEntities", "AnotherRememberCounter",
                "RememberCounter", "RebalancingRememberCounter", "AutoMigrateRememberRegionTest"
            };

            foreach (var typeName in typeNames)
            {
                var rebalanceEnabled = typeName.ToLowerInvariant().StartsWith("rebalancing");
                var rememberEnabled = typeName.ToLowerInvariant().Contains("remember");
                var singletonProps = BackoffSupervisor.Props(
                    CoordinatorProps(typeName, rebalanceEnabled, rememberEnabled),
                    "coordinator",
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(5),
                    0.1,
                    -1).WithDeploy(Deploy.Local);

                Sys.ActorOf(ClusterSingletonManager.Props(
                    singletonProps,
                    Terminate.Instance,
                    ClusterSingletonManagerSettings.Create(Sys)),
                    typeName + "Coordinator");
            }
        }

        private Props CoordinatorProps(string typeName, bool rebalanceEntities, bool rememberEntities)
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            var config = ConfigurationFactory.ParseString(string.Format(@"
                handoff-timeout = 10s
                shard-start-timeout = 10s
                rebalance-interval = " + (rebalanceEntities ? "2s" : "3600s")))
                .WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));
            var settings = ClusterShardingSettings.Create(config, Sys.Settings.Config.GetConfig("akka.cluster.singleton"))
                .WithRememberEntities(rememberEntities);
            return PersistentShardCoordinator.Props(typeName, settings, allocationStrategy);
        }

        private IActorRef CreateRegion(string typeName, bool rememberEntities)
        {
            var config = ConfigurationFactory.ParseString(@"
                retry-interval = 1s
                shard-failure-backoff = 1s
                entity-restart-backoff = 1s
                buffer-size = 1000")
                .WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));
            var settings = ClusterShardingSettings.Create(config, Sys.Settings.Config.GetConfig("akka.cluster.singleton"))
                .WithRememberEntities(rememberEntities);

            return Sys.ActorOf(Props.Create(() => new ShardRegion(
                typeName,
                entityId => QualifiedCounter.Props(typeName, entityId),
                settings,
                "/user/" + typeName + "Coordinator/singleton/coordinator",
                Counter.ExtractEntityId,
                Counter.ExtractShardId,
                PoisonPill.Instance,
                ReplicatorRef,
                3)),
                typeName + "Region");
        }

        #endregion

        #region Cluster shardings specs

        [MultiNodeFact]
        public void ClusterSharding_specs()
        {
            // must be done also in ddata mode since Counter is PersistentActor
            ClusterSharding_should_setup_shared_journal();
            ClusterSharding_should_work_in_single_node_cluster();
            ClusterSharding_should_use_second_node();
            ClusterSharding_should_support_passivation_and_activation_of_entities();
            ClusterSharding_should_support_proxy_only_mode();
            ClusterSharding_should_failover_shards_on_crashed_node();
            ClusterSharding_should_use_third_and_fourth_node();
            ClusterSharding_should_recover_coordinator_state_after_coordinator_crash();
            ClusterSharding_should_rebalance_to_nodes_with_less_shards();

            ClusterSharding_should_be_easy_to_use_with_extensions();

            ClusterSharding_should_be_easy_API_for_starting();

            if (!IsDDataMode)
            {
                PersistentClusterShards_should_recover_entities_upon_restart();
                PersistentClusterShards_should_permanently_stop_entities_which_passivate();
                PersistentClusterShards_should_restart_entities_which_stop_without_passivation();
                PersistentClusterShards_should_be_migrated_to_new_regions_upon_region_failure();
                PersistentClusterShards_should_ensure_rebalance_restarts_shards();
            }
        }

        public void ClusterSharding_should_setup_shared_journal()
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
            }, _config.First, _config.Second, _config.Third, _config.Fourth, _config.Fifth, _config.Sixth);
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

        public void ClusterSharding_should_work_in_single_node_cluster()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_config.First, _config.First);

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
                    ExpectMsg<CurrentRegions>(m => m.Regions.Count == 1 && m.Regions.Contains(Cluster.SelfAddress));
                }, _config.First);

                EnterBarrier("after-2");
            });
        }

        public void ClusterSharding_should_use_second_node()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_config.Second, _config.First);

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
                }, _config.Second);
                EnterBarrier("second-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(2));
                    ExpectMsg(3);
                    LastSender.Path.Should().Be(Node(_config.Second) / "user" / $"{TestCounterShardingTypeName}Region" / "2" / "2");

                    r.Tell(new Counter.Get(11));
                    ExpectMsg(1);
                    var path11 = LastSender.Path;
                    LastSender.Path.ToStringWithoutAddress().Should().Be((r.Path / "11" / "11").ToStringWithoutAddress());
                    r.Tell(new Counter.Get(12));
                    ExpectMsg(1);
                    var path12 = LastSender.Path;
                    LastSender.Path.ToStringWithoutAddress().Should().Be((r.Path / "0" / "12").ToStringWithoutAddress());

                    //one has to be local, the other one remote
                    (path11.Address.HasLocalScope && path12.Address.HasGlobalScope || path11.Address.HasGlobalScope && path12.Address.HasLocalScope).Should().BeTrue();
                }, _config.First);
                EnterBarrier("first-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.Get(2));
                    ExpectMsg(3);
                    LastSender.Path.Should().Be(r.Path / "2" / "2");

                    r.Tell(GetCurrentRegions.Instance);
                    ExpectMsg<CurrentRegions>(x => x.Regions.SetEquals(new[] { Cluster.SelfAddress, Node(_config.First).Address }));
                }, _config.Second);
                EnterBarrier("after-3");
            });
        }

        public void ClusterSharding_should_support_passivation_and_activation_of_entities()
        {
            RunOn(() =>
            {
                var r = _region.Value;
                r.Tell(new Counter.Get(2));
                ExpectMsg(3);
                r.Tell(new Counter.EntityEnvelope(2, ReceiveTimeout.Instance));
                // let the Passivate-Stop roundtrip begin to trigger buffering of subsequent messages
                Thread.Sleep(200);
                r.Tell(new Counter.EntityEnvelope(2, Counter.Increment.Instance));
                r.Tell(new Counter.Get(2));
                ExpectMsg(4);
            }, _config.Second);
            EnterBarrier("after-4");
        }

        public void ClusterSharding_should_support_proxy_only_mode()
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
                        typeName: TestCounterShardingTypeName,
                        settings: settings,
                        coordinatorPath: $"/user/{TestCounterShardingTypeName}Coordinator/singleton/coordinator",
                        extractEntityId: Counter.ExtractEntityId,
                        extractShardId: Counter.ExtractShardId,
                        replicator: Sys.DeadLetters,
                        majorityMinCap: 0
                        ), "regionProxy");

                    proxy.Tell(new Counter.Get(1));
                    ExpectMsg(2);
                    proxy.Tell(new Counter.Get(2));
                    ExpectMsg(4);
                }, _config.Second);
                EnterBarrier("after-5");
            });
        }

        public void ClusterSharding_should_failover_shards_on_crashed_node()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // mute logging of deadLetters during shutdown of systems
                if (!Log.IsDebugEnabled)
                    Sys.EventStream.Publish(new Mute(new DeadLettersFilter(new PredicateMatcher(x => true), new PredicateMatcher(x => true))));
                EnterBarrier("logs-muted");

                RunOn(() =>
                {
                    TestConductor.Exit(_config.Second, 0).Wait();
                }, _config.Controller);
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
                            probe1.LastSender.Path.Should().Be(r.Path / "2" / "2");
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
                            probe2.LastSender.Path.Should().Be(r.Path / "0" / "12");
                        });
                    });
                }, _config.First);
                EnterBarrier("after-6");
            });
        }

        public void ClusterSharding_should_use_third_and_fourth_node()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                Join(_config.Third, _config.First);

                RunOn(() =>
                {
                    var r = _region.Value;
                    for (int i = 0; i < 10; i++)
                        r.Tell(new Counter.EntityEnvelope(3, Counter.Increment.Instance));

                    r.Tell(new Counter.Get(3));
                    ExpectMsg(10);
                    LastSender.Path.Should().Be(r.Path / "3" / "3");
                }, _config.Third);
                EnterBarrier("third-update");

                Join(_config.Fourth, _config.First);

                RunOn(() =>
                {
                    var r = _region.Value;
                    for (int i = 0; i < 20; i++)
                        r.Tell(new Counter.EntityEnvelope(4, Counter.Increment.Instance));

                    r.Tell(new Counter.Get(4));
                    ExpectMsg(20);
                    LastSender.Path.Should().Be(r.Path / "4" / "4");
                }, _config.Fourth);
                EnterBarrier("fourth-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.EntityEnvelope(3, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(3));
                    ExpectMsg(11);
                    LastSender.Path.Should().Be(Node(_config.Third) / "user" / $"{TestCounterShardingTypeName}Region" / "3" / "3");

                    r.Tell(new Counter.EntityEnvelope(4, Counter.Increment.Instance));
                    r.Tell(new Counter.Get(4));
                    ExpectMsg(21);
                    LastSender.Path.Should().Be(Node(_config.Fourth) / "user" / $"{TestCounterShardingTypeName}Region" / "4" / "4");
                }, _config.First);
                EnterBarrier("first-update");

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.Get(3));
                    ExpectMsg(11);
                    LastSender.Path.Should().Be(r.Path / "3" / "3");
                }, _config.Third);

                RunOn(() =>
                {
                    var r = _region.Value;
                    r.Tell(new Counter.Get(4));
                    ExpectMsg(21);
                    LastSender.Path.Should().Be(r.Path / "4" / "4");
                }, _config.Fourth);
                EnterBarrier("after-7");
            });
        }

        public void ClusterSharding_should_recover_coordinator_state_after_coordinator_crash()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                Join(_config.Fifth, _config.Fourth);
                RunOn(() =>
                {
                    TestConductor.Exit(_config.First, 0).Wait();
                }, _config.Controller);
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
                            probe3.LastSender.Path.Should().Be(Node(_config.Third) / "user" / $"{TestCounterShardingTypeName}Region" / "3" / "3");
                        });
                    });

                    var probe4 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Within(TimeSpan.FromSeconds(1), () =>
                        {
                            _region.Value.Tell(new Counter.Get(4), probe4.Ref);
                            probe4.ExpectMsg(21);
                            probe4.LastSender.Path.Should().Be(Node(_config.Fourth) / "user" / $"{TestCounterShardingTypeName}Region" / "4" / "4");
                        });
                    });
                }, _config.Fifth);
                EnterBarrier("after-8");
            });
        }

        public void ClusterSharding_should_rebalance_to_nodes_with_less_shards()
        {
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
                }, _config.Fourth);
                EnterBarrier("rebalancing-shards-allocated");

                Join(_config.Sixth, _config.Third);

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

                            count.Should().BeGreaterOrEqualTo(2);
                        });
                    });
                }, _config.Sixth);
                EnterBarrier("after-9");
            });
        }

        public void ClusterSharding_should_be_easy_to_use_with_extensions()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    //#counter-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: Counter.ShardingTypeName,
                        entityPropsFactory: entityId => Counter.Props(entityId),
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: Counter.ExtractEntityId,
                        extractShardId: Counter.ExtractShardId);

                    //#counter-start
                    ClusterSharding.Get(Sys).Start(
                        typeName: AnotherCounter.ShardingTypeName,
                        entityPropsFactory: entityId => AnotherCounter.Props(entityId),
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: Counter.ExtractEntityId,
                        extractShardId: Counter.ExtractShardId);

                    //#counter-supervisor-start
                    ClusterSharding.Get(Sys).Start(
                      typeName: CounterSupervisor.ShardingTypeName,
                      entityPropsFactory: entityId => CounterSupervisor.Props(entityId),
                      settings: ClusterShardingSettings.Create(Sys),
                      extractEntityId: Counter.ExtractEntityId,
                      extractShardId: Counter.ExtractShardId);
                }, _config.Third, _config.Fourth, _config.Fifth, _config.Sixth);
                EnterBarrier("extension-started");

                RunOn(() =>
                {
                    //#counter-usage
                    var counterRegion = ClusterSharding.Get(Sys).ShardRegion(Counter.ShardingTypeName);
                    var entityId = 999;
                    counterRegion.Tell(new Counter.Get(entityId));
                    ExpectMsg(0);

                    counterRegion.Tell(new Counter.EntityEnvelope(entityId, Counter.Increment.Instance));
                    counterRegion.Tell(new Counter.Get(entityId));
                    ExpectMsg(1);
                    //#counter-usage

                    var anotherCounterRegion = ClusterSharding.Get(Sys).ShardRegion(AnotherCounter.ShardingTypeName);
                    anotherCounterRegion.Tell(new Counter.EntityEnvelope(entityId, Counter.Decrement.Instance));
                    anotherCounterRegion.Tell(new Counter.Get(entityId));
                    ExpectMsg(-1);
                }, _config.Fifth);
                EnterBarrier("extension-used");

                // sixth is a frontend node, i.e. proxy only
                RunOn(() =>
                {
                    for (int i = 1000; i <= 1010; i++)
                    {
                        ClusterSharding.Get(Sys).ShardRegion(Counter.ShardingTypeName).Tell(new Counter.EntityEnvelope(i, Counter.Increment.Instance));
                        ClusterSharding.Get(Sys).ShardRegion(Counter.ShardingTypeName).Tell(new Counter.Get(i));
                        ExpectMsg(1);
                        LastSender.Path.Address.Should().NotBe(Cluster.SelfAddress);
                    }
                }, _config.Sixth);
                EnterBarrier("after-10");
            });
        }

        public void ClusterSharding_should_be_easy_API_for_starting()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var counterRegionViaStart = ClusterSharding.Get(Sys).Start(
                        typeName: "ApiTest",
                        entityPropsFactory: Counter.Props,
                        settings: ClusterShardingSettings.Create(Sys),
                        extractEntityId: Counter.ExtractEntityId,
                        extractShardId: Counter.ExtractShardId);

                    var counterRegionViaGet = ClusterSharding.Get(Sys).ShardRegion("ApiTest");

                    counterRegionViaStart.Should().Be(counterRegionViaGet);
                }, _config.First);
                EnterBarrier("after-11");
            });
        }

        #endregion

        #region Persistent cluster shards specs

        public void PersistentClusterShards_should_recover_entities_upon_restart()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var x = _persistentEntitiesRegion.Value;
                    var y = _anotherPersistentRegion.Value;
                }, _config.Third, _config.Fourth, _config.Fifth);
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

                    //Stop the shard cleanly
                    region.Tell(new PersistentShardCoordinator.HandOff("1"));
                    ExpectMsg<PersistentShardCoordinator.ShardStopped>(s => s.Shard == "1", TimeSpan.FromSeconds(10), "ShardStopped not received");

                    var probe = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        shard.Tell(new Identify(1), probe.Ref);
                        probe.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(1) && i.Subject == null, TimeSpan.FromSeconds(1), "Shard was still around");
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    //Get the path to where the shard now resides
                    _persistentEntitiesRegion.Value.Tell(new Counter.Get(13));
                    ExpectMsg(0);

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

                    counter1.Tell(new Counter.Get(1));
                    ExpectMsg(1);
                }, _config.Third);
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
                }, _config.Fourth);
                EnterBarrier("after-12");
            });
        }

        public void PersistentClusterShards_should_permanently_stop_entities_which_passivate()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var x = _persistentRegion.Value;
                }, _config.Third, _config.Fourth, _config.Fifth);
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

                    counter13.Path.Parent.Should().Be(counter1.Path.Parent);

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
                        probe1.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(1) && i.Subject == null, TimeSpan.FromSeconds(1), "Entity 1 was still around");
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                    // stop shard cleanly
                    region.Tell(new PersistentShardCoordinator.HandOff("1"));
                    ExpectMsg<PersistentShardCoordinator.ShardStopped>(s => s.Shard == "1", TimeSpan.FromSeconds(10), "ShardStopped not received");

                    var probe2 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        shard.Tell(new Identify(2), probe2.Ref);
                        probe2.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(2) && i.Subject == null, TimeSpan.FromSeconds(1), "Shard was still around");
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));

                }, _config.Third);
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
                    var probe3 = CreateTestProbe();
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection(shard / "13").Tell(new Identify(4), probe3.Ref);
                        probe3.ExpectMsg<ActorIdentity>(i => i.MessageId.Equals(4) && i.Subject != null);
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
                }, _config.Fourth);
                EnterBarrier("after-13");
            });
        }

        public void PersistentClusterShards_should_restart_entities_which_stop_without_passivation()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    var x = _persistentRegion.Value;
                }, _config.Third, _config.Fourth);
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
                        probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(1)).Subject.Should().NotBeNull();
                    }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
                }, _config.Third);
                EnterBarrier("after-14");
            });
        }

        public void PersistentClusterShards_should_be_migrated_to_new_regions_upon_region_failure()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                //Start only one region, and force an entity onto that region
                RunOn(() =>
                {
                    _autoMigrateRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _autoMigrateRegion.Value.Tell(new Counter.Get(1));
                    ExpectMsg(1);
                }, _config.Third);
                EnterBarrier("shard1-region3");

                //Start another region and test it talks to node 3
                RunOn(() =>
                {
                    _autoMigrateRegion.Value.Tell(new Counter.EntityEnvelope(1, Counter.Increment.Instance));
                    _autoMigrateRegion.Value.Tell(new Counter.Get(1));
                    ExpectMsg(2);

                    LastSender.Path.Should().Be(Node(_config.Third) / "user" / "AutoMigrateRememberRegionTestRegion" / "1" / "1");

                    // kill region 3
                    Sys.ActorSelection(LastSender.Path.Parent.Parent).Tell(PoisonPill.Instance);
                }, _config.Fourth);
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

                    counter1.Tell(new Counter.Get(1));
                    ExpectMsg(2);
                }, _config.Fourth);
                EnterBarrier("after-15");
            });
        }

        public void PersistentClusterShards_should_ensure_rebalance_restarts_shards()
        {
            Within(TimeSpan.FromSeconds(50), () =>
            {
                RunOn(() =>
                {
                    for (int i = 2; i <= 12; i++)
                        _rebalancingPersistentRegion.Value.Tell(new Counter.EntityEnvelope(i, Counter.Increment.Instance));

                    for (int i = 2; i <= 12; i++)
                    {
                        _rebalancingPersistentRegion.Value.Tell(new Counter.Get(i));
                        ExpectMsg(1);
                    }
                }, _config.Fourth);
                EnterBarrier("entities-started");

                RunOn(() =>
                {
                    var r = _rebalancingPersistentRegion.Value;
                }, _config.Fifth);
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
                }, _config.Fifth);
                EnterBarrier("after-16");
            });
        }

        #endregion
    }
}
