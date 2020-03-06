//-----------------------------------------------------------------------
// <copyright file="ClusterClientConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using System.Collections.Immutable;

namespace Akka.Cluster.Tools.Tests.ClusterClient
{
    public class ClusterClientConfigSpec : AkkaSpec
    {
        public ClusterClientConfigSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.remote.dot-netty.tcp.port = 0");
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
        [InlineData(-1)]
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

        /// <summary>
        /// Addresses the bug discussed here: https://github.com/akkadotnet/akka.net/issues/3417#issuecomment-397443227
        /// </summary>
        [Fact]
        public void ClusterClientSettings_must_copy_initial_contacts_via_fluent_interface()
        {
            var initialContacts = ImmutableHashSet<ActorPath>.Empty.Add(new RootActorPath(Address.AllSystems) / "user" / "foo");
            var clusterClientSettings = ClusterClientSettings.Create(Sys).WithInitialContacts(initialContacts).WithBufferSize(2000);
            clusterClientSettings.InitialContacts.Should().BeEquivalentTo(initialContacts);
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
            Assert.False(config.IsNullOrEmpty());
            config.GetString("name").Should().Be("receptionist");
            config.GetString("use-dispatcher").Should().Be(string.Empty);
        }
    }
}
