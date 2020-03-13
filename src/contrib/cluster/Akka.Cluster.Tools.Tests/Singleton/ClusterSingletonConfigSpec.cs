//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
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
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.remote.dot-netty.tcp.port = 0");
        }

        [Fact]
        public void ClusterSingletonManagerSettings_must_have_default_config()
        {
            var clusterSingletonManagerSettings = ClusterSingletonManagerSettings.Create(Sys);

            clusterSingletonManagerSettings.ShouldNotBe(null);
            clusterSingletonManagerSettings.SingletonName.ShouldBe("singleton");
            clusterSingletonManagerSettings.Role.ShouldBe(null);
            clusterSingletonManagerSettings.HandOverRetryInterval.TotalSeconds.ShouldBe(1);
            clusterSingletonManagerSettings.RemovalMargin.TotalSeconds.ShouldBe(0);

            var config = Sys.Settings.Config.GetConfig("akka.cluster.singleton");
            Assert.False(config.IsNullOrEmpty());
            config.GetInt("min-number-of-hand-over-retries", 0).ShouldBe(15);
        }

        [Fact]
        public void ClusterSingletonProxySettings_must_have_default_config()
        {
            var clusterSingletonProxySettings = ClusterSingletonProxySettings.Create(Sys);

            clusterSingletonProxySettings.ShouldNotBe(null);
            clusterSingletonProxySettings.SingletonName.ShouldBe("singleton");
            clusterSingletonProxySettings.Role.ShouldBe(null);
            clusterSingletonProxySettings.SingletonIdentificationInterval.TotalSeconds.ShouldBe(1);
            clusterSingletonProxySettings.BufferSize.ShouldBe(1000);
        }
    }
}
