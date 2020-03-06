//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Remote
{
    /// <summary>
    /// Setings for the AkkaProtocolTransport
    /// </summary>
    public class AkkaProtocolSettings
    {
        /// <summary>
        /// The HOCON for the failure detector.
        /// </summary>
        public Config TransportFailureDetectorConfig { get; private set; }

        /// <summary>
        /// The failure detection implementation class FQN.
        /// </summary>
        public string TransportFailureDetectorImplementationClass { get; private set; }

        /// <summary>
        /// The heartbeat interval used to keep the protocol transport alive.
        /// </summary>
        public TimeSpan TransportHeartBeatInterval { get; private set; }

        /// <summary>
        /// The heartbeat handshake timeout.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; private set; }

        /// <summary>
        /// Creates a new AkkaProtocolSettings instance.
        /// </summary>
        /// <param name="config">The HOCON configuration.</param>
        public AkkaProtocolSettings(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<AkkaProtocolSettings>();

            TransportFailureDetectorConfig = config.GetConfig("akka.remote.transport-failure-detector");
            TransportFailureDetectorImplementationClass = TransportFailureDetectorConfig.GetString("implementation-class", null);
            TransportHeartBeatInterval = TransportFailureDetectorConfig.GetTimeSpan("heartbeat-interval", null);

            // backwards compatibility with the existing dot-netty.tcp.connection-timeout
            var enabledTransports = config.GetStringList("akka.remote.enabled-transports", new string[] { });
            if (enabledTransports.Contains("akka.remote.dot-netty.tcp"))
                HandshakeTimeout = config.GetTimeSpan("akka.remote.dot-netty.tcp.connection-timeout", null);
            else if (enabledTransports.Contains("akka.remote.dot-netty.ssl"))
                HandshakeTimeout = config.GetTimeSpan("akka.remote.dot-netty.ssl.connection-timeout", null);
            else
                HandshakeTimeout = config.GetTimeSpan("akka.remote.handshake-timeout", TimeSpan.FromSeconds(20), allowInfinite:false);
        }
    }
}

