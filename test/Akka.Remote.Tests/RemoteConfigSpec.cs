using System;
using Akka.Remote.Transport.Helios;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests
{
    [TestClass]
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

        [TestMethod]
        public void Remoting_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var remoteSettings = ((RemoteActorRefProvider) sys.Provider).RemoteSettings;

            Assert.IsFalse(remoteSettings.LogReceive);
            Assert.IsFalse(remoteSettings.LogSend);
            Assert.IsFalse(remoteSettings.UntrustedMode);
            Assert.AreEqual(0, remoteSettings.TrustedSelectionPaths.Count);
            Assert.AreEqual(TimeSpan.FromSeconds(10), remoteSettings.ShutdownTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(2), remoteSettings.FlushWait);
            Assert.AreEqual(TimeSpan.FromSeconds(10), remoteSettings.StartupTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(5), remoteSettings.RetryGateClosedFor);
            //Assert.AreEqual("akka.remote.default-remote-dispatcher", remoteSettings.Dispatcher); //TODO: add RemoteDispatcher support
            Assert.IsTrue(remoteSettings.UsePassiveConnections);
            Assert.AreEqual(TimeSpan.FromMilliseconds(10), remoteSettings.BackoffPeriod);
            Assert.AreEqual(TimeSpan.FromSeconds(0.3d), remoteSettings.SysMsgAckTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(2), remoteSettings.SysResendTimeout);
            Assert.AreEqual(1000, remoteSettings.SysMsgBufferSize);
            Assert.AreEqual(TimeSpan.FromMinutes(3), remoteSettings.InitialSysMsgDeliveryTimeout);
            Assert.AreEqual(TimeSpan.FromDays(5), remoteSettings.QuarantineDuration);
            Assert.AreEqual(TimeSpan.FromSeconds(30), remoteSettings.CommandAckTimeout);
            Assert.AreEqual(1, remoteSettings.Transports.Length);
            Assert.AreEqual(typeof(HeliosTcpTransport), Type.GetType(remoteSettings.Transports.Head().TransportClass));

            //TODO add adapter support
            //TODO add remote watcher support
        }

        [TestMethod]
        public void Remoting_should_be_able_to_parse_AkkaProtocol_related_config_elements()
        {
            var settings = new AkkaProtocolSettings(((RemoteActorRefProvider) sys.Provider).RemoteSettings.Config);

            //TODO fill this in when we add secure cookie support
            Assert.AreEqual(typeof(PhiAccrualFailureDetector), Type.GetType(settings.TransportFailureDetectorImplementationClass));
            Assert.AreEqual(TimeSpan.FromSeconds(4), settings.TransportHeartBeatInterval);
            Assert.IsTrue(Math.Abs(settings.TransportFailureDetectorConfig.GetDouble("threshold") - 7.0) <= 0.0001);
            Assert.AreEqual(100, settings.TransportFailureDetectorConfig.GetDouble("max-sample-size"));
            Assert.AreEqual(TimeSpan.FromSeconds(10), settings.TransportFailureDetectorConfig.GetMillisDuration("acceptable-heartbeat-pause"));
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), settings.TransportFailureDetectorConfig.GetMillisDuration("min-std-deviation"));
        }

        [TestMethod]
        public void Remoting_should_contain_correct_heliosTCP_values_in_ReferenceConf()
        {
            var c = ((RemoteActorRefProvider)sys.Provider).RemoteSettings.Config.GetConfig("akka.remote.helios.tcp");
            var s = new HeliosTransportSettings(c);

            Assert.AreEqual(TimeSpan.FromSeconds(15), s.ConnectTimeout);
            Assert.IsNull(s.WriteBufferHighWaterMark);
            Assert.IsNull(s.WriteBufferLowWaterMark);
            Assert.AreEqual(256000, s.SendBufferSize.Value);
            Assert.AreEqual(256000, s.ReceiveBufferSize.Value);
            Assert.AreEqual(128000, s.MaxFrameSize);
            Assert.AreEqual(4096, s.Backlog);
            Assert.IsTrue(s.TcpNoDelay);
            Assert.IsTrue(s.TcpKeepAlive);
            Assert.IsTrue(s.TcpReuseAddr);
            Assert.IsTrue(string.IsNullOrEmpty(c.GetString("hostname")));
            Assert.AreEqual(2, s.ServerSocketWorkerPoolSize);
            Assert.AreEqual(2, s.ClientSocketWorkerPoolSize);
        }

        [TestMethod]
        public void Remoting_should_contain_correct_socket_worker_pool_configuration_values_in_ReferenceConf()
        {
            var c = ((RemoteActorRefProvider)sys.Provider).RemoteSettings.Config.GetConfig("akka.remote.helios.tcp");

            // server-socket-worker-pool
            {
                var pool = c.GetConfig("server-socket-worker-pool");
                Assert.AreEqual(2, pool.GetInt("pool-size-min"));
                Assert.AreEqual(1.0d, pool.GetDouble("pool-size-factor"));
                Assert.AreEqual(2, pool.GetInt("pool-size-max"));
            }

            //client-socket-worker-pool
            {
                var pool = c.GetConfig("client-socket-worker-pool");
                Assert.AreEqual(2, pool.GetInt("pool-size-min"));
                Assert.AreEqual(1.0d, pool.GetDouble("pool-size-factor"));
                Assert.AreEqual(2, pool.GetInt("pool-size-max"));
            }
        }
    }
}
