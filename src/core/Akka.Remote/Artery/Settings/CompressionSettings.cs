using Akka.Configuration;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class CompressionSettings
    {
        public bool Debug => false;
        public bool Enabled => ActorRefs.Max > 0 || Manifests.Max > 0;
        public ActorRefsSettings ActorRefs { get; }
        public ManifestsSettings Manifests { get; }

        public CompressionSettings(Config compressionConfig)
        {
            if (compressionConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression");

            ActorRefs = new ActorRefsSettings(compressionConfig.GetConfig("actor-refs"));
            Manifests = new ManifestsSettings(compressionConfig.GetConfig("manifests"));
        }
    }
}
