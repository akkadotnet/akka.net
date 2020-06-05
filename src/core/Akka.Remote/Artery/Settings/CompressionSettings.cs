using Akka.Configuration;

namespace Akka.Remote.Artery.Settings
{
    internal class CompressionSettings
    {
        public bool Debug => false;
        public bool Enabled => ActorRefs.Max > 0 || Manifests.Max > 0;
        public ActorRefsSettings ActorRefs { get; }
        public ManifestsSettings Manifests { get; }

        public CompressionSettings(Config config)
        {
            var compressionConfig = config.GetConfig("compression");
            if (compressionConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression");

            ActorRefs = new ActorRefsSettings(compressionConfig);
            Manifests = new ManifestsSettings(compressionConfig);
        }
    }
}
