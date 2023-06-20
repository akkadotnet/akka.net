//-----------------------------------------------------------------------
// <copyright file="ShardRegionQueriesHashCodeSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public class ShardRegionQueriesHashCodeSpecs : AkkaSpec
    {
        private class MessageExtractor: HashCodeMessageExtractor
        {
            public MessageExtractor() : base(10)
            { }

            public override string EntityId(object message)
            {
                return message switch
                {
                    int i => i.ToString(),
                    _ => throw new NotSupportedException()
                };
            }
        }
        
        private readonly Cluster _cluster;
        private readonly ClusterSharding _clusterSharding;
        private readonly IActorRef _shardRegion;

        private readonly ActorSystem _proxySys;

        public ShardRegionQueriesHashCodeSpecs(ITestOutputHelper outputHelper) : base(GetConfig(), outputHelper)
        {
            _clusterSharding = ClusterSharding.Get(Sys);
            _cluster = Cluster.Get(Sys);
            _shardRegion = _clusterSharding.Start("entity", _ => EchoActor.Props(this, true),
                ClusterShardingSettings.Create(Sys).WithRole("shard"), new MessageExtractor());

            var proxySysConfig = ConfigurationFactory.ParseString("akka.cluster.roles = [proxy]")
                .WithFallback(Sys.Settings.Config);
            _proxySys = ActorSystem.Create(Sys.Name, proxySysConfig);

            _cluster.Join(_cluster.SelfAddress);
            AwaitAssert(() => { _cluster.SelfMember.Status.ShouldBe(MemberStatus.Up); });

            // form a 2-node cluster
            var proxyCluster = Cluster.Get(_proxySys);
            proxyCluster.Join(_cluster.SelfAddress);
            AwaitAssert(() => { proxyCluster.SelfMember.Status.ShouldBe(MemberStatus.Up); });
        }

        protected override void AfterAll()
        {
            Shutdown(_proxySys);
            base.AfterAll();
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

        [Fact]
        public async Task ShardRegion_GetEntityLocation_test()
        {
            // creates an entity with entityId="1"
            await _shardRegion.Ask<int>(1, TimeSpan.FromSeconds(3));
            
            // determine where entity with "entityId=1" is located in cluster
            var q1 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("1", TimeSpan.FromSeconds(1)));

            q1.EntityId.Should().Be("1");
            
            // have a valid ShardId
            q1.ShardId.Should().NotBeEmpty();
            
            // have valid address for node that will / would host entity
            q1.ShardRegion.Should().NotBe(Address.AllSystems); // has real address
            
            // if entity actor is alive, will retrieve a reference to it
            q1.EntityRef.HasValue.Should().BeTrue();
        }

        [Fact(DisplayName = "ShardRegion should support GetEntityLocation queries locally")]
        public async Task ShardRegion_should_support_GetEntityLocation_query_locally()
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

        [Fact(DisplayName = "ShardRegion should support GetEntityLocation queries remotely")]
        public async Task ShardRegion_should_support_GetEntityLocation_query_remotely()
        {
            // arrange
            var sharding2 = ClusterSharding.Get(_proxySys);
            var shardRegionProxy = await sharding2.StartProxyAsync("entity", "shard", new MessageExtractor());
            
            await shardRegionProxy.Ask<int>(1, TimeSpan.FromSeconds(3));
            await shardRegionProxy.Ask<int>(2, TimeSpan.FromSeconds(3));

            // act
            var q1 = await shardRegionProxy.Ask<EntityLocation>(new GetEntityLocation("1", TimeSpan.FromSeconds(1)));
            var q2 = await shardRegionProxy.Ask<EntityLocation>(new GetEntityLocation("2", TimeSpan.FromSeconds(1)));
            var q3 = await shardRegionProxy.Ask<EntityLocation>(new GetEntityLocation("3", TimeSpan.FromSeconds(1)));

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
