using System;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Remote.Artery.Settings
{
    internal class TcpSettings
    {
        public TimeSpan ConnectionTimeout { get; }
        public string OutboundClientHostname { get; }

        public TcpSettings(Config config)
        {
            var tcpConfig = config.GetConfig("tcp");
            if (tcpConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<TcpSettings>("akka.remote.artery.advanced.tcp");

            ConnectionTimeout = tcpConfig
                .GetTimeSpan("connection-timeout")
                .Requiring(interval => interval > TimeSpan.Zero, "connection-timeout must be more than zero");

            OutboundClientHostname = tcpConfig.GetString("outbound-client-hostname");
        }
    }
}
