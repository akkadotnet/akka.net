// -----------------------------------------------------------------------
//  <copyright file="ClusterShardingDeliveryGracefulShutdownSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Sharding.Delivery;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests.MultiNode.Delivery;

public class ClusterShardingDeliveryGracefulShutdownSpecConfig : MultiNodeClusterShardingConfig
{
    public ClusterShardingDeliveryGracefulShutdownSpecConfig(StateStoreMode mode)
        : base(mode, loglevel: "DEBUG", additionalConfig: @"
# don't leak ddata state across runs
akka.cluster.sharding.distributed-data.durable.keys = []
akka.reliable-delivery.sharding.consumer-controller.allow-bypass = true
")
    {
        First = Role("first");
        Second = Role("second");
    }

    public RoleName First { get; }
    public RoleName Second { get; }
}

public class
    PersistentClusterShardingDeliveryGracefulShutdownSpecConfig : ClusterShardingDeliveryGracefulShutdownSpecConfig
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
        : base(new PersistentClusterShardingDeliveryGracefulShutdownSpecConfig(),
            typeof(PersistentClusterShardingDeliveryGracefulShutdownSpec))
    {
    }
}

public class DDataClusterShardingDeliveryGracefulShutdownSpec : ClusterShardingDeliveryGracefulShutdownSpec
{
    public DDataClusterShardingDeliveryGracefulShutdownSpec()
        : base(new DDataClusterShardingDeliveryGracefulShutdownSpecConfig(),
            typeof(DDataClusterShardingDeliveryGracefulShutdownSpec))
    {
    }
}

public abstract class
    ClusterShardingDeliveryGracefulShutdownSpec : MultiNodeClusterShardingSpec<
    ClusterShardingDeliveryGracefulShutdownSpecConfig>
{
    [MultiNodeFact]
    public void ClusterShardingDeliveryGracefulShutdownSpecs()
    {
        Cluster_sharding_must_join_cluster();
        Cluster_sharding_must_start_some_shards_in_both_regions();
        Cluster_sharding_must_gracefully_shutdown_the_oldest_region();
    }

    private void Cluster_sharding_must_join_cluster()
    {
        StartPersistenceIfNeeded(Config.First, Config.First, Config.Second);

        Join(Config.First, Config.First);
        Join(Config.Second, Config.First);

        // make sure all nodes are up
        AwaitAssert(() =>
        {
            Cluster.Get(Sys).SendCurrentClusterState(TestActor);
            ExpectMsg<ClusterEvent.CurrentClusterState>().Members.Count.Should().Be(2);
        });

        RunOn(() => { StartSharding(); }, Config.First);

        RunOn(() => { StartSharding(); }, Config.Second);

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
                    foreach (var n in Enumerable.Range(1, maxCount)) producer.Tell(n, TestActor);
                    var responses = ReceiveN(maxCount, TimeSpan.FromSeconds(2));
                    responses.Count.Should().Be(20);
                });
            }, Config.Second);
            EnterBarrier("done-o");
        });
    }

    #region setup

    public class TerminationOrderActor : ActorBase
    {
        private readonly IActorRef _coordinator;

        private readonly IActorRef _probe;
        private readonly IActorRef _region;

        public TerminationOrderActor(IActorRef probe, IActorRef coordinator, IActorRef region)
        {
            _probe = probe;
            _coordinator = coordinator;
            _region = region;

            Context.Watch(coordinator);
            Context.Watch(region);
        }

        public static Props Props(IActorRef probe, IActorRef coordinator, IActorRef region)
        {
            return Actor.Props.Create(() => new TerminationOrderActor(probe, coordinator, region));
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
    }

    private sealed class MessageExtractor : IMessageExtractor
    {
        public string EntityId(object message)
        {
            return message switch
            {
                SlowStopConsumerEntity.Job j => j.Payload.ToString(),
                _ => null
            };
        }

        public object EntityMessage(object message)
        {
            return message;
        }

        public string ShardId(object message)
        {
            return message switch
            {
                SlowStopConsumerEntity.Job j => j.Payload.ToString(),
                _ => null
            };
        }

        public string ShardId(string entityId, object messageHint = null)
        {
            return entityId;
        }
    }

    private const string TypeName = "SlowStopEntity";
    private IActorRef _producer;
    private IActorRef _producerController;

    protected ClusterShardingDeliveryGracefulShutdownSpec(ClusterShardingDeliveryGracefulShutdownSpecConfig config,
        Type type)
        : base(config, type)
    {
    }

    private IActorRef CreateProducer(string producerId)
    {
        _producerController =
            Sys.ActorOf(
                ShardingProducerController.Create<SlowStopConsumerEntity.Job>(
                    producerId,
                    ClusterSharding.Get(Sys).ShardRegion(TypeName),
                    Option<Props>.None,
                    ShardingProducerController.Settings.Create(Sys)),
                "shardingProducerController");
        _producer = Sys.ActorOf(Props.Create(() => new TestShardingProducer(_producerController, TestActor)),
            "producer");
        return _producer;
    }

    private IActorRef StartSharding()
    {
        return ClusterSharding.Get(Sys).Start(
            TypeName,
            e => ShardingConsumerController.Create<SlowStopConsumerEntity.Job>(
                c => Props.Create(() => new SlowStopConsumerEntity(e, c)),
                ShardingConsumerController.Settings.Create(Sys)),
            Settings.Value.WithRole(null),
            new MessageExtractor(),
            ShardAllocationStrategy.LeastShardAllocationStrategy(2, 1.0),
            SlowStopConsumerEntity.Stop.Instance);
    }

    #endregion
}