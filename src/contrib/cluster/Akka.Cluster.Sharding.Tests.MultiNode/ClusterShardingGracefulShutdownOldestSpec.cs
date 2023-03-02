//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGracefulShutdownOldestSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGracefulShutdownOldestSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingGracefulShutdownOldestSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
            # don't leak ddata state across runs
            akka.cluster.sharding.distributed-data.durable.keys = []
            ")
        {
            First = Role("first");
            Second = Role("second");
        }
    }

    public class PersistentClusterShardingGracefulShutdownOldestSpecConfig : ClusterShardingGracefulShutdownOldestSpecConfig
    {
        public PersistentClusterShardingGracefulShutdownOldestSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingGracefulShutdownOldestSpecConfig : ClusterShardingGracefulShutdownOldestSpecConfig
    {
        public DDataClusterShardingGracefulShutdownOldestSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingGracefulShutdownOldestSpec : ClusterShardingGracefulShutdownOldestSpec
    {
        public PersistentClusterShardingGracefulShutdownOldestSpec()
            : base(new PersistentClusterShardingGracefulShutdownOldestSpecConfig(), typeof(PersistentClusterShardingGracefulShutdownOldestSpec))
        {
        }
    }

    public class DDataClusterShardingGracefulShutdownOldestSpec : ClusterShardingGracefulShutdownOldestSpec
    {
        public DDataClusterShardingGracefulShutdownOldestSpec()
            : base(new DDataClusterShardingGracefulShutdownOldestSpecConfig(), typeof(DDataClusterShardingGracefulShutdownOldestSpec))
        {
        }
    }

    public abstract class ClusterShardingGracefulShutdownOldestSpec : MultiNodeClusterShardingSpec<ClusterShardingGracefulShutdownOldestSpecConfig>
    {
        #region setup

        public class TerminationOrderActor : ActorBase
        {
            public class RegionTerminated
            {
                public static RegionTerminated Instance = new RegionTerminated();

                private RegionTerminated()
                {
                }
            }

            public class CoordinatorTerminated
            {
                public static CoordinatorTerminated Instance = new CoordinatorTerminated();

                private CoordinatorTerminated()
                {
                }
            }

            public static Props Props(IActorRef probe, IActorRef coordinator, IActorRef region)
            {
                return Actor.Props.Create(() => new TerminationOrderActor(probe, coordinator, region));
            }

            private readonly IActorRef _probe;
            private readonly IActorRef _coordinator;
            private readonly IActorRef _region;

            public TerminationOrderActor(IActorRef probe, IActorRef coordinator, IActorRef region)
            {
                _probe = probe;
                _coordinator = coordinator;
                _region = region;

                Context.Watch(coordinator);
                Context.Watch(region);
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Terminated t when t.ActorRef.Equals(_coordinator):
                        _probe.Tell(CoordinatorTerminated.Instance);
                        return true;

                    case Terminated t when t.ActorRef.Equals(_region):
                        _probe.Tell(RegionTerminated.Instance);
                        return true;
                }
                return false;
            }
        }

        // slow stop previously made it more likely that the coordinator would stop before the local region
        public class SlowStopShardedEntity : ActorBase, IWithTimers
        {
            public class Stop
            {
                public static Stop Instance = new Stop();

                private Stop()
                {
                }
            }

            public class ActualStop
            {
                public static ActualStop Instance = new ActualStop();

                private ActualStop()
                {
                }
            }

            public ITimerScheduler Timers { get; set; }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case int id:
                        Sender.Tell(id);
                        return true;
                    case Stop _:
                        Timers.StartSingleTimer(ActualStop.Instance, ActualStop.Instance, TimeSpan.FromMilliseconds(50));
                        return true;
                    case ActualStop _:
                        Context.Stop(Self);
                        return true;
                }
                return false;
            }
        }

        private const string TypeName = "Entity";
        private readonly Lazy<IActorRef> _region;

        protected ClusterShardingGracefulShutdownOldestSpec(ClusterShardingGracefulShutdownOldestSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion(TypeName));
        }

        private void Join(RoleName from, RoleName to, string typeName)
        {
            base.Join(from, to);
            RunOn(() =>
            {
                StartSharding(typeName);
            }, from);
            EnterBarrier($"{from}-started");
        }

        private IActorRef StartSharding(string typeName)
        {
            return StartSharding(
                Sys,
                typeName,
                entityProps: Props.Create(() => new SlowStopShardedEntity()),
                extractEntityId: IntExtractEntityId,
                extractShardId: IntExtractShardId,
                allocationStrategy: ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 2, relativeLimit: 1.0),
                handOffStopMessage: SlowStopShardedEntity.Stop.Instance);
        }

        #endregion

        [MultiNodeFact]
        public void ClusterShardingGracefulShutdownOldestSpecs()
        {
            Cluster_sharding_must_start_some_shards_in_both_regions();
            Cluster_sharding_must_gracefully_shutdown_the_oldest_region();
        }

        private void Cluster_sharding_must_start_some_shards_in_both_regions()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                StartPersistenceIfNeeded(startOn: config.First, config.First, config.Second);

                Join(config.First, config.First, TypeName);
                Join(config.Second, config.First, TypeName);

                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    var regionAddresses = Enumerable.Range(1, 100).Select(n =>
                    {
                        _region.Value.Tell(n, probe.Ref);
                        probe.ExpectMsg(n, TimeSpan.FromSeconds(1));
                        return probe.LastSender.Path.Address;
                    }).ToImmutableHashSet();

                    regionAddresses.Count.Should().Be(2);
                });
                EnterBarrier("after-2");
            });
        }

        private void Cluster_sharding_must_gracefully_shutdown_the_oldest_region()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    IActorRef coordinator = null;
                    AwaitAssert(() =>
                    {
                        coordinator = Sys
                          .ActorSelection($"/system/sharding/{TypeName}Coordinator/singleton/coordinator")
                          .ResolveOne(RemainingOrDefault).Result;
                    });
                    var terminationProbe = CreateTestProbe();
                    Sys.ActorOf(TerminationOrderActor.Props(terminationProbe.Ref, coordinator, _region.Value));

                    // trigger graceful shutdown
                    Cluster.Leave(GetAddress(config.First));

                    // region first
                    terminationProbe.ExpectMsg<TerminationOrderActor.RegionTerminated>();
                    terminationProbe.ExpectMsg<TerminationOrderActor.CoordinatorTerminated>();
                }, config.First);

                EnterBarrier("terminated");

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        var p = CreateTestProbe();


                        var responses = Enumerable.Range(1, 100).Select(n =>
                        {
                            _region.Value.Tell(n, p.Ref);
                            return p.ExpectMsg(n, TimeSpan.FromSeconds(1));
                        }).ToImmutableHashSet();

                        responses.Count.Should().Be(100);
                    });
                }, config.Second);
                EnterBarrier("done-o");
            });
        }
    }
}
