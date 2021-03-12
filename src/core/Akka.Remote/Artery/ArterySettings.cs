using System;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Akka.Configuration;
using Akka.Remote.Artery.Utils;
using Akka.Util;
using Akka.Streams;
using TransportType = Akka.Remote.Artery.ArterySettings.TransportType;

namespace Akka.Remote.Artery
{
    internal sealed class ArterySettings
    {
        #region Static

        #region configuration classes
        internal sealed class AdvancedSettings
        {
            public Config Config { get; }
            public bool TestMode { get; }
            public string Dispatcher { get; }
            public string ControlStreamDispatcher { get; }

            [Obsolete("deprecated")]
            public ActorMaterializerSettings MaterializerSettings { get; }
            [Obsolete("deprecated")]
            public ActorMaterializerSettings ControlStreamMaterializerSettings { get; }

            public int OutboundLanes { get; }
            public int InboundLanes { get; }
            public int SysMsgBufferSize { get; }
            public int OutboundMessageQueueSize { get; }
            public int OutboundControlQueueSize { get; }
            public int OutboundLargeMessageQueueSize { get; }
            public TimeSpan SystemMessageResendInterval { get; }
            public TimeSpan HandshakeTimeout { get; }
            public TimeSpan HandshakeRetryInterval { get; }
            public TimeSpan InjectHandshakeInterval { get; }
            public TimeSpan GiveUpSystemMessageAfter { get; }
            public TimeSpan StopIdleOutboundAfter { get; }
            public TimeSpan QuarantineIdleOutboundAfter { get; }
            public TimeSpan StopQuarantinedAfterIdle { get; }
            public TimeSpan RemoveQuarantinedAssociationAfter { get; }
            public TimeSpan ShutdownFlushTimeout { get; }
            public TimeSpan DeathWatchNotificationFlushTimeout { get; }
            public TimeSpan InboundRestartTimeout { get; }
            public int InboundMaxRestarts { get; }
            public TimeSpan OutboundRestartBackoff { get; }
            public TimeSpan OutboundRestartTimeout { get; }
            public int OutboundMaxRestarts { get; }
            public CompressionSettings Compression { get; }
            public int MaximumFrameSize { get; }
            public int BufferPoolSize { get; }
            public int InboundHubBufferSize { get; }
            public int MaximumLargeFrameSize { get; }
            public int LargeBufferPoolSize { get; }
            // ARTERY This is a lie, we are not planning on using Aeron in our artery implementation. This will be refactored later.
            public AeronSettings Aeron { get; }
            public TcpSettings Tcp { get; }

            public AdvancedSettings(Config config)
            {
                Config = config.GetConfig("advanced");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<AdvancedSettings>("akka.remote.artery.advanced");

                TestMode = Config.GetBoolean("test-mode");
                Dispatcher = Config.GetString("use-dispatcher");
                ControlStreamDispatcher = Config.GetString("use-control-stream-dispatcher");

                MaterializerSettings =
                    ActorMaterializerSettings.Create(Config.GetConfig("materializer"))
                    .WithDispatcher(Dispatcher);
                ControlStreamMaterializerSettings =
                    ActorMaterializerSettings.Create(Config.GetConfig("materializer"))
                    .WithDispatcher(ControlStreamDispatcher);

                OutboundLanes = Config.GetInt("outbound-lanes")
                    .Requiring(i => i > 0, "outbound-lanes must be greater than zero.");
                InboundLanes = Config.GetInt("inbound-lanes")
                    .Requiring(i => i > 0, "inbound-lanes must be greater than zero.");
                SysMsgBufferSize = Config.GetInt("system-message-buffer-size")
                    .Requiring(i => i > 0, "system-message-buffer-size must be greater than zero.");
                OutboundMessageQueueSize = Config.GetInt("outbound-message-queue-size")
                    .Requiring(i => i > 0, "outbound-message-queue-size must be greater than zero.");
                OutboundControlQueueSize = Config.GetInt("outbound-control-queue-size")
                    .Requiring(i => i > 0, "outbound-control-queue-size must be greater than zero.");
                OutboundLargeMessageQueueSize = Config.GetInt("outbound-large-message-queue-size")
                    .Requiring(i => i > 0, "outbound-large-message-queue-size must be greater than zero.");
                SystemMessageResendInterval = Config.GetTimeSpan("system-message-resend-interval")
                    .Requiring(t => t > TimeSpan.Zero, "system-message-resend-interval must be greater than zero.");
                HandshakeTimeout = Config.GetTimeSpan("handshake-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "handshake-timeout must be greater than zero.");
                HandshakeRetryInterval = Config.GetTimeSpan("handshake-retry-interval")
                    .Requiring(t => t > TimeSpan.Zero, "handshake-retry-interval must be greater than zero.");
                InjectHandshakeInterval = Config.GetTimeSpan("inject-handshake-interval")
                    .Requiring(t => t > TimeSpan.Zero, "inject-handshake-interval must be greater than zero.");
                GiveUpSystemMessageAfter = Config.GetTimeSpan("give-up-system-message-after")
                    .Requiring(t => t > TimeSpan.Zero, "give-up-system-message-after must be greater than zero.");
                StopIdleOutboundAfter = Config.GetTimeSpan("stop-idle-outbound-after")
                    .Requiring(t => t > TimeSpan.Zero, "stop-idle-outbound-after must be greater than zero.");
                QuarantineIdleOutboundAfter = Config.GetTimeSpan("quarantine-idle-outbound-after")
                    .Requiring(t => t > TimeSpan.Zero, "quarantine-idle-outbound-after must be greater than zero.");
                StopQuarantinedAfterIdle = Config.GetTimeSpan("stop-quarantined-after-idle")
                    .Requiring(t => t > TimeSpan.Zero, "stop-quarantined-after-idle must be greater than zero.");
                RemoveQuarantinedAssociationAfter = Config.GetTimeSpan("remove-quarantined-association-after")
                    .Requiring(t => t > TimeSpan.Zero, "remove-quarantined-association-after must be greater than zero.");
                ShutdownFlushTimeout = Config.GetTimeSpan("shutdown-flush-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "shutdown-flush-timeout must be greater than zero.");
                DeathWatchNotificationFlushTimeout =
                    Config.GetString("death-watch-notification-flush-timeout") switch
                    {
                        "off" => TimeSpan.Zero,
                        _ => Config.GetTimeSpan("death-watch-notification-flush-timeout")
                            .Requiring(
                                interval => interval > TimeSpan.Zero,
                                "death-watch-notification-flush-timeout must be more than zero, or off")
                    };
                InboundRestartTimeout = Config.GetTimeSpan("inbound-restart-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "inbound-restart-timeout must be greater than zero.");
                InboundMaxRestarts = Config.GetInt("inbound-max-restarts");
                OutboundRestartBackoff = Config.GetTimeSpan("outbound-restart-backoff")
                    .Requiring(t => t > TimeSpan.Zero, "outbound-restart-backoff must be greater than zero.");
                OutboundRestartTimeout = Config.GetTimeSpan("outbound-restart-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "outbound-restart-timeout must be greater than zero.");
                OutboundMaxRestarts = Config.GetInt("outbound-max-restarts");

                Compression = new CompressionSettings(Config);

                MaximumFrameSize = Math.Min((int)Config.GetByteSize("maximum-frame-size").Value, int.MaxValue)
                    .Requiring(i => i >= 32 * 1024, "maximum-frame-size must be greater than or equal to 32 KiB");
                BufferPoolSize = Config.GetInt("buffer-pool-size")
                    .Requiring(i => i > 0, "buffer-pool-size must be greater than zero.");
                InboundHubBufferSize = BufferPoolSize / 2;
                MaximumLargeFrameSize = Math.Min((int)Config.GetByteSize("maximum-large-frame-size").Value, int.MaxValue)
                    .Requiring(i => i >= 32 * 1024, "maximum-large-frame-size must be greater than or equal to 32 KiB");
                LargeBufferPoolSize = Config.GetInt("large-buffer-pool-size")
                    .Requiring(i => i > 0, "large-buffer-pool-size must be greater than zero.");

                Aeron = new AeronSettings(Config);
                Tcp = new TcpSettings(Config);
            }
        }

        // ARTERY This is a lie, we are not planning on using Aeron in our artery implementation. This will be refactored later.
        internal sealed class AeronSettings
        {
            public Config Config { get; }
            public bool LogAeronCounters { get; }
            public bool EmbeddedMediaDriver { get; }
            public string AeronDirectoryName { get; }
            public bool DeleteAeronDirectory { get; }
            public int IdleCpuLevel { get; }
            public TimeSpan GiveUpMessageAfter { get; }
            public TimeSpan ClientLivenessTimeout { get; }
            public TimeSpan PublicationUnblockTimeout { get; }
            public TimeSpan ImageLivenessTimeout { get; }
            public TimeSpan DriverTimeout { get; }

            public AeronSettings(Config config)
            {
                Config = config.GetConfig("aeron");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<AeronSettings>("akka.remote.artery.advanced.aeron");

                LogAeronCounters = Config.GetBoolean("log-aeron-counters");
                EmbeddedMediaDriver = Config.GetBoolean("embedded-media-driver");
                AeronDirectoryName = Config.GetString("aeron-dir")
                    .Requiring(dir => EmbeddedMediaDriver || !string.IsNullOrEmpty(dir), "aeron-dir must be defined when using external media driver");
                DeleteAeronDirectory = Config.GetBoolean("delete-aeron-dir");
                IdleCpuLevel = Config.GetInt("idle-cpu-level")
                    .Requiring(level => 1 <= level && level <= 10, "idle-cpu-level must be between 1 and 10");
                GiveUpMessageAfter = Config.GetTimeSpan("give-up-message-after")
                    .Requiring(t => t > TimeSpan.Zero, "give-up-message-after must be more than zero");
                ClientLivenessTimeout = Config.GetTimeSpan("client-liveness-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "client-liveness-timeout must be more than zero");
                PublicationUnblockTimeout = Config.GetTimeSpan("publication-unblock-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "publication-unblock-timeout must be more than zero");
                ImageLivenessTimeout = Config.GetTimeSpan("image-liveness-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "image-liveness-timeout must be more than zero");
                DriverTimeout = Config.GetTimeSpan("driver-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "driver-timeout must be more than zero");
            }
        }

        internal sealed class TcpSettings
        {
            public Config Config { get; }
            public TimeSpan ConnectionTimeout { get; }
            public string OutboundClientHostname { get; }

            public TcpSettings(Config config)
            {
                Config = config.GetConfig("tcp");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<TcpSettings>("akka.remote.artery.advanced.tcp");

                ConnectionTimeout = Config
                    .GetTimeSpan("connection-timeout")
                    .Requiring(interval => interval > TimeSpan.Zero, "connection-timeout must be more than zero");

                OutboundClientHostname = Config.GetString("outbound-client-hostname");
            }
        }

        internal sealed class BindSettings
        {
            public Config Config { get; }
            public int Port { get; }
            public string Hostname { get; }
            public TimeSpan BindTimeout { get; }

            public BindSettings(Config config, CanonicalSettings canonical)
            {
                Config = config.GetConfig("bind");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<BindSettings>("akka.remote.artery.bind");

                Port = string.IsNullOrEmpty(Config.GetString("port")) ?
                    canonical.Port :
                    Config.GetInt("port")
                        .Requiring(p => p >= 0 && p <= 65535, "bind.port must be 0 through 65535");

                Hostname = GetHostname(Config, "hostname");
                Hostname = string.IsNullOrEmpty(Hostname) ? canonical.Hostname : Hostname;

                BindTimeout = Config.GetTimeSpan("bind-timeout")
                    .Requiring(b => b > TimeSpan.Zero, "bind-timeout must be greater than 0");
            }
        }

        internal sealed class CanonicalSettings
        {
            public Config Config { get; }
            public int Port { get; }
            public string Hostname { get; }

            public CanonicalSettings(Config config)
            {
                Config = config.GetConfig("canonical");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<CanonicalSettings>("akka.remote.artery.canonical");

                Port = Config.GetInt("port").Requiring(p => p >= 0 && p <= 65535, "canonical.port must be 0 through 65535");
                Hostname = GetHostname(Config, "hostname");
            }
        }

        internal enum TransportType
        {
            AeronUdp,
            Tcp,
            TlsTcp
        }

        internal sealed class CompressionSettings
        {
            public Config Config { get; }
            public bool Enabled => ActorRefs.Enabled || Manifests.Enabled;
            public ActorRefsSettings ActorRefs { get; }
            public ManifestsSettings Manifests { get; }

            public CompressionSettings(Config config)
            {
                Config = config.GetConfig("compression");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression");

                ActorRefs = new ActorRefsSettings(Config);
                Manifests = new ManifestsSettings(Config);
            }
        }

        internal sealed class ActorRefsSettings
        {
            public Config Config { get; }
            public TimeSpan AdvertisementInterval { get; }
            public int Max { get; }
            public bool Enabled => Max > 0;

            public ActorRefsSettings(Config config)
            {
                Config = config.GetConfig("actor-refs");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression.actor-refs");

                AdvertisementInterval = Config.GetTimeSpan("advertisement-interval");
                Max = Config.GetString("max").ToLowerInvariant() switch
                {
                    "off" => 0,
                    _ => Config.GetInt("max")
                };
            }
        }

        internal sealed class ManifestsSettings
        {
            public Config Config { get; }
            public TimeSpan AdvertisementInterval { get; }
            public int Max { get; }
            public bool Enabled => Max > 0;

            public ManifestsSettings(Config config)
            {
                Config = config.GetConfig("manifests");
                if (Config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<CompressionSettings>("akka.remote.artery.advanced.compression.manifests");

                AdvertisementInterval = Config.GetTimeSpan("advertisement-interval");
                Max = Config.GetString("max").ToLowerInvariant() switch
                {
                    "off" => 0,
                    _ => Config.GetInt("max")
                };
            }

        }

        #endregion

        public static ArterySettings Apply(Config config) => new ArterySettings(config);

        #endregion

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

        public IOptionVal<int> LogFrameSizeExceeding { get; }

        public TransportType Transport { get; }

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
            Canonical = new CanonicalSettings(_config);
            Bind = new BindSettings(_config, Canonical);

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

            LogFrameSizeExceeding = _config.GetString("log-frame-size-exceeding").ToLowerInvariant().Equals("off")
                ? OptionVal.None<int>()
                : OptionVal.Some((int)(_config.GetByteSize("log-frame-size-exceeding") ?? 0)); // ARTERY a bit janky, check HOCON

            Transport = _config.GetString("transport").ToLowerInvariant() switch
            {
                "aeron-udp" => throw new ConfigurationException("akka.remote.artery.transport: Aeron transport is not supported yet."),
                "tcp" => TransportType.Tcp,
                "tls-tcp" => TransportType.TlsTcp,
                string transport => throw new ConfigurationException(
                    $"akka.remote.artery.transport: Unknown [{transport}], possible values: \"aeron-udp\", \"tcp\", or \"tls-tcp\""),
            };

            Version = ArteryTransport.HighestVersion;

            Advanced = new AdvancedSettings(_config);
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

        public static TransportType GetTransport(string transport)
            => transport switch
            {
                "aeron-udp" => throw new ConfigurationException("Aeron transport is not supported."),
                "tcp" => TransportType.Tcp,
                "tls-tcp" => TransportType.TlsTcp,
                _ => throw new ConfigurationException(
                    $"Unknown transport [{transport}], possible values: {nameof(TransportType.AeronUdp)}, {nameof(TransportType.Tcp)}, or {nameof(TransportType.TlsTcp)}")
            };

        public static string GetHostname(Config config, string key)
            => GetHostName(config.GetString(key)) ?? throw new ConfigurationException("No network adapter with an IPv4 address found in host machine.");

        public static string GetHostName(string value, bool useIpv4 = true, bool useIpv6 = false)
        {
            return value switch
            {
                "<getHostAddress>" => GetHostAddress(),
                "<getHostName>" => Dns.GetHostName(),
                _ => value
            };

            string GetHostAddress()
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (useIpv4 && ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip.ToString();

                    if (useIpv6 && ip.AddressFamily == AddressFamily.InterNetworkV6)
                        return ip.ToString();
                }
                return null;
            }
        }    }

    #region Static extensions

    internal static class SettingsExtensions
    {
        public static string ToString(this TransportType type)
            => type switch
            {
                TransportType.AeronUdp => "aeron-udp",
                TransportType.Tcp => "tcp",
                TransportType.TlsTcp => "tls-tcp",
                _ => throw new ConfigurationException($"Unknown transport [{type}], possible values: {nameof(TransportType.AeronUdp)}, {nameof(TransportType.Tcp)}, or {nameof(TransportType.TlsTcp)}")
            };
    }

    #endregion
}
