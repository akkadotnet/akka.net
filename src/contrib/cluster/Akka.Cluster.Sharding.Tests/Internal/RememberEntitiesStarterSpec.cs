//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesStarterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests.Internal
{
    /// <summary>
    /// Covers the interaction between the shard and the remember entities store
    /// </summary>
    public class RememberEntitiesStarterSpec : AkkaSpec
    {
        private static int shardIdCounter = 1;

        private string NextShardId()
        {
            var id = $"ShardId{shardIdCounter}";
            shardIdCounter++;
            return id;
        }

        public RememberEntitiesStarterSpec(ITestOutputHelper helper) : base(SpecConfig, output: helper)
        {
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString("akka.loglevel = DEBUG")
            .WithFallback(
                ClusterSingletonManager.DefaultConfig())
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig());

        [Fact]
        public void RememberEntitiesStarter_must_try_start_all_entities_directly_with_entity_recovery_strategy_all_default()
        {
            var regionProbe = CreateTestProbe();
            var shardProbe = CreateTestProbe();
            var shardId = NextShardId();

            var defaultSettings = ClusterShardingSettings.Create(Sys);

            var rememberEntityStarter = Sys.ActorOf(
                RememberEntityStarter.Props(regionProbe.Ref, shardProbe.Ref, shardId, ImmutableHashSet.Create("1", "2", "3"), defaultSettings));

            Watch(rememberEntityStarter);
            var startedEntityIds = Enumerable.Range(1, 3).Select(i =>
            {
                var start = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
                regionProbe.LastSender.Tell(new ShardRegion.StartEntityAck(start.EntityId, shardId));
                return start.EntityId;
            }).ToImmutableHashSet();
            startedEntityIds.Should().BeEquivalentTo("1", "2", "3");

            // the starter should then stop itself, not sending anything more to the shard or region
            ExpectTerminated(rememberEntityStarter);
            shardProbe.ExpectNoMsg();
            regionProbe.ExpectNoMsg();
        }

        [Fact]
        public void RememberEntitiesStarter_must_retry_start_all_entities_with_no_ack_with_entity_recovery_strategy_all_default()
        {
            var regionProbe = CreateTestProbe();
            var shardProbe = CreateTestProbe();
            var shardId = NextShardId();

            var customSettings = ClusterShardingSettings.Create(
                ConfigurationFactory.ParseString(
                    // the restarter somewhat surprisingly uses this for no-ack-retry. Tune it down to speed up test
                    @"
                    retry-interval = 1s
                    ")
                    .WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding")), Sys.Settings.Config.GetConfig("akka.cluster.singleton"));

            var rememberEntityStarter = Sys.ActorOf(
                RememberEntityStarter.Props(regionProbe.Ref, shardProbe.Ref, shardId, ImmutableHashSet.Create("1", "2", "3"), customSettings));

            Watch(rememberEntityStarter);
            for (int i = 1; i <= 3; i++)
            {
                var start = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
            }
            var startedOnSecondTry = Enumerable.Range(1, 3).Select(i =>
             {
                 var start = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
                 regionProbe.LastSender.Tell(new ShardRegion.StartEntityAck(start.EntityId, shardId));
                 return start.EntityId;
             }).ToImmutableHashSet();
            startedOnSecondTry.Should().BeEquivalentTo("1", "2", "3");

            // should stop itself, not sending anything to the shard
            ExpectTerminated(rememberEntityStarter);
            shardProbe.ExpectNoMsg();
        }

        [Fact]
        public void RememberEntitiesStarter_must_inform_the_shard_when_entities_has_been_reallocated_to_different_shard_id()
        {
            var regionProbe = CreateTestProbe();
            var shardProbe = CreateTestProbe();
            var shardId = NextShardId();

            var customSettings = ClusterShardingSettings.Create(
                ConfigurationFactory.ParseString(
                    // the restarter somewhat surprisingly uses this for no-ack-retry. Tune it down to speed up test
                    @"
                    retry-interval = 1s
                    ")
                    .WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding")), Sys.Settings.Config.GetConfig("akka.cluster.singleton"));

            var rememberEntityStarter = Sys.ActorOf(
                RememberEntityStarter.Props(regionProbe.Ref, shardProbe.Ref, shardId, ImmutableHashSet.Create("1", "2", "3"), customSettings));

            Watch(rememberEntityStarter);
            var start1 = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
            regionProbe.LastSender.Tell(new ShardRegion.StartEntityAck(start1.EntityId, start1.EntityId == "1"? shardId : $"Relocated{start1.EntityId}")); // keep 1 on current shard

            var start2 = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
            regionProbe.LastSender.Tell(new ShardRegion.StartEntityAck(start2.EntityId, start2.EntityId == "1" ? shardId : $"Relocated{start2.EntityId}"));

            var start3 = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
            regionProbe.LastSender.Tell(new ShardRegion.StartEntityAck(start3.EntityId, start3.EntityId == "1" ? shardId : $"Relocated{start3.EntityId}"));

            shardProbe.ExpectMsg(new Shard.EntitiesMovedToOtherShard(ImmutableHashSet.Create("2", "3")));
            ExpectTerminated(rememberEntityStarter);
        }

        [Fact]
        public void RememberEntitiesStarter_must_try_start_all_entities_in_a_throttled_way_with_entity_recovery_strategy_constant()
        {
            var regionProbe = CreateTestProbe();
            var shardProbe = CreateTestProbe();
            var shardId = NextShardId();

            var customSettings = ClusterShardingSettings.Create(
                ConfigurationFactory.ParseString(
                    // slow constant restart
                    @"
                    entity-recovery-strategy = constant
                    entity-recovery-constant-rate-strategy {
                        frequency = 2 s
                        number-of-entities = 2
                    }
                    retry-interval = 1s
                    ")
                    .WithFallback(Sys.Settings.Config.GetConfig("akka.cluster.sharding")), Sys.Settings.Config.GetConfig("akka.cluster.singleton"));

            var rememberEntityStarter = Sys.ActorOf(
                    RememberEntityStarter
                      .Props(regionProbe.Ref, shardProbe.Ref, shardId, ImmutableHashSet.Create("1", "2", "3", "4", "5"), customSettings));

            void RecieveStartAndAck()
            {
                var start = regionProbe.ExpectMsg<ShardRegion.StartEntity>();
                regionProbe.LastSender.Tell(new ShardRegion.StartEntityAck(start.EntityId, shardId));
            }

            Watch(rememberEntityStarter);
            // first batch should be immediate
            RecieveStartAndAck();
            RecieveStartAndAck();
            // second batch holding off (with some room for unstable test env)
            regionProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(600));

            // second batch should be immediate
            RecieveStartAndAck();
            RecieveStartAndAck();
            // third batch holding off
            regionProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(600));

            RecieveStartAndAck();

            // the starter should then stop itself, not sending anything more to the shard or region
            ExpectTerminated(rememberEntityStarter);
            shardProbe.ExpectNoMsg();
            regionProbe.ExpectNoMsg();
        }
    }
}
