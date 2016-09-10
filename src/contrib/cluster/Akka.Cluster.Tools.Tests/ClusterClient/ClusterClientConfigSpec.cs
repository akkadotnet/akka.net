//-----------------------------------------------------------------------
// <copyright file="ClusterClientConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Client;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tests.ClusterClient
{
    public class ClusterClientConfigSpec : AkkaSpec
    {
        public ClusterClientConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
        }

        [Fact]
        public void ClusterClientSettings_must_have_default_config()
        {
            var clusterClientSettings = ClusterClientSettings.Create(Sys);

            clusterClientSettings.Should().NotBeNull();
            clusterClientSettings.InitialContacts.Should().HaveCount(0);
            clusterClientSettings.EstablishingGetContactsInterval.Should().Be(3.Seconds());
            clusterClientSettings.RefreshContactsInterval.Should().Be(60.Seconds());
            clusterClientSettings.HeartbeatInterval.Should().Be(2.Seconds());
            clusterClientSettings.AcceptableHeartbeatPause.Should().Be(13.Seconds());
            clusterClientSettings.BufferSize.Should().Be(1000);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10001)]
        public void ClusterClientSettings_must_throw_exception_on_wrong_buffer(int bufferSize)
        {
            var sys = ActorSystem.Create("test", ConfigurationFactory.ParseString("akka.cluster.client.buffer-size = " + bufferSize));
            var exception = Assert.Throws<ArgumentException>(() => ClusterClientSettings.Create(sys));
            exception.Message.Should().Be("BufferSize must be >= 0 and <= 10000");
        }

        [Fact]
        public void ClusterClientSettings_must_throw_exception_on_empty_initial_contacts()
        {
            var clusterClientSettings = ClusterClientSettings.Create(Sys);
            var exception = Assert.Throws<ArgumentException>(() => clusterClientSettings.WithInitialContacts(ImmutableHashSet<ActorPath>.Empty));
            exception.Message.Should().Be("InitialContacts must be defined");
        }

        [Fact]
        public void ClusterReceptionistSettings_must_have_default_config()
        {
            var clusterReceptionistSettings = ClusterReceptionistSettings.Create(Sys);
            clusterReceptionistSettings.Should().NotBeNull();
            clusterReceptionistSettings.Role.Should().BeNull();
            clusterReceptionistSettings.NumberOfContacts.Should().Be(3);
            clusterReceptionistSettings.ResponseTunnelReceiveTimeout.Should().Be(30.Seconds());
            clusterReceptionistSettings.HeartbeatInterval.Should().Be(2.Seconds());
            clusterReceptionistSettings.AcceptableHeartbeatPause.Should().Be(13.Seconds());
            clusterReceptionistSettings.FailureDetectionInterval.Should().Be(2.Seconds());

            var config = Sys.Settings.Config.GetConfig("akka.cluster.client.receptionist");
            config.GetString("name").Should().Be("receptionist");
            config.GetString("use-dispatcher").Should().Be(string.Empty);
        }
    }
}
