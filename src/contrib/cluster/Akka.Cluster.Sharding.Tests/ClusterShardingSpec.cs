//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Remote.TestKit;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.MultiNodeTests;
using Akka.Pattern;
using Akka.Persistence.Journal;
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

    public class ClusterShardingSpec : MultiNodeClusterSpec
    {
        #region Setup

        private readonly DirectoryInfo[] _storageLocations;
        private RoleName _controller;
        private RoleName _second;
        private RoleName _first;
        private RoleName _third;
        private RoleName _fourth;
        private RoleName _sixth;
        private RoleName _fifth;

        private readonly Lazy<IActorRef> _region;
        private readonly Lazy<IActorRef> _rebalancingRegion;
        private readonly Lazy<IActorRef> _persistentEntitiesRegion;
        private readonly Lazy<IActorRef> _anotherPersistentRegion;
        private readonly Lazy<IActorRef> _persistentRegion;
        private readonly Lazy<IActorRef> _rebalancingPersistentRegion;
        private readonly Lazy<IActorRef> _autoMigrateRegion;

        public ClusterShardingSpec() : base(new ClusterShardingSpecConfig())
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
            var settings = ClusterShardingSettings.Create(config);

            return ShardCoordinator.Props(typeName, settings, allocationStrategy);
        }

        private IActorRef CreateRegion(string typeName, bool rememberEntities)
        {
            var config = ConfigurationFactory.ParseString(@"
              retry-interval = 1s
              shard-failure-backoff = 1s
              entity-restart-backoff = 1s
              buffer-size = 1000").WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding"));
            var settings = ClusterShardingSettings.Create(config).WithRememberEntities(rememberEntities);

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

        [MultiNodeFact]
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

        [MultiNodeFact]
        public void ClusterSharding_should_work_in_single_node_cluster()
        {
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

        [MultiNodeFact]
        public void ClusterSharding_should_use_second_node()
        {
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

        [MultiNodeFact]
        public void ClusterSharding_should_support_passivation_and_activation_of_entities()
        {
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
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_failover_shards_on_crashed_node()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_use_third_and_fourth_node()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_recover_coordinator_state_after_coordinator_crash()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_rebalance_to_nodes_with_less_shards()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_be_easy_to_use_with_extensions()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterSharding_should_be_easy_API_for_starting()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_recover_entities_upon_restart()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_permanently_stop_entities_which_passivate()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_restart_entities_which_stop_without_passivation()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_be_migrated_to_new_regions_upon_region_failure()
        {
            throw new NotImplementedException();
        }

        [MultiNodeFact(Skip = "TODO")]
        public void Persistent_cluster_shards_should_ensure_rebalance_restarts_shards()
        {
            throw new NotImplementedException();
        }
    }
}