//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubConfigSpec : AkkaSpec
    {
        public DistributedPubSubConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                    akka.extensions = [""Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider,Akka.Cluster.Tools""]
                                                    akka.remote.dot-netty.tcp.port = 0");
        }

        [Fact]
        public void DistributedPubSubSettings_must_have_default_config()
        {
            var distributedPubSubSettings = DistributedPubSubSettings.Create(Sys);

            distributedPubSubSettings.ShouldNotBe(null);
            distributedPubSubSettings.Role.ShouldBe(null);
            distributedPubSubSettings.RoutingLogic.GetType().ShouldBe(typeof(RandomLogic));
            distributedPubSubSettings.GossipInterval.TotalSeconds.ShouldBe(1);
            distributedPubSubSettings.RemovedTimeToLive.TotalSeconds.ShouldBe(120);
            distributedPubSubSettings.MaxDeltaElements.ShouldBe(3000);

            var config = Sys.Settings.Config.GetConfig("akka.cluster.pub-sub");
            Assert.False(config.IsNullOrEmpty());
            config.GetString("name").ShouldBe("distributedPubSubMediator");
            config.GetString("use-dispatcher").ShouldBe(string.Empty);
        }

        [Fact]
        public void DistributedPubSub_must_load_via_HOCON()
        {
            // Validate that the syntax recommended at http://getakka.net/articles/clustering/distributed-publish-subscribe.html
            // for automatically loading the DistributedPubSub plugin at startup is correct
            Assert.True(Sys.HasExtension<DistributedPubSub>());
        }
    }
}
