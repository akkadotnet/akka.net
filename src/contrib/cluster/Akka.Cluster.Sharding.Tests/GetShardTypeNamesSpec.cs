//-----------------------------------------------------------------------
// <copyright file="GetShardTypeNamesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class GetShardTypeNamesSpec : Akka.TestKit.Xunit2.TestKit
    {
        public GetShardTypeNamesSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                     akka.remote.dot-netty.tcp.port = 0")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());
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
            ClusterSharding.Get(Sys).Start("type1", EchoActor.Props(this), settings, ExtractEntityId, ExtractShardId);
            ClusterSharding.Get(Sys).Start("type2", EchoActor.Props(this), settings, ExtractEntityId, ExtractShardId);

            ClusterSharding.Get(Sys).ShardTypeNames.ShouldBeEquivalentTo(new string[] { "type1", "type2" });
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
