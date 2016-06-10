//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
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
            config.GetString("name").ShouldBe("distributedPubSubMediator");
            config.GetString("use-dispatcher").ShouldBe(string.Empty);
        }
    }
}