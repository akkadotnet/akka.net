using System;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class BindSettings
    {
        public int Port { get; }
        public string Hostname { get; }
        public TimeSpan BindTimeout { get; }

        public BindSettings(Config bindConfig, CanonicalSettings canonical)
        {
            if (bindConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<BindSettings>("akka.remote.artery.bind");

            Port = string.IsNullOrEmpty(bindConfig.GetString("port")) ?
                canonical.Port :
                bindConfig.GetInt("port")
                    .Requiring(p => p >= 0 && p <= 65535, "bind.port must be 0 through 65535");

            Hostname = bindConfig.GetHostname("hostname");
            Hostname = string.IsNullOrEmpty(Hostname) ? canonical.Hostname : Hostname;

            BindTimeout = bindConfig.GetTimeSpan("bind-timeout")
                .Requiring(b => b > TimeSpan.Zero, "bind-timeout must be greater than 0");
        }
    }
}
