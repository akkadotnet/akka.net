using System;
using Akka.Configuration;

namespace Akka.Remote.Artery.Settings
{
    internal class ActorRefsSettings
    {
        public TimeSpan AdvertisementInterval { get; }
        public int Max { get; }

        public ActorRefsSettings(Config config)
        {
            var actorRefConfig = config.GetConfig("actor-refs");
            if (actorRefConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression.actor-refs");

            AdvertisementInterval = actorRefConfig.GetTimeSpan("advertisement-interval");
            Max = actorRefConfig.GetInt("max");
        }
    }
}
