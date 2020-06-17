using System;
using Akka.Configuration;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class ActorRefsSettings
    {
        public TimeSpan AdvertisementInterval { get; }
        public int Max { get; }

        public ActorRefsSettings(Config actorRefConfig)
        {
            if (actorRefConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression.actor-refs");

            AdvertisementInterval = actorRefConfig.GetTimeSpan("advertisement-interval");
            Max = actorRefConfig.GetInt("max");
        }
    }
}
