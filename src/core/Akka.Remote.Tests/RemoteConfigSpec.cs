//-----------------------------------------------------------------------
// <copyright file="RemoteConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Akka.Remote.Artery;
using Akka.Remote.Transport.DotNetty;
using Akka.Remote.Transport;
using static Akka.Util.RuntimeDetector;

using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class RemoteConfigSpec : AkkaSpec
    {
        public RemoteConfigSpec():base(@"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.dot-netty.tcp.port = 0
            ") {}
        

        [Fact]
        public void Remoting_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var remoteSettings = RARP.For(Sys).Provider.RemoteSettings;

            Assert.False(remoteSettings.LogReceive);
            Assert.False(remoteSettings.LogSend);
            Assert.False(remoteSettings.UntrustedMode);
            Assert.Empty(remoteSettings.TrustedSelectionPaths);
            Assert.Equal(TimeSpan.FromSeconds(10), remoteSettings.ShutdownTimeout);
            Assert.Equal(TimeSpan.FromSeconds(2), remoteSettings.FlushWait);
            Assert.Equal(TimeSpan.FromSeconds(10), remoteSettings.StartupTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), remoteSettings.RetryGateClosedFor);
            Assert.Equal("akka.remote.default-remote-dispatcher", remoteSettings.Dispatcher);
            Assert.True(remoteSettings.UsePassiveConnections);
            Assert.Equal(TimeSpan.FromMilliseconds(50), remoteSettings.BackoffPeriod);
            Assert.Equal(50000, remoteSettings.LogBufferSizeExceeding);
            Assert.Equal(TimeSpan.FromSeconds(0.3d), remoteSettings.SysMsgAckTimeout);
            Assert.Equal(TimeSpan.FromSeconds(2), remoteSettings.SysResendTimeout);
            Assert.Equal(200, remoteSettings.SysResendLimit);
            Assert.Equal(20000, remoteSettings.SysMsgBufferSize);
            Assert.Equal(TimeSpan.FromMinutes(3), remoteSettings.InitialSysMsgDeliveryTimeout);
            Assert.Equal(TimeSpan.FromDays(5), remoteSettings.QuarantineDuration);
            Assert.Equal(TimeSpan.FromSeconds(30), remoteSettings.CommandAckTimeout);
            Assert.Equal(TimeSpan.FromDays(2), remoteSettings.QuarantineSilentSystemTimeout);
            Assert.Single(remoteSettings.Transports);
            Assert.Equal(typeof(TcpTransport), Type.GetType(remoteSettings.Transports.Head().TransportClass));

            var adapters = new Dictionary<string, Type>
            {
                { "gremlin", typeof(FailureInjectorProvider) },
                { "trttl",  typeof(ThrottlerProvider) }
            };
            remoteSettings.Adapters
                .ToDictionary(kvp => kvp.Key, kvp => Type.GetType(kvp.Value))
                .Should().BeEquivalentTo(adapters);

            Assert.Equal(typeof(PhiAccrualFailureDetector), Type.GetType(remoteSettings.WatchFailureDetectorImplementationClass));
            Assert.Equal(TimeSpan.FromSeconds(1), remoteSettings.WatchHeartBeatInterval);
            Assert.Equal(TimeSpan.FromSeconds(1), remoteSettings.WatchHeartbeatExpectedResponseAfter);
            Assert.Equal(TimeSpan.FromSeconds(1), remoteSettings.WatchUnreachableReaperInterval);
            Assert.Equal(10, remoteSettings.WatchFailureDetectorConfig.GetDouble("threshold"));
            Assert.Equal(200, remoteSettings.WatchFailureDetectorConfig.GetInt("max-sample-size"));
            Assert.Equal(TimeSpan.FromSeconds(10), remoteSettings.WatchFailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause"));
            Assert.Equal(TimeSpan.FromMilliseconds(100), remoteSettings.WatchFailureDetectorConfig.GetTimeSpan("min-std-deviation"));

            remoteSettings.Config.GetString("akka.remote.log-frame-size-exceeding").ShouldBe("off");
        }

        [Fact]
        public void Remoting_should_be_able_to_parse_AkkaProtocol_related_config_elements()
        {
            var settings = new AkkaProtocolSettings(RARP.For(Sys).Provider.RemoteSettings.Config);
            
            Assert.Equal(typeof(DeadlineFailureDetector), Type.GetType(settings.TransportFailureDetectorImplementationClass));
            Assert.Equal(TimeSpan.FromSeconds(4), settings.TransportHeartBeatInterval);
            Assert.Equal(TimeSpan.FromSeconds(120), settings.TransportFailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause"));
        }

        [Fact]
        public void Remoting_should_contain_correct_heliosTCP_values_in_ReferenceConf()
        {
            var c = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.tcp");
            var s = DotNettyTransportSettings.Create(c);

            Assert.Equal(TimeSpan.FromSeconds(15), s.ConnectTimeout);
            s.ConnectTimeout.Should().Be(new AkkaProtocolSettings(RARP.For(Sys).Provider.RemoteSettings.Config).HandshakeTimeout);
            Assert.Null(s.WriteBufferHighWaterMark);
            Assert.Null(s.WriteBufferLowWaterMark);
            Assert.Equal(256000, s.SendBufferSize.Value);
            Assert.Equal(256000, s.ReceiveBufferSize.Value);
            Assert.Equal(128000, s.MaxFrameSize);
            Assert.Equal(4096, s.Backlog);
            Assert.True(s.TcpNoDelay);
            Assert.True(s.TcpKeepAlive);

            Assert.Equal("off-for-windows", c.GetString("tcp-reuse-addr"));

            Assert.True(string.IsNullOrEmpty(c.GetString("hostname")));
            Assert.Null(s.PublicPort);
            Assert.Equal(2, s.ServerSocketWorkerPoolSize);
            Assert.Equal(2, s.ClientSocketWorkerPoolSize);
            Assert.False(s.BackwardsCompatibilityModeEnabled);
            Assert.False(s.DnsUseIpv6);
            Assert.False(s.LogTransport);
            Assert.False(s.EnableSsl);
        }

        [Fact]
        public void When_remoting_works_in_Mono_ip_enforcement_should_be_defaulted_to_true()
        {
            if (!IsMono) return; // skip IF NOT using Mono
            var c = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.tcp");
            var s = DotNettyTransportSettings.Create(c);
            
            Assert.True(s.EnforceIpFamily);
        }

        [Fact]
        public void When_remoting_works_not_in_Mono_ip_enforcement_should_be_defaulted_to_false()
        {
            if (IsMono) return; // skip IF using Mono
            var c = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.tcp");
            var s = DotNettyTransportSettings.Create(c);

            Assert.False(s.EnforceIpFamily);
        }


        [Fact]
        public void Remoting_should_contain_correct_socket_worker_pool_configuration_values_in_ReferenceConf()
        {
            var c = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.tcp");

            // server-socket-worker-pool
            {
                var pool = c.GetConfig("server-socket-worker-pool");
                Assert.Equal(2, pool.GetInt("pool-size-min"));
                Assert.Equal(1.0d, pool.GetDouble("pool-size-factor"));
                Assert.Equal(2, pool.GetInt("pool-size-max"));
            }

            //client-socket-worker-pool
            {
                var pool = c.GetConfig("client-socket-worker-pool");
                Assert.Equal(2, pool.GetInt("pool-size-min"));
                Assert.Equal(1.0d, pool.GetDouble("pool-size-factor"));
                Assert.Equal(2, pool.GetInt("pool-size-max"));
            }
        }

        [Fact]
        public void Remoting_should_contain_correct_hostname_values_in_ReferenceConf()
        {
           var c = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.tcp");
           var s = DotNettyTransportSettings.Create(c);

           //Non-specified hostnames should default to IPAddress.Any
           Assert.Equal(IPAddress.Any.ToString(), s.Hostname);
           Assert.Equal(IPAddress.Any.ToString(), s.PublicHostname);
        }

        [Fact]
        public void Remoting_should_contain_correct_BatchWriter_settings_in_ReferenceConf()
        {
            var c = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.tcp");
            var s = DotNettyTransportSettings.Create(c);

            s.BatchWriterSettings.EnableBatching.Should().BeTrue();
            s.BatchWriterSettings.MaxExplicitFlushes.Should().Be(BatchWriterSettings.DefaultMaxPendingWrites);
        }

        [Fact]
        public void Remoting_should_contain_correct_ssl_configuration_value_int_ReferenceConf()
        {
            var r = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.dot-netty.ssl");
            r.GetBoolean("suppress-validation").Should().BeFalse();

            var c = r.GetConfig("certificate");
            c.GetString("path").Should().BeEmpty();
            c.GetString("password").Should().BeEmpty();
            c.GetBoolean("use-thumprint-over-file").Should().BeFalse();
            c.GetString("thumbprint").Should().BeEmpty();
            c.GetString("store-name").Should().BeEmpty();
            c.GetString("store-location").Should().Be("current-user");
        }

        [Fact]
        public void ArterySettings_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery;

            // Root settings
            settings.Enabled.ShouldBeTrue();
            settings.Transport.ShouldBe(ArterySettings.TransportType.Tcp);
            settings.LargeMessageDestinations.IsEmpty.ShouldBeTrue();

            // ARTERY: Akka.NET SSL implementation is different compared to scala, this code is wrong.
            // settings.SslEngineProviderClassName.ShouldBe("Akka.Remote.Artery.Tcp.ConfigSSLEngineProvider,Akka.Remote");
            settings.UntrustedMode.ShouldBeFalse();
            settings.TrustedSelectionPaths.Count.ShouldBe(0);
            settings.LogReceive.ShouldBeFalse();
            settings.LogSend.ShouldBeFalse();

            // ARTERY: Version number is pulled from ArteryTransport, which isn't ported yet.
            // settings.Version.Should().Be(0);
        }

        [Fact]
        public void ArterySettings_Canonical_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery;

            // Canonical settings
            settings.Canonical.Port.ShouldBe(25520);
            settings.Canonical.Hostname.ShouldBe(ArterySettings.GetHostName("<getHostAddress>"));
        }

        [Fact]
        public void ArterySettings_Bind_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery;

            // Bind settings
            settings.Bind.Port.ShouldBe(settings.Canonical.Port);
            settings.Bind.Hostname.ShouldBe(settings.Canonical.Hostname);
            settings.Bind.BindTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void ArterySettings_Advanced_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery.Advanced;

            // Advanced settings
            settings.TestMode.ShouldBeFalse();
            settings.Dispatcher.ShouldBe("akka.remote.default-remote-dispatcher");
            settings.ControlStreamDispatcher.ShouldBe("akka.actor.internal-dispatcher");

            settings.OutboundLanes.ShouldBe(1);
            settings.InboundLanes.ShouldBe(4);

            settings.SysMsgBufferSize.ShouldBe(20000);

            settings.OutboundMessageQueueSize.ShouldBe(3072);
            settings.OutboundControlQueueSize.ShouldBe(20000);
            settings.OutboundLargeMessageQueueSize.ShouldBe(256);

            settings.SystemMessageResendInterval.ShouldBe(TimeSpan.FromSeconds(1));

            settings.HandshakeTimeout.ShouldBe(TimeSpan.FromSeconds(20));
            settings.HandshakeRetryInterval.ShouldBe(TimeSpan.FromSeconds(1));
            settings.InjectHandshakeInterval.ShouldBe(TimeSpan.FromSeconds(1));

            settings.GiveUpSystemMessageAfter.ShouldBe(TimeSpan.FromHours(6));

            settings.StopIdleOutboundAfter.ShouldBe(TimeSpan.FromMinutes(5));
            settings.QuarantineIdleOutboundAfter.ShouldBe(TimeSpan.FromHours(6));

            settings.StopQuarantinedAfterIdle.ShouldBe(TimeSpan.FromSeconds(3));
            settings.RemoveQuarantinedAssociationAfter.ShouldBe(TimeSpan.FromHours(1));

            settings.ShutdownFlushTimeout.ShouldBe(TimeSpan.FromSeconds(1));
            settings.InboundRestartTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            settings.InboundMaxRestarts.ShouldBe(5);
            settings.OutboundRestartBackoff.ShouldBe(TimeSpan.FromSeconds(1));
            settings.OutboundRestartTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            settings.OutboundMaxRestarts.ShouldBe(5);

            settings.MaximumFrameSize.ShouldBe(256 * 1024);
            settings.BufferPoolSize.ShouldBe(128);
            settings.InboundHubBufferSize.ShouldBe(128 / 2);
            settings.MaximumLargeFrameSize.ShouldBe(2 * 1024 * 1024);
            settings.LargeBufferPoolSize.ShouldBe(32);
        }

        // ARTERY: insert Advanced.MaterializerSettings validation here

        [Fact]
        public void ArterySettings_Advanced_Compression_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery.Advanced.Compression;

            settings.Enabled.ShouldBeTrue();

            settings.ActorRefs.Max.ShouldBe(256);
            settings.ActorRefs.AdvertisementInterval.ShouldBe(TimeSpan.FromMinutes(1));

            settings.Manifests.Max.ShouldBe(256);
            settings.Manifests.AdvertisementInterval.ShouldBe(TimeSpan.FromMinutes(1));
        }

        // Not being used yet since we're not implementing Aeron, but might as well check that this is proper
        [Fact]
        public void ArterySettings_Advanced_Aeron_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery.Advanced.Aeron;

            settings.LogAeronCounters.ShouldBeFalse();
            settings.EmbeddedMediaDriver.ShouldBeTrue();
            settings.AeronDirectoryName.Should().BeEmpty();
            settings.DeleteAeronDirectory.ShouldBeTrue();
            settings.IdleCpuLevel.ShouldBe(5);
            settings.GiveUpMessageAfter.ShouldBe(TimeSpan.FromSeconds(60));
            settings.ClientLivenessTimeout.ShouldBe(TimeSpan.FromSeconds(20));
            settings.PublicationUnblockTimeout.ShouldBe(TimeSpan.FromSeconds(40));
            settings.ImageLivenessTimeout.ShouldBe(TimeSpan.FromSeconds(10));
            settings.DriverTimeout.ShouldBe(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public void ArterySettings_Advanced_Tcp_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var settings = RARP.For(Sys).Provider.RemoteSettings.Artery.Advanced.Tcp;

            settings.ConnectionTimeout.ShouldBe(TimeSpan.FromSeconds(5));
            settings.OutboundClientHostname.Should().BeEmpty();
        }

        [Fact]
        public void Helper_extension_methods_should_return_proper_values()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            string hostIp = null;
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    hostIp = ip.ToString();
                    break;
                }
            }

            ArterySettings.GetHostName("<getHostAddress>").ShouldBe(hostIp); 
            ArterySettings.GetHostName("<getHostName>").ShouldBe(Dns.GetHostName());

            ArterySettings.GetTransport("tcp").Should().Be(ArterySettings.TransportType.Tcp);
            ArterySettings.GetTransport("tls-tcp").Should().Be(ArterySettings.TransportType.TlsTcp);

            "aeron-udp".Invoking(ArterySettings.GetTransport)
                .Should().Throw<ConfigurationException>()
                .WithMessage("Aeron transport is not supported.");
        }

    }
}

