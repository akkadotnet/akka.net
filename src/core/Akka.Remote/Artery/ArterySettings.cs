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
        private readonly Config _config;

        public bool Enabled { get; }
        public CanonicalSettings Canonical { get; }
        public BindSettings Bind { get; }
        internal WildcardIndex<NotUsed> LargeMessageDestinations { get; }
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

        internal ArterySettings(Config config)
        {
            _config = config;

            Enabled = _config.GetBoolean("enabled");
            Canonical = new CanonicalSettings(_config.GetConfig("canonical"));
            Bind = new BindSettings(_config.GetConfig("bind"), Canonical);

            LargeMessageDestinations = new WildcardIndex<NotUsed>();
            var destinations = _config.GetStringList("large-message-destinations");
            foreach (var entry in destinations)
            {
                var segments = entry.Split('/');
                LargeMessageDestinations.Insert(segments, NotUsed.Instance);
            }

            SslEngineProviderClassName = _config.GetString("ssl.ssl-engine-provider");
            UntrustedMode = _config.GetBoolean("untrusted-mode");
            TrustedSelectionPaths = _config.GetStringList("trusted-selection-paths").ToImmutableHashSet();
            LogReceive = _config.GetBoolean("log-received-messages");
            LogSend = _config.GetBoolean("log-sent-messages");

            Transport = _config.GetTransport("transport");

            // ARTERY: ArteryTransport isn't ported yet.
            //Version = ArteryTransport.HighestVersion;

            Advanced = new AdvancedSettings(_config.GetConfig("advanced"));
        }

        public ArterySettings WithDisabledCompression()
        {
            return new ArterySettings(
                ConfigurationFactory.ParseString(
                    @"akka.remote.artery.advanced.compression {
                        actor-refs.max = 0
                        manifests.max = 0
                    }")
                .WithFallback(_config));
        }
    }
}
