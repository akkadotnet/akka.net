//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Sharding.Serialization;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;

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

        private static readonly Config SpecConfig;

        static ClusterShardingMessageSerializerSpec()
        {
            SpecConfig = ClusterSingletonManager.DefaultConfig().WithFallback(ClusterSharding.DefaultConfig());
        }

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

            var state = new PersistentShardCoordinator.State(
                shards: shards,
                regions: regions,
                regionProxies: ImmutableHashSet.Create(regionProxy1, regionProxy2),
                unallocatedShards: ImmutableHashSet.Create("d"));

            CheckSerialization(state);
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardCoordinator_domain_events()
        {
            CheckSerialization(new PersistentShardCoordinator.ShardRegionRegistered(region1));
            CheckSerialization(new PersistentShardCoordinator.ShardRegionProxyRegistered(regionProxy1));
            CheckSerialization(new PersistentShardCoordinator.ShardRegionTerminated(region1));
            CheckSerialization(new PersistentShardCoordinator.ShardRegionProxyTerminated(regionProxy1));
            CheckSerialization(new PersistentShardCoordinator.ShardHomeAllocated("a", region1));
            CheckSerialization(new PersistentShardCoordinator.ShardHomeDeallocated("a"));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardCoordinator_remote_messages()
        {
            CheckSerialization(new PersistentShardCoordinator.Register(region1));
            CheckSerialization(new PersistentShardCoordinator.RegisterProxy(regionProxy1));
            CheckSerialization(new PersistentShardCoordinator.RegisterAck(region1));
            CheckSerialization(new PersistentShardCoordinator.GetShardHome("a"));
            CheckSerialization(new PersistentShardCoordinator.ShardHome("a", region1));
            CheckSerialization(new PersistentShardCoordinator.HostShard("a"));
            CheckSerialization(new PersistentShardCoordinator.ShardStarted("a"));
            CheckSerialization(new PersistentShardCoordinator.BeginHandOff("a"));
            CheckSerialization(new PersistentShardCoordinator.BeginHandOffAck("a"));
            CheckSerialization(new PersistentShardCoordinator.HandOff("a"));
            CheckSerialization(new PersistentShardCoordinator.ShardStopped("a"));
            CheckSerialization(new PersistentShardCoordinator.GracefulShutdownRequest(region1));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_PersistentShard_snapshot_state()
        {
            CheckSerialization(new Shard.ShardState(ImmutableHashSet.Create("e1", "e2", "e3")));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_PersistentShard_domain_events()
        {
            CheckSerialization(new Shard.EntityStarted("e1"));
            CheckSerialization(new Shard.EntityStopped("e1"));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_GetShardStats()
        {
            CheckSerialization(Shard.GetShardStats.Instance);
            CheckSerialization(GetShardRegionStats.Instance);
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serializable_ShardStats()
        {
            CheckSerialization(new Shard.ShardStats("a", 23));
            CheckSerialization(new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("f", 12)));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_be_able_to_serialize_StartEntity()
        {
            CheckSerialization(new ShardRegion.StartEntity("42"));
            CheckSerialization(new ShardRegion.StartEntityAck("13", "37"));
        }

        [Fact]
        public void ClusterShardingMessageSerializer_must_serialize_ClusterShardingStats()
        {
            CheckSerialization(new GetClusterShardingStats(TimeSpan.FromMilliseconds(500)));
            CheckSerialization(new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty.Add(new Address("akka.tcp", "foo", "localhost", 9110), 
                new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("f", 12)))));
        }
    }
}
