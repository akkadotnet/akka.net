using System;
using Akka.Configuration;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class ManifestsSettings
    {
        public TimeSpan AdvertisementInterval { get; }
        public int Max { get; }

        public ManifestsSettings(Config config)
        {
            var actorRefConfig = config.GetConfig("manifests");
            if (actorRefConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression.manifests");

            AdvertisementInterval = actorRefConfig.GetTimeSpan("advertisement-interval");
            Max = actorRefConfig.GetInt("max");
        }

    }
}
