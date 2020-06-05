using System.Collections.Immutable;
using Akka.Configuration;
using Akka.Util;
using Akka.Remote.Artery.Settings;
using Akka.Actor;
using Akka.Streams;

namespace Akka.Remote.Artery
{
    internal sealed class ArterySettings
    {
        private static readonly Config DefaultArteryConfig = 
            ConfigurationFactory.FromResource<ArterySettings>("Akka.Remote.Artery.reference.conf");

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig()
            => DefaultArteryConfig;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static ArterySettings Create(ActorSystem system)
        {
            // need to make sure the default actor materializer settings are available
            system.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());

            // need to make sure the default artery settings are available
            system.Settings.InjectTopLevelFallback(ArterySettings.DefaultConfig());
            var config = system.Settings.Config.GetConfig("akka.remote.artery");

            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ArterySettings>("akka.remote.artery");

            return new ArterySettings(config);
        }

        private Config Config { get; }

        public bool Enabled { get; }
        public CanonicalSettings Canonical { get; }
        public BindSettings Bind { get; }
        public WildcardIndex<NotUsed> LargeMessageDestinations { get; }
        public string SslEngineProviderClassName { get; }
        public bool UntrustedMode { get; }
        public ImmutableHashSet<string> TrustedSelectionPaths { get; }
        public bool LogReceive { get; }
        public bool LogSend { get; }

        public Settings.Transport Transport { get; }

        /// <summary>
        /// Used version of the header format for outbound messages.
        /// To support rolling upgrades this may be a lower version than `ArteryTransport.HighestVersion`,
        /// which is the highest supported version on receiving (decoding) side.
        /// </summary>
        public byte Version { get; }

        public AdvancedSettings Advanced { get; }

        private ArterySettings(Config config)
        {
            Enabled = Config.GetBoolean("enabled");
            Canonical = new CanonicalSettings(Config);
            Bind = new BindSettings(Config, Canonical);

            LargeMessageDestinations = new WildcardIndex<NotUsed>();
            var destinations = Config.GetStringList("large-message-destinations");
            foreach (var entry in destinations)
            {
                var segments = entry.Split('/');
                LargeMessageDestinations.Insert(segments, NotUsed.Instance);
            }

            SslEngineProviderClassName = Config.GetString("ssl.ssl-engine-provider");
            UntrustedMode = Config.GetBoolean("untrusted-mode");
            TrustedSelectionPaths = Config.GetStringList("trusted-selection-paths").ToImmutableHashSet();
            LogReceive = Config.GetBoolean("log-received-messages");
            LogSend = Config.GetBoolean("log-sent-messages");

            Transport = config.GetTransport("transport");

            // ARTERY: ArteryTransport isn't ported yet.
            //Version = ArteryTransport.HighestVersion;

            Advanced = new AdvancedSettings(Config);
        }

        public ArterySettings WithDisabledCompression()
        {
            return new ArterySettings(
                ConfigurationFactory.ParseString(
                    @"akka.remote.artery.advanced.compression {
                        actor-refs.max = 0
                        manifests.max = 0
                    }")
                .WithFallback(Config));
        }
    }
}
