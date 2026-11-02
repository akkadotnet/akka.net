//-----------------------------------------------------------------------
// <copyright file="GetShardTypeNamesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class GetShardTypeNamesSpec : AkkaSpec
    {
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
        
        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingleton.DefaultConfig());

        private readonly MessageExtractor _messageExtractor = new();
        
        public GetShardTypeNamesSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
        }

        [Fact]
        public void GetShardTypeNames_must_contain_empty_when_join_cluster_without_shards()
        {
            ClusterSharding.Get(Sys).ShardTypeNames.Should().BeEmpty();
        }

        [Fact]
        public void GetShardTypeNames_must_contain_started_shards_when_started_2_shards()
        {
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);
            var settings = ClusterShardingSettings.Create(Sys);
            ClusterSharding.Get(Sys).Start("type1", SimpleEchoActor.Props(), settings, _messageExtractor);
            ClusterSharding.Get(Sys).Start("type2", SimpleEchoActor.Props(), settings, _messageExtractor);

            ClusterSharding.Get(Sys).ShardTypeNames.Should().BeEquivalentTo("type1", "type2");
        }
    }
}
