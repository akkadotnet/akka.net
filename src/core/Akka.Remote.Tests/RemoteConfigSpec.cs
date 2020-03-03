//-----------------------------------------------------------------------
// <copyright file="RemoteConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using System.Net;
using Akka.Remote.Transport;
using static Akka.Util.RuntimeDetector;
using FluentAssertions;

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
            Assert.Equal(TimeSpan.FromSeconds(0.3d), remoteSettings.SysMsgAckTimeout);
            Assert.Equal(TimeSpan.FromSeconds(2), remoteSettings.SysResendTimeout);
            Assert.Equal(20000, remoteSettings.SysMsgBufferSize);
            Assert.Equal(TimeSpan.FromMinutes(3), remoteSettings.InitialSysMsgDeliveryTimeout);
            Assert.Equal(TimeSpan.FromDays(5), remoteSettings.QuarantineDuration);
            Assert.Equal(TimeSpan.FromDays(2), remoteSettings.QuarantineSilentSystemTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), remoteSettings.CommandAckTimeout);
            Assert.Single(remoteSettings.Transports);
            Assert.Equal(typeof(TcpTransport), Type.GetType(remoteSettings.Transports.Head().TransportClass));
            Assert.Equal(typeof(PhiAccrualFailureDetector), Type.GetType(remoteSettings.WatchFailureDetectorImplementationClass));
            Assert.Equal(TimeSpan.FromSeconds(1), remoteSettings.WatchHeartBeatInterval);
            Assert.Equal(TimeSpan.FromSeconds(1), remoteSettings.WatchHeartbeatExpectedResponseAfter);
            Assert.Equal(TimeSpan.FromSeconds(1), remoteSettings.WatchUnreachableReaperInterval);
            Assert.Equal(10, remoteSettings.WatchFailureDetectorConfig.GetDouble("threshold", 0));
            Assert.Equal(200, remoteSettings.WatchFailureDetectorConfig.GetDouble("max-sample-size", 0));
            Assert.Equal(TimeSpan.FromSeconds(10), remoteSettings.WatchFailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause", null));
            Assert.Equal(TimeSpan.FromMilliseconds(100), remoteSettings.WatchFailureDetectorConfig.GetTimeSpan("min-std-deviation", null));

            var remoteSettingsAdaptersStandart = new List<KeyValuePair<string, Type>>()
            {
                new KeyValuePair<string, Type>("gremlin", typeof(FailureInjectorProvider)),
                new KeyValuePair<string, Type>("trttl", typeof(ThrottlerProvider))
            };

            var remoteSettingsAdapters =
                remoteSettings.Adapters.Select(kv => new KeyValuePair<string, Type>(kv.Key, Type.GetType(kv.Value)));

            Assert.Empty(remoteSettingsAdapters.Except(remoteSettingsAdaptersStandart));

            remoteSettings.Config.GetString("akka.remote.log-frame-size-exceeding", null).ShouldBe("off");
        }

        [Fact]
        public void Remoting_should_be_able_to_parse_AkkaProtocol_related_config_elements()
        {
            var settings = new AkkaProtocolSettings(RARP.For(Sys).Provider.RemoteSettings.Config);
            
            Assert.Equal(typeof(DeadlineFailureDetector), Type.GetType(settings.TransportFailureDetectorImplementationClass));
            Assert.Equal(TimeSpan.FromSeconds(4), settings.TransportHeartBeatInterval);
            Assert.Equal(TimeSpan.FromSeconds(120), settings.TransportFailureDetectorConfig.GetTimeSpan("acceptable-heartbeat-pause", null));
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
            Assert.Equal("off-for-windows", c.GetString("tcp-reuse-addr", null));
            Assert.True(string.IsNullOrEmpty(c.GetString("hostname", null)));
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
                Assert.Equal(2, pool.GetInt("pool-size-min", 0));
                Assert.Equal(1.0d, pool.GetDouble("pool-size-factor", 0));
                Assert.Equal(2, pool.GetInt("pool-size-max", 0));
            }

            //client-socket-worker-pool
            {
                var pool = c.GetConfig("client-socket-worker-pool");
                Assert.Equal(2, pool.GetInt("pool-size-min", 0));
                Assert.Equal(1.0d, pool.GetDouble("pool-size-factor", 0));
                Assert.Equal(2, pool.GetInt("pool-size-max", 0));
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
            s.BatchWriterSettings.FlushInterval.Should().Be(BatchWriterSettings.DefaultFlushInterval);
            s.BatchWriterSettings.MaxPendingBytes.Should().Be(BatchWriterSettings.DefaultMaxPendingBytes);
            s.BatchWriterSettings.MaxPendingWrites.Should().Be(BatchWriterSettings.DefaultMaxPendingWrites);
        }
   }
}

