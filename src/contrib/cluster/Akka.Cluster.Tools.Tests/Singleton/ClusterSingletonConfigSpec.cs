using System;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonConfigSpec : TestKit.Xunit2.TestKit
    {
        public ClusterSingletonConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
        }

        [Fact]
        public void Should_cluster_singleton_manager_settings_have_default_config()
        {
            ClusterSingletonManagerSettings.Create(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.singleton");

            Assert.NotNull(config);
            Assert.Equal("singleton", config.GetString("singleton-name"));
            Assert.Equal(string.Empty, config.GetString("role"));
            Assert.Equal(TimeSpan.FromSeconds(1), config.GetTimeSpan("hand-over-retry-interval"));
            Assert.Equal(10, config.GetInt("min-number-of-hand-over-retries"));
        }

        [Fact]
        public void Should_singleton_proxy_have_default_config()
        {
            ClusterSingletonProxySettings.Create(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.singleton-proxy");

            Assert.NotNull(config);
            Assert.Equal("singleton", config.GetString("singleton-name"));
            Assert.Equal(string.Empty, config.GetString("role"));
            Assert.Equal(TimeSpan.FromSeconds(1), config.GetTimeSpan("singleton-identification-interval"));
            Assert.Equal(1000, config.GetInt("buffer-size"));
        }
    }
}
