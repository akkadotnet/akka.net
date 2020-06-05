using Akka.Configuration;
using Akka.Util;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class CanonicalSettings
    {
        public int Port { get; }
        public string Hostname { get; }

        public CanonicalSettings(Config config)
        {
            var canonicalConfig = config.GetConfig("canonical");
            if (canonicalConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CanonicalSettings>("akka.remote.artery.canonical");

            Port = canonicalConfig.GetInt("port").Requiring(p => p >= 0 && p <= 65535, "canonical.port must be 0 through 65535");
            Hostname = canonicalConfig.GetHostname("hostname");
        }
    }
}
