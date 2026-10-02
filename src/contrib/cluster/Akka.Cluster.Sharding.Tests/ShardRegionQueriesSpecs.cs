//-----------------------------------------------------------------------
// <copyright file="ShardRegionQueriesSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardRegionQueriesSpecs : AkkaSpec
    {
        private readonly Cluster _cluster;
        private readonly ClusterSharding _clusterSharding;
        private readonly MessageExtractor _messageExtractor = new();
        private IActorRef _shardRegion;

        private readonly ActorSystem _proxySys;

        public ShardRegionQueriesSpecs(ITestOutputHelper outputHelper) : base(GetConfig(), outputHelper)
        {
            _clusterSharding = ClusterSharding.Get(Sys);
            _cluster = Cluster.Get(Sys);

            var proxySysConfig = ConfigurationFactory.ParseString("akka.cluster.roles = [proxy]")
                .WithFallback(Sys.Settings.Config);
            _proxySys = ActorSystem.Create(Sys.Name, proxySysConfig);

            // the proxy system's own log, so a failure on its side (e.g. who closed the association) shows up
            InitializeLogger(_proxySys, "[proxy]");
        }

        // Start the region and form the 2-node cluster without blocking a pool thread: JoinAsync waits
        // on the real MemberUp, and xUnit v3 awaits this before the test runs.
        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            try
            {
                _shardRegion = await _clusterSharding.StartAsync("entity", _ => EchoActor.Props(this, true),
                    ClusterShardingSettings.Create(Sys).WithRole("shard"), _messageExtractor);

                using var cts = new CancellationTokenSource(Dilated(TimeSpan.FromSeconds(30)));
                await _cluster.JoinAsync(_cluster.SelfAddress, cts.Token);
                await Cluster.Get(_proxySys).JoinAsync(_cluster.SelfAddress, cts.Token);
            }
            catch
            {
                // xUnit does not dispose a test class whose InitializeAsync threw, so AfterAll would never
                // shut the proxy system down
                await DisposeAsync();
                throw;
            }
        }

        protected override void AfterAll()
        {
            Shutdown(_proxySys);
            base.AfterAll();
        }

        private class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    int i => i.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string ShardId(object message)
                => message switch
                {
                    int i => (i % 10).ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => (int.Parse(entityId) % 10).ToString();
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
                .WithFallback(ClusterSingleton.DefaultConfig());
        }

        // The first message through a region may need the coordinator's registration retries (up to 2 s
        // apart) and, after a dropped association, a 5 s gate; 3 s was too tight for a 2-vCPU agent.
        private TimeSpan EntityAskTimeout => Dilated(TimeSpan.FromSeconds(10));
        private TimeSpan QueryTimeout => Dilated(TimeSpan.FromSeconds(3));

        /// <summary>
        /// DocFx material for demonstrating how this query type works
        /// </summary>
        [Fact]
        public async Task ShardRegion_GetEntityLocation_DocumentationSpec()
        {
            // <GetEntityLocationQuery>
            // creates an entity with entityId="1"
            await _shardRegion.Ask<int>(1, EntityAskTimeout);
            
            // determine where entity with "entityId=1" is located in cluster
            var q1 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("1", QueryTimeout));

            q1.EntityId.Should().Be("1");
            
            // have a valid ShardId
            q1.ShardId.Should().NotBeEmpty();
            
            // have valid address for node that will / would host entity
            q1.ShardRegion.Should().NotBe(Address.AllSystems); // has real address
            
            // if entity actor is alive, will retrieve a reference to it
            q1.EntityRef.HasValue.Should().BeTrue();
            // </GetEntityLocationQuery>
        }

        [Fact(DisplayName = "ShardRegion should support GetEntityLocation queries locally")]
        public async Task ShardRegion_should_support_GetEntityLocation_query_locally()
        {
            // arrange
            await _shardRegion.Ask<int>(1, EntityAskTimeout);
            await _shardRegion.Ask<int>(2, EntityAskTimeout);

            // act
            var q1 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("1", QueryTimeout));
            var q2 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("2", QueryTimeout));
            var q3 = await _shardRegion.Ask<EntityLocation>(new GetEntityLocation("3", QueryTimeout));

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
            var shardRegionProxy = await sharding2.StartProxyAsync("entity", "shard", _messageExtractor);
            
            await shardRegionProxy.Ask<int>(1, EntityAskTimeout);
            await shardRegionProxy.Ask<int>(2, EntityAskTimeout);

            // act
            var q1 = await shardRegionProxy.Ask<EntityLocation>(new GetEntityLocation("1", QueryTimeout));
            var q2 = await shardRegionProxy.Ask<EntityLocation>(new GetEntityLocation("2", QueryTimeout));
            var q3 = await shardRegionProxy.Ask<EntityLocation>(new GetEntityLocation("3", QueryTimeout));

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
