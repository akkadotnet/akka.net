//-----------------------------------------------------------------------
// <copyright file="ClusterShardingDeliveryGracefulShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Sharding.Delivery;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests.MultiNode.Delivery
{
    public class ClusterShardingDeliveryGracefulShutdownSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingDeliveryGracefulShutdownSpecConfig(StateStoreMode mode)
            : base(mode: mode, loglevel: "DEBUG", additionalConfig: @"
# don't leak ddata state across runs
akka.cluster.sharding.distributed-data.durable.keys = []
akka.reliable-delivery.sharding.consumer-controller.allow-bypass = true
")
        {
            First = Role("first");
            Second = Role("second");
        }
    }

    public class PersistentClusterShardingDeliveryGracefulShutdownSpecConfig : ClusterShardingDeliveryGracefulShutdownSpecConfig
    {
        public PersistentClusterShardingDeliveryGracefulShutdownSpecConfig()
            : base(StateStoreMode.Persistence)
        {
        }
    }

    public class DDataClusterShardingDeliveryGracefulShutdownSpecConfig : ClusterShardingDeliveryGracefulShutdownSpecConfig
    {
        public DDataClusterShardingDeliveryGracefulShutdownSpecConfig()
            : base(StateStoreMode.DData)
        {
        }
    }

    public class PersistentClusterShardingDeliveryGracefulShutdownSpec : ClusterShardingDeliveryGracefulShutdownSpec
    {
        public PersistentClusterShardingDeliveryGracefulShutdownSpec()
            : base(new PersistentClusterShardingDeliveryGracefulShutdownSpecConfig(), typeof(PersistentClusterShardingDeliveryGracefulShutdownSpec))
        {
        }
    }

    public class DDataClusterShardingDeliveryGracefulShutdownSpec : ClusterShardingDeliveryGracefulShutdownSpec
    {
        public DDataClusterShardingDeliveryGracefulShutdownSpec()
            : base(new DDataClusterShardingDeliveryGracefulShutdownSpecConfig(), typeof(DDataClusterShardingDeliveryGracefulShutdownSpec))
        {
        }
    }

    public abstract class ClusterShardingDeliveryGracefulShutdownSpec : MultiNodeClusterShardingSpec<ClusterShardingDeliveryGracefulShutdownSpecConfig>
    {
        #region setup

        public class TerminationOrderActor : ActorBase
        {
            public class RegionTerminated
            {
                public static RegionTerminated Instance = new();

                private RegionTerminated()
                {
                }
            }

            public class CoordinatorTerminated
            {
                public static CoordinatorTerminated Instance = new();

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

        private sealed class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    SlowStopConsumerEntity.Job j => j.Payload.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string ShardId(object message)
                => message switch
                {
                    SlowStopConsumerEntity.Job j => j.Payload.ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => entityId;
        }

        private const string TypeName = "SlowStopEntity";
        private IActorRef _producer;
        private IActorRef _producerController;

        protected ClusterShardingDeliveryGracefulShutdownSpec(ClusterShardingDeliveryGracefulShutdownSpecConfig config, Type type)
            : base(config, type)
        {
        }

        private IActorRef CreateProducer(string producerId)
        {
            _producerController =
                Sys.ActorOf(
                    ShardingProducerController.Create<SlowStopConsumerEntity.Job>(
                        producerId: producerId, 
                        shardRegion: ClusterSharding.Get(Sys).ShardRegion(TypeName), 
                        durableQueue: Option<Props>.None,
                        settings: ShardingProducerController.Settings.Create(Sys)), 
                    "shardingProducerController");
            _producer = Sys.ActorOf(Props.Create(() => new TestShardingProducer(_producerController, TestActor)),
                "producer");
            return _producer;
        }
        
        private IActorRef StartSharding()
        {
            return ClusterSharding.Get(Sys).Start(
                typeName: TypeName, 
                entityPropsFactory: e => ShardingConsumerController.Create<SlowStopConsumerEntity.Job>(
                    c => Props.Create(() => new SlowStopConsumerEntity(e, c)),
                    ShardingConsumerController.Settings.Create(Sys)), 
                settings: Settings.Value.WithRole(null),
                messageExtractor: new MessageExtractor(),
                allocationStrategy: ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 2, relativeLimit: 1.0),
                handOffStopMessage: SlowStopConsumerEntity.Stop.Instance);
        }
        
        #endregion

        [MultiNodeFact]
        public void ClusterShardingDeliveryGracefulShutdownSpecs()
        {
            Cluster_sharding_must_join_cluster();
            Cluster_sharding_must_start_some_shards_in_both_regions();
            Cluster_sharding_must_gracefully_shutdown_the_oldest_region();
        }

        private void Cluster_sharding_must_join_cluster()
        {
            StartPersistenceIfNeeded(startOn: Config.First, Config.First, Config.Second);

            Join(Config.First, Config.First);
            Join(Config.Second, Config.First);
            
            // make sure all nodes are up
            AwaitAssert(() =>
            {
                Cluster.Get(Sys).SendCurrentClusterState(TestActor);
                ExpectMsg<ClusterEvent.CurrentClusterState>().Members.Count.Should().Be(2);
            });

            RunOn(() =>
            {
                StartSharding();
            }, Config.First);
            
            RunOn(() =>
            {
                StartSharding();
            }, Config.Second);
            
            EnterBarrier("sharding started");
        }
        
        private void Cluster_sharding_must_start_some_shards_in_both_regions()
        {
            RunOn(() =>
            {
                var producer = CreateProducer("p-1");
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    var regionAddresses = Enumerable.Range(1, 20).Select(n =>
                    {
                        producer.Tell(n, TestActor);
                        ExpectMsg(n, TimeSpan.FromSeconds(1));
                        return LastSender.Path.Address;
                    }).ToImmutableHashSet();

                    regionAddresses.Count.Should().Be(2);
                });
            }, Config.First);
            
            EnterBarrier("after-2");
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
                    var region = ClusterSharding.Get(Sys).ShardRegion(TypeName);
                    Sys.ActorOf(TerminationOrderActor.Props(terminationProbe.Ref, coordinator, region));

                    // trigger graceful shutdown
                    Cluster.Leave(GetAddress(Config.First));

                    // region first
                    terminationProbe.ExpectMsg<TerminationOrderActor.RegionTerminated>();
                    terminationProbe.ExpectMsg<TerminationOrderActor.CoordinatorTerminated>();
                }, Config.First);

                EnterBarrier("terminated");

                RunOn(() =>
                {
                    var producer = CreateProducer("p-2");
                    AwaitAssert(() =>
                    {
                        var maxCount = 20;
                        foreach(var n in Enumerable.Range(1, maxCount))
                        {
                            producer.Tell(n, TestActor);
                        }
                        var responses = ReceiveN(maxCount, TimeSpan.FromSeconds(2));
                        responses.Count.Should().Be(20);
                    });
                }, Config.Second);
                EnterBarrier("done-o");
            });
        }
    }
}
