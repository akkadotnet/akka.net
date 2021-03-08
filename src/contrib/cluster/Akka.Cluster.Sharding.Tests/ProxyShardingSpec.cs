//-----------------------------------------------------------------------
// <copyright file="ProxyShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ProxyShardingSpec : Akka.TestKit.Xunit2.TestKit
    {
        ClusterSharding clusterSharding;
        ClusterShardingSettings shardingSettings;
        private MessageExtractor messageExtractor = new MessageExtractor(10);

        private class MessageExtractor : HashCodeMessageExtractor
        {
            public MessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards)
            {
            }

            public override string EntityId(object message)
            {
                return "dummyId";
            }
        }

        private Option<(string, object)> IdExtractor(object message)
        {
            switch (message)
            {
                case int i:
                    return (i.ToString(), message);
            }
            throw new NotSupportedException();
        }

        private string ShardResolver(object message)
        {
            switch (message)
            {
                case int i:
                    return i.ToString();
            }
            throw new NotSupportedException();
        }


        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());

        public ProxyShardingSpec() : base(SpecConfig)
        {
            var role = "Shard";
            clusterSharding = ClusterSharding.Get(Sys);
            shardingSettings = ClusterShardingSettings.Create(Sys);
            clusterSharding.StartProxy("myType", role, IdExtractor, ShardResolver);
        }

        [Fact]
        public void ProxyShardingSpec_Proxy_should_be_found()
        {
            IActorRef proxyActor = Sys.ActorSelection("akka://test/system/sharding/myTypeProxy")
                    .ResolveOne(TimeSpan.FromSeconds(5)).Result;

            proxyActor.Path.Should().NotBeNull();
            proxyActor.Path.ToString().Should().EndWith("Proxy");
        }

        [Fact]
        public void ProxyShardingSpec_Shard_region_should_be_found()
        {
            var shardRegion = clusterSharding.Start("myType", SimpleEchoActor.Props(), shardingSettings, messageExtractor);

            shardRegion.Path.Should().NotBeNull();
            shardRegion.Path.ToString().Should().EndWith("myType");
        }

        [Fact]
        public void ProxyShardingSpec_Shard_coordinator_should_be_found()
        {
            var shardRegion = clusterSharding.Start("myType", SimpleEchoActor.Props(), shardingSettings, messageExtractor);

            IActorRef shardCoordinator = Sys.ActorSelection("akka://test/system/sharding/myTypeCoordinator")
                    .ResolveOne(TimeSpan.FromSeconds(5)).Result;

            shardCoordinator.Path.Should().NotBeNull();
            shardCoordinator.Path.ToString().Should().EndWith("Coordinator");
        }
    }
}
