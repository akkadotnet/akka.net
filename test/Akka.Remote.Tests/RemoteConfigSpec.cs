using System;
using Akka.Remote.Transport.Helios;
using Akka.Tests;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class RemoteConfigSpec : AkkaSpec
    {

        #region Setup / Configuration
        protected override string GetConfig()
        {
            return @"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.helios.tcp.port = 0
            ";
        }
        #endregion

        [Fact]
        public void Remoting_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var remoteSettings = ((RemoteActorRefProvider) sys.Provider).RemoteSettings;

            Assert.False(remoteSettings.LogReceive);
            Assert.False(remoteSettings.LogSend);
            Assert.False(remoteSettings.UntrustedMode);
            Assert.Equal(0, remoteSettings.TrustedSelectionPaths.Count);
            Assert.Equal(TimeSpan.FromSeconds(10), remoteSettings.ShutdownTimeout);
            Assert.Equal(TimeSpan.FromSeconds(2), remoteSettings.FlushWait);
            Assert.Equal(TimeSpan.FromSeconds(10), remoteSettings.StartupTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), remoteSettings.RetryGateClosedFor);
            //Assert.Equal("akka.remote.default-remote-dispatcher", remoteSettings.Dispatcher); //TODO: add RemoteDispatcher support
            Assert.True(remoteSettings.UsePassiveConnections);
            Assert.Equal(TimeSpan.FromMilliseconds(10), remoteSettings.BackoffPeriod);
            Assert.Equal(TimeSpan.FromSeconds(0.3d), remoteSettings.SysMsgAckTimeout);
            Assert.Equal(TimeSpan.FromSeconds(2), remoteSettings.SysResendTimeout);
            Assert.Equal(1000, remoteSettings.SysMsgBufferSize);
            Assert.Equal(TimeSpan.FromMinutes(3), remoteSettings.InitialSysMsgDeliveryTimeout);
            Assert.Equal(TimeSpan.FromDays(5), remoteSettings.QuarantineDuration);
            Assert.Equal(TimeSpan.FromSeconds(30), remoteSettings.CommandAckTimeout);
            Assert.Equal(1, remoteSettings.Transports.Length);
            Assert.Equal(typeof(HeliosTcpTransport), Type.GetType(remoteSettings.Transports.Head().TransportClass));

            //TODO add adapter support
            //TODO add remote watcher support
        }

        [Fact]
        public void Remoting_should_be_able_to_parse_AkkaProtocol_related_config_elements()
        {
            var settings = new AkkaProtocolSettings(((RemoteActorRefProvider) sys.Provider).RemoteSettings.Config);

            //TODO fill this in when we add secure cookie support
            Assert.Equal(typeof(PhiAccrualFailureDetector), Type.GetType(settings.TransportFailureDetectorImplementationClass));
            Assert.Equal(TimeSpan.FromSeconds(4), settings.TransportHeartBeatInterval);
            Assert.True(Math.Abs(settings.TransportFailureDetectorConfig.GetDouble("threshold") - 7.0) <= 0.0001);
            Assert.Equal(100, settings.TransportFailureDetectorConfig.GetDouble("max-sample-size"));
            Assert.Equal(TimeSpan.FromSeconds(10), settings.TransportFailureDetectorConfig.GetMillisDuration("acceptable-heartbeat-pause"));
            Assert.Equal(TimeSpan.FromMilliseconds(100), settings.TransportFailureDetectorConfig.GetMillisDuration("min-std-deviation"));
        }

        [Fact]
        public void Remoting_should_contain_correct_heliosTCP_values_in_ReferenceConf()
        {
            var c = ((RemoteActorRefProvider)sys.Provider).RemoteSettings.Config.GetConfig("akka.remote.helios.tcp");
            var s = new HeliosTransportSettings(c);

            Assert.Equal(TimeSpan.FromSeconds(15), s.ConnectTimeout);
            Assert.Null(s.WriteBufferHighWaterMark);
            Assert.Null(s.WriteBufferLowWaterMark);
            Assert.Equal(256000, s.SendBufferSize.Value);
            Assert.Equal(256000, s.ReceiveBufferSize.Value);
            Assert.Equal(128000, s.MaxFrameSize);
            Assert.Equal(4096, s.Backlog);
            Assert.True(s.TcpNoDelay);
            Assert.True(s.TcpKeepAlive);
            Assert.True(s.TcpReuseAddr);
            Assert.True(string.IsNullOrEmpty(c.GetString("hostname")));
            Assert.Equal(2, s.ServerSocketWorkerPoolSize);
            Assert.Equal(2, s.ClientSocketWorkerPoolSize);
        }

        [Fact]
        public void Remoting_should_contain_correct_socket_worker_pool_configuration_values_in_ReferenceConf()
        {
            var c = ((RemoteActorRefProvider)sys.Provider).RemoteSettings.Config.GetConfig("akka.remote.helios.tcp");

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
    }
}
