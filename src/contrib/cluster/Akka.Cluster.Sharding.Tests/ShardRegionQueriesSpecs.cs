//-----------------------------------------------------------------------
// <copyright file="ShardRegionQueriesSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardRegionQueriesSpecs : AkkaSpec
    {
        private Cluster _cluster;
        private ClusterSharding _clusterSharding;
        private IActorRef _shardRegion;

        private ActorSystem _proxySys;

        public ShardRegionQueriesSpecs(ITestOutputHelper outputHelper) : base(GetConfig(), outputHelper)
        {
            _clusterSharding = ClusterSharding.Get(Sys);
            _cluster = Cluster.Get(Sys);
            _shardRegion = _clusterSharding.Start("entity", s => EchoActor.Props(this, true),
                ClusterShardingSettings.Create(Sys).WithRole("shard"), ExtractEntityId, ExtractShardId);

            var proxySysConfig = ConfigurationFactory.ParseString("akka.cluster.roles = [proxy]")
                .WithFallback(Sys.Settings.Config);
            _proxySys = ActorSystem.Create(Sys.Name, proxySysConfig);
            
            _cluster.Join(_cluster.SelfAddress);
            AwaitAssert(() =>
            {
                _cluster.SelfMember.Status.ShouldBe(MemberStatus.Up);
            });

            // form a 2-node cluster
            var proxyCluster = Cluster.Get(_proxySys);
            proxyCluster.Join(_cluster.SelfAddress);
            AwaitAssert(() =>
            {
                proxyCluster.SelfMember.Status.ShouldBe(MemberStatus.Up);
            });
        }

        protected override void AfterAll()
        {
            Shutdown(_proxySys);
            base.AfterAll();
        }

        private Option<(string, object)> ExtractEntityId(object message)
        {
            switch (message)
            {
                case int i:
                    return (i.ToString(), message);
            }
            throw new NotSupportedException();
        }

        private string ExtractShardId(object message)
        {
            switch (message)
            {
                case int i:
                    return (i % 10).ToString();
                case ShardRegion.StartEntity se:
                    return se.EntityId;
            }
            throw new NotSupportedException();
        }

        private static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
                                                     akka.loglevel = WARNING
                                                     akka.actor.provider = cluster
                                                     akka.remote.dot-netty.tcp.port = 0
                                                     akka.cluster.roles = [shard]")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());
        }

        [Fact(DisplayName = "ShardRegion should support GetEntityLocation queries locally")]
        public async Task ShardRegion_should_support_GetEntityLocation_query()
        {
            // arrange
            await _shardRegion.Ask<int>(1, TimeSpan.FromSeconds(3));
            await _shardRegion.Ask<int>(2, TimeSpan.FromSeconds(3));

            // act
            var q1 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("1", TimeSpan.FromSeconds(1)));
            var q2 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("2", TimeSpan.FromSeconds(1)));
            var q3 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("3", TimeSpan.FromSeconds(1)));

            // assert
            void AssertValidEntityLocation(EntityLocation e, string entityId)
            {
                e.EntityId.Should().Be(entityId);
                e.EntityRef.Should().NotBe(Option<IActorRef>.None);
                e.ShardId.Should().NotBeNullOrEmpty();
                e.ShardRegion.Should().Be(_cluster.SelfAddress);
            }
            
            AssertValidEntityLocation(q1, "1");
            AssertValidEntityLocation(q2, "2");

            q3.EntityRef.Should().Be(Option<IActorRef>.None);
            q3.ShardId.Should().NotBeNullOrEmpty(); // should still have computed a valid shard?
            q3.ShardRegion.Should().Be(Address.AllSystems);
        }

    }
}