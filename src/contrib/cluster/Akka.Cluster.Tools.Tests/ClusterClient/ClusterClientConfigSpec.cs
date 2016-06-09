//-----------------------------------------------------------------------
// <copyright file="ClusterClientConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Client
{
    public class ClusterClientConfigSpec : TestKit.Xunit2.TestKit
    {
        public ClusterClientConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
        }

        [Fact]
        public void Should_cluster_client_settings_have_default_config()
        {
            ClusterClientSettings.Create(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.client");

            Assert.NotNull(config);
            var initialContacts = config.GetStringList("initial-contacts");
            Assert.Equal(0, initialContacts.Count);
            Assert.Equal(TimeSpan.FromSeconds(3), config.GetTimeSpan("establishing-get-contacts-interval"));
            Assert.Equal(TimeSpan.FromSeconds(60), config.GetTimeSpan("refresh-contacts-interval"));
            Assert.Equal(TimeSpan.FromSeconds(2), config.GetTimeSpan("heartbeat-interval"));
            Assert.Equal(TimeSpan.FromSeconds(13), config.GetTimeSpan("acceptable-heartbeat-pause"));
            Assert.Equal(TimeSpan.FromSeconds(3), config.GetTimeSpan("establishing-get-contacts-interval"));
            Assert.Equal(1000, config.GetInt("buffer-size"));
        }

        [Fact]
        public void Should_cluster_receptionist_settings_have_default_config()
        {
            ClusterClientReceptionist.Get(Sys);
            var config = Sys.Settings.Config.GetConfig("akka.cluster.client.receptionist");

            Assert.NotNull(config);
            Assert.Equal("receptionist", config.GetString("name"));
            Assert.Equal(string.Empty, config.GetString("role"));
            Assert.Equal(3, config.GetInt("number-of-contacts"));
            Assert.Equal(TimeSpan.FromSeconds(30), config.GetTimeSpan("response-tunnel-receive-timeout"));
            Assert.Equal(string.Empty, config.GetString("use-dispatcher"));
        }
    }
}
