//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Sharding.Serialization;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingMessageSerializerSpec : AkkaSpec
    {
        private SerializerWithStringManifest serializer;
        private IActorRef region1;
        private IActorRef region2;
        private IActorRef region3;
        private IActorRef regionProxy1;
        private IActorRef regionProxy2;

        private static Config SpecConfig =>
            ClusterSingletonManager.DefaultConfig().WithFallback(ClusterSharding.DefaultConfig());

        public ClusterShardingMessageSerializerSpec() : base(SpecConfig)
        {
            serializer = new ClusterShardingMessageSerializer((ExtendedActorSystem)Sys);
            region1 = Sys.ActorOf(Props.Empty, "region1");
            region2 = Sys.ActorOf(Props.Empty, "region2");
            region3 = Sys.ActorOf(Props.Empty, "region3");
            regionProxy1 = Sys.ActorOf(Props.Empty, "regionProxy1");
            regionProxy2 = Sys.ActorOf(Props.Empty, "regionProxy2");
        }

        private void CheckSerialization(object obj)
        {
            var blob = serializer.ToBinary(obj);
            var reference = serializer.FromBinary(blob, serializer.Manifest(obj));
            reference.Should().Be(obj);
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardCoordinator_snapshot_State()
        {
            var shards = ImmutableDictionary
                .CreateBuilder<string, IActorRef>()
                .AddAndReturn("a", region1)
                .AddAndReturn("b", region2)
                .AddAndReturn("c", region2)
                .ToImmutableDictionary();

            var regions = ImmutableDictionary
                .CreateBuilder<IActorRef, IImmutableList<string>>()
                .AddAndReturn(region1, ImmutableArray.Create("a"))
                .AddAndReturn(region2, ImmutableArray.Create("b", "c"))
                .AddAndReturn(region3, ImmutableArray<string>.Empty)
                .ToImmutableDictionary();

            var state = new ShardCoordinator.CoordinatorState(
                shards: shards,
                regions: regions,
                regionProxies: ImmutableHashSet.Create(regionProxy1, regionProxy2),
                unallocatedShards: ImmutableHashSet.Create("d"));

            CheckSerialization(state);
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardCoordinator_domain_events()
        {
            CheckSerialization(new ShardCoordinator.ShardRegionRegistered(region1));
            CheckSerialization(new ShardCoordinator.ShardRegionProxyRegistered(regionProxy1));
            CheckSerialization(new ShardCoordinator.ShardRegionTerminated(region1));
            CheckSerialization(new ShardCoordinator.ShardRegionProxyTerminated(regionProxy1));
            CheckSerialization(new ShardCoordinator.ShardHomeAllocated("a", region1));
            CheckSerialization(new ShardCoordinator.ShardHomeDeallocated("a"));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardCoordinator_remote_messages()
        {
            CheckSerialization(new ShardCoordinator.Register(region1));
            CheckSerialization(new ShardCoordinator.RegisterProxy(regionProxy1));
            CheckSerialization(new ShardCoordinator.RegisterAck(region1));
            CheckSerialization(new ShardCoordinator.GetShardHome("a"));
            CheckSerialization(new ShardCoordinator.ShardHome("a", region1));
            CheckSerialization(new ShardCoordinator.HostShard("a"));
            CheckSerialization(new ShardCoordinator.ShardStarted("a"));
            CheckSerialization(new ShardCoordinator.BeginHandOff("a"));
            CheckSerialization(new ShardCoordinator.BeginHandOffAck("a"));
            CheckSerialization(new ShardCoordinator.HandOff("a"));
            CheckSerialization(new ShardCoordinator.ShardStopped("a"));
            CheckSerialization(new ShardCoordinator.GracefulShutdownRequest(region1));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_PersistentShard_snapshot_state()
        {
            CheckSerialization(new EventSourcedRememberEntitiesShardStore.State(ImmutableHashSet.Create("e1", "e2", "e3")));
        }


        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_PersistentShard_domain_events()
        {
            CheckSerialization(new EventSourcedRememberEntitiesShardStore.EntitiesStarted(ImmutableHashSet.Create("e1", "e2")));
            CheckSerialization(new EventSourcedRememberEntitiesShardStore.EntitiesStopped(ImmutableHashSet.Create("e1", "e2")));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_deserialize_old_entity_started_event_into_entities_started()
        {
            var message1 = new Serialization.Proto.Msg.EntityStarted();
            message1.EntityId = "e1";
            var blob = message1.ToByteArray();

            var reference = serializer.FromBinary(blob, "CB");
            reference.Should().Be(new EventSourcedRememberEntitiesShardStore.EntitiesStarted(ImmutableHashSet.Create("e1")));


            var message2 = new Serialization.Proto.Msg.EntityStopped();
            message1.EntityId = "e1";
            blob = message1.ToByteArray();

            reference = serializer.FromBinary(blob, "CD");
            reference.Should().Be(new EventSourcedRememberEntitiesShardStore.EntitiesStopped(ImmutableHashSet.Create("e1")));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_GetShardStats()
        {
            CheckSerialization(Shard.GetShardStats.Instance);
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardStats()
        {
            CheckSerialization(new Shard.ShardStats("a", 23));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_GetShardRegionStats()
        {
            CheckSerialization(GetShardRegionStats.Instance);
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardRegionStats()
        {
            CheckSerialization(new ShardRegionStats(ImmutableDictionary<string, int>.Empty, ImmutableHashSet<string>.Empty));
            CheckSerialization(new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("a", 23), ImmutableHashSet.Create("b")));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serialize_StartEntity()
        {
            CheckSerialization(new ShardRegion.StartEntity("42"));
            CheckSerialization(new ShardRegion.StartEntityAck("13", "37"));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serialize_GetCurrentRegions()
        {
            CheckSerialization(GetCurrentRegions.Instance);
            CheckSerialization(new CurrentRegions(ImmutableHashSet.Create(new Address("akka", "sys", "a", 2552), new Address("akka", "sys", "b", 2552))));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_serialize_ClusterShardingStats()
        {
            CheckSerialization(new GetClusterShardingStats(TimeSpan.FromSeconds(3)));

            CheckSerialization(new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty
                .Add(new Address("akka.tcp", "sys", "a", 2552), new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("a", 23), ImmutableHashSet.Create("b")))
                .Add(new Address("akka.tcp", "sys", "b", 2552), new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("a", 23), ImmutableHashSet.Create("b")))
                ));
        }
    }
}


