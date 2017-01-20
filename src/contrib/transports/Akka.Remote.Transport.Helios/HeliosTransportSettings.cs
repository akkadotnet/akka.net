#region copyright
// -----------------------------------------------------------------------
//  <copyright file="HeliosTransportSettings.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Net;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;
using Helios.Logging;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class HeliosTransportSettings
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal readonly Config Config;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public HeliosTransportSettings(Config config)
        {
            Config = config;
            Init();
        }
        static HeliosTransportSettings()
        {
            // Disable STDOUT logging for Helios in release mode
#if !DEBUG
            LoggingFactory.DefaultFactory = new NoOpLoggerFactory();
#endif
        }

        private void Init()
        {
            //TransportMode
            var protocolString = Config.GetString("transport-protocol");
            if (protocolString.Equals("tcp")) TransportMode = new Tcp();
            else if (protocolString.Equals("udp")) TransportMode = new Udp();
            else throw new ConfigurationException(string.Format("Unknown transport transport-protocol='{0}'", protocolString));
            EnableSsl = Config.GetBoolean("enable-ssl");
            ConnectTimeout = Config.GetTimeSpan("connection-timeout");
            WriteBufferHighWaterMark = OptionSize("write-buffer-high-water-mark");
            WriteBufferLowWaterMark = OptionSize("write-buffer-low-water-mark");
            SendBufferSize = OptionSize("send-buffer-size");
            ReceiveBufferSize = OptionSize("receive-buffer-size");
            var size = OptionSize("maximum-frame-size");
            if (size == null || size < 32000) throw new ConfigurationException("Setting 'maximum-frame-size' must be at least 32000 bytes");
            MaxFrameSize = (int)size;
            Backlog = Config.GetInt("backlog");
            TcpNoDelay = Config.GetBoolean("tcp-nodelay");
            TcpKeepAlive = Config.GetBoolean("tcp-keepalive");
            TcpReuseAddr = Config.GetBoolean("tcp-reuse-addr");
            var configHost = Config.GetString("hostname");
            var publicConfigHost = Config.GetString("public-hostname");
            DnsUseIpv6 = Config.GetBoolean("dns-use-ipv6");
            EnforceIpFamily = RuntimeDetector.IsMono || Config.GetBoolean("enforce-ip-family");
            Hostname = string.IsNullOrEmpty(configHost) ? IPAddress.Any.ToString() : configHost;
            PublicHostname = string.IsNullOrEmpty(publicConfigHost) ? Hostname : publicConfigHost;
            ServerSocketWorkerPoolSize = ComputeWps(Config.GetConfig("server-socket-worker-pool"));
            ClientSocketWorkerPoolSize = ComputeWps(Config.GetConfig("client-socket-worker-pool"));
            Port = Config.GetInt("port");

            // used to provide backwards compatibility with Helios 1.* clients
            BackwardsCompatibilityModeEnabled = Config.GetBoolean("enable-backwards-compatibility", false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public TransportMode TransportMode { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool EnableSsl { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan ConnectTimeout { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public long? WriteBufferHighWaterMark { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public long? WriteBufferLowWaterMark { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public long? SendBufferSize { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public long? ReceiveBufferSize { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxFrameSize { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int Backlog { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool TcpNoDelay { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool TcpKeepAlive { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool TcpReuseAddr { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool DnsUseIpv6 { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool EnforceIpFamily { get; private set; }

        /// <summary>
        /// The hostname that this server binds to
        /// </summary>
        public string Hostname { get; private set; }

        /// <summary>
        /// If different from <see cref="Hostname"/>, this is the public "address" that is bound to the <see cref="ActorSystem"/>,
        /// whereas <see cref="Hostname"/> becomes the physical address that the low-level socket connects to.
        /// </summary>
        public string PublicHostname { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int ServerSocketWorkerPoolSize { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int ClientSocketWorkerPoolSize { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool BackwardsCompatibilityModeEnabled { get; private set; }

        #region Internal methods

        private long? OptionSize(string s)
        {
            var bytes = Config.GetByteSize(s);
            if (bytes == null || bytes == 0) return null;
            if (bytes < 0) throw new ConfigurationException(string.Format("Setting {0} must be 0 or positive", s));
            return bytes;
        }

        private int ComputeWps(Config config)
        {
            return ThreadPoolConfig.ScaledPoolSize(config.GetInt("pool-size-min"), config.GetDouble("pool-size-factor"),
                config.GetInt("pool-size-max"));
        }

        #endregion
    }
}