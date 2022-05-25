//-----------------------------------------------------------------------
// <copyright file="GetShardTypeNamesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class GetShardTypeNamesSpec : AkkaSpec
    {
        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());

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
            ClusterSharding.Get(Sys).Start("type1", SimpleEchoActor.Props(), settings, ExtractEntityId, ExtractShardId);
            ClusterSharding.Get(Sys).Start("type2", SimpleEchoActor.Props(), settings, ExtractEntityId, ExtractShardId);

            ClusterSharding.Get(Sys).ShardTypeNames.Should().BeEquivalentTo("type1", "type2");
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
            }
            throw new NotSupportedException();
        }
    }
}
