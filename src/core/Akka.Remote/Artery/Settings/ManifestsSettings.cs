using System;
using Akka.Configuration;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class ManifestsSettings
    {
        public TimeSpan AdvertisementInterval { get; }
        public int Max { get; }

        public ManifestsSettings(Config manifestConfig)
        {
            if (manifestConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression.manifests");

            AdvertisementInterval = manifestConfig.GetTimeSpan("advertisement-interval");
            Max = manifestConfig.GetInt("max");
        }

    }
}
