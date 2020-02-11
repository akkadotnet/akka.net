//-----------------------------------------------------------------------
// <copyright file="ClusterShardingConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Hocon;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingConfigSpec : Akka.TestKit.Xunit2.TestKit
    {
        public ClusterShardingConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.remote.dot-netty.tcp.port = 0");
        }

        [Fact]
        public void Should_cluster_sharding_settings_have_default_config()
        {
            ClusterSharding.Get(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.sharding");

            Assert.False(config.IsNullOrEmpty());
            Assert.Equal("sharding", config.GetString("guardian-name", null));
            Assert.Equal(string.Empty, config.GetString("role", null));
            Assert.False(config.GetBoolean("remember-entities", false));
            Assert.Equal(TimeSpan.FromSeconds(5), config.GetTimeSpan("coordinator-failure-backoff", null));
            Assert.Equal(TimeSpan.FromSeconds(2), config.GetTimeSpan("retry-interval", null));
            Assert.Equal(100000, config.GetInt("buffer-size", 0));
            Assert.Equal(TimeSpan.FromSeconds(60), config.GetTimeSpan("handoff-timeout", null));
            Assert.Equal(TimeSpan.FromSeconds(10), config.GetTimeSpan("shard-start-timeout", null));
            Assert.Equal(TimeSpan.FromSeconds(10), config.GetTimeSpan("entity-restart-backoff", null));
            Assert.Equal(TimeSpan.FromSeconds(10), config.GetTimeSpan("rebalance-interval", null));
            Assert.Equal(string.Empty, config.GetString("journal-plugin-id", null));
            Assert.Equal(string.Empty, config.GetString("snapshot-plugin-id", null));
            Assert.Equal("persistence", config.GetString("state-store-mode", null));
            Assert.Equal(TimeSpan.FromSeconds(5), config.GetTimeSpan("waiting-for-state-timeout", null));
            Assert.Equal(TimeSpan.FromSeconds(5), config.GetTimeSpan("updating-state-timeout", null));
            Assert.Equal("akka.cluster.singleton", config.GetString("coordinator-singleton", null));
            Assert.Equal(string.Empty, config.GetString("use-dispatcher", null));

            Assert.Equal(1, config.GetInt("least-shard-allocation-strategy.rebalance-threshold", 0));
            Assert.Equal(3, config.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance", 0));

            Assert.Equal("all", config.GetString("entity-recovery-strategy", null));
            Assert.Equal(TimeSpan.FromMilliseconds(100), config.GetTimeSpan("entity-recovery-constant-rate-strategy.frequency", null));
            Assert.Equal(5, config.GetInt("entity-recovery-constant-rate-strategy.number-of-entities", 0));

            var singletonConfig = Sys.Settings.Config.GetConfig("akka.cluster.singleton");

            Assert.NotNull(singletonConfig);
            Assert.Equal("singleton", singletonConfig.GetString("singleton-name", null));
            Assert.Equal(string.Empty, singletonConfig.GetString("role", null));
            Assert.Equal(TimeSpan.FromSeconds(1), singletonConfig.GetTimeSpan("hand-over-retry-interval", null));
            Assert.Equal(15, singletonConfig.GetInt("min-number-of-hand-over-retries", 0));
        }
    }
}
