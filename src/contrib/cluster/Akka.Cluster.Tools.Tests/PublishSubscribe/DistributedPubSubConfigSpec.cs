using System;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubConfigSpec : TestKit.Xunit2.TestKit
    {
        public DistributedPubSubConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
        }

        [Fact]
        public void Should_distributed_pub_sub_settings_have_default_config()
        {
            DistributedPubSub.Get(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.pub-sub");

            Assert.NotNull(config);
            Assert.Equal("distributedPubSubMediator", config.GetString("name"));
            Assert.Equal(string.Empty, config.GetString("role"));
            Assert.Equal("random", config.GetString("routing-logic"));
            Assert.Equal(TimeSpan.FromSeconds(1), config.GetTimeSpan("gossip-interval"));
            Assert.Equal(TimeSpan.FromSeconds(120), config.GetTimeSpan("removed-time-to-live"));
            Assert.Equal(3000, config.GetInt("max-delta-elements"));
            Assert.Equal(string.Empty, config.GetString("use-dispatcher"));
        }
    }
}
