using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using Akka.Configuration;
using Akka.Streams;
using Akka.Util;
using System.Threading;
using DotNetty.Codecs.Compression;

namespace Akka.Remote.Artery
{
    internal class ArterySettings
    {
        #region Classes
        public class CanonicalSettings
        {
            public int Port { get; }
            public string Hostname { get; }

            public CanonicalSettings(Config config)
            {
                Port = config.GetInt("port").Requiring(p => p >= 0 && p <= 65535, "canonical.port must be 0 through 65535");
                Hostname = GetHostname(config, "hostname");
            }
        }

        public class BindSettings
        {
            public int Port { get; }
            public string Hostname { get; }
            public TimeSpan BindTimeout { get; }

            public BindSettings(Config config, CanonicalSettings canonical)
            {
                Port = string.IsNullOrEmpty(config.GetString("port")) ? 
                    canonical.Port :
                    config.GetInt("port")
                        .Requiring(p => p >= 0 && p <= 65535, "bind.port must be 0 through 65535");

                Hostname = GetHostname(config, "hostname");
                Hostname = string.IsNullOrEmpty(Hostname) ? canonical.Hostname : Hostname;

                BindTimeout = config.GetTimeSpan("bind-timeout")
                    .Requiring(b => b > TimeSpan.Zero, "bind-timeout must be greater than 0");
            }
        }

        public class AdvancedSettings
        {
            public bool TestMode { get; }
            public string Dispatcher { get; }
            public string ControlStreamDispatcher { get; }
            public ActorMaterializerSettings MaterializerSettings { get; }
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
            public AeronSettings Aeron { get; }
            public TcpSettings Tcp { get; }

            public AdvancedSettings(Config config)
            {
                config = config.GetConfig("advanced");

                TestMode = config.GetBoolean("test-mode");
                Dispatcher = config.GetString("use-dispatcher");
                ControlStreamDispatcher = config.GetString("use-control-stream-dispatcher");
                MaterializerSettings = 
                    ActorMaterializerSettings.Create(config.GetConfig("materializer"))
                    .WithDispatcher(Dispatcher);
                ControlStreamMaterializerSettings =
                    ActorMaterializerSettings.Create(config.GetConfig("materializer"))
                    .WithDispatcher(ControlStreamDispatcher);

                OutboundLanes = config.GetInt("outbound-lanes")
                    .Requiring(i => i > 0, "outbound-lanes must be greater than zero.");
                InboundLanes = config.GetInt("inbound-lanes")
                    .Requiring(i => i > 0, "inbound-lanes must be greater than zero.");
                SysMsgBufferSize = config.GetInt("system-message-buffer-size")
                    .Requiring(i => i > 0, "system-message-buffer-size must be greater than zero.");
                OutboundMessageQueueSize = config.GetInt("outbound-message-queue-size")
                    .Requiring(i => i > 0, "outbound-message-queue-size must be greater than zero.");
                OutboundControlQueueSize = config.GetInt("outbound-control-queue-size")
                    .Requiring(i => i > 0, "outbound-control-queue-size must be greater than zero.");
                OutboundLargeMessageQueueSize = config.GetInt("outbound-large-message-queue-size")
                    .Requiring(i => i > 0, "outbound-large-message-queue-size must be greater than zero.");
                SystemMessageResendInterval = config.GetTimeSpan("system-message-resend-interval")
                    .Requiring(t => t > TimeSpan.Zero, "system-message-resend-interval must be greater than zero.");
                HandshakeTimeout = config.GetTimeSpan("handshake-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "handshake-timeout must be greater than zero.");
                HandshakeRetryInterval = config.GetTimeSpan("handshake-retry-interval")
                    .Requiring(t => t > TimeSpan.Zero, "handshake-retry-interval must be greater than zero.");
                InjectHandshakeInterval = config.GetTimeSpan("inject-handshake-interval")
                    .Requiring(t => t > TimeSpan.Zero, "inject-handshake-interval must be greater than zero.");
                GiveUpSystemMessageAfter = config.GetTimeSpan("give-up-system-message-after")
                    .Requiring(t => t > TimeSpan.Zero, "give-up-system-message-after must be greater than zero.");
                StopIdleOutboundAfter = config.GetTimeSpan("stop-idle-outbound-after")
                    .Requiring(t => t > TimeSpan.Zero, "stop-idle-outbound-after must be greater than zero.");
                QuarantineIdleOutboundAfter = config.GetTimeSpan("quarantine-idle-outbound-after")
                    .Requiring(t => t > TimeSpan.Zero, "quarantine-idle-outbound-after must be greater than zero.");
                StopQuarantinedAfterIdle = config.GetTimeSpan("stop-quarantined-after-idle")
                    .Requiring(t => t > TimeSpan.Zero, "stop-quarantined-after-idle must be greater than zero.");
                RemoveQuarantinedAssociationAfter = config.GetTimeSpan("remove-quarantined-association-after")
                    .Requiring(t => t > TimeSpan.Zero, "remove-quarantined-association-after must be greater than zero.");
                ShutdownFlushTimeout = config.GetTimeSpan("shutdown-flush-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "shutdown-flush-timeout must be greater than zero.");
                InboundRestartTimeout = config.GetTimeSpan("inbound-restart-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "inbound-restart-timeout must be greater than zero.");
                InboundMaxRestarts = config.GetInt("inbound-max-restarts");
                OutboundRestartBackoff = config.GetTimeSpan("outbound-restart-backoff")
                    .Requiring(t => t > TimeSpan.Zero, "outbound-restart-backoff must be greater than zero.");
                OutboundRestartTimeout = config.GetTimeSpan("outbound-restart-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "outbound-restart-timeout must be greater than zero.");
                OutboundMaxRestarts = config.GetInt("outbound-max-restarts");
                Compression = new CompressionSettings(config);

                MaximumFrameSize = Math.Min((int)config.GetByteSize("maximum-frame-size").Value, int.MaxValue)
                    .Requiring(i => i >= 32 * 1024, "maximum-frame-size must be greater than or equal to 32 KiB");
                BufferPoolSize = config.GetInt("buffer-pool-size")
                    .Requiring(i => i > 0, "buffer-pool-size must be greater than zero.");
                InboundHubBufferSize = BufferPoolSize / 2;
                MaximumLargeFrameSize = Math.Min((int)config.GetByteSize("maximum-large-frame-size").Value, int.MaxValue)
                    .Requiring(i => i >= 32 * 1024, "maximum-large-frame-size must be greater than or equal to 32 KiB");
                LargeBufferPoolSize = config.GetInt("large-buffer-pool-size")
                    .Requiring(i => i > 0, "large-buffer-pool-size must be greater than zero.");
                Aeron = new AeronSettings(config.GetConfig("aeron"));
                Tcp = new TcpSettings(config.GetConfig("tcp"));
            }


        }

        public class AeronSettings
        {
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
                LogAeronCounters = config.GetBoolean("log-aeron-counters");
                EmbeddedMediaDriver = config.GetBoolean("embedded-media-driver");
                AeronDirectoryName = config.GetString("aeron-dir")
                    .Requiring(dir => EmbeddedMediaDriver || !string.IsNullOrEmpty(dir), "aeron-dir must be defined when using external media driver");
                DeleteAeronDirectory = config.GetBoolean("delete-aeron-dir");
                IdleCpuLevel = config.GetInt("idle-cpu-level")
                    .Requiring(level => 1 <= level && level <= 10, "idle-cpu-level must be between 1 and 10");
                GiveUpMessageAfter = config.GetTimeSpan("give-up-message-after")
                    .Requiring(t => t > TimeSpan.Zero, "give-up-message-after must be more than zero");
                ClientLivenessTimeout = config.GetTimeSpan("client-liveness-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "client-liveness-timeout must be more than zero");
                PublicationUnblockTimeout = config.GetTimeSpan("publication-unblock-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "publication-unblock-timeout must be more than zero");
                ImageLivenessTimeout = config.GetTimeSpan("image-liveness-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "image-liveness-timeout must be more than zero");
                DriverTimeout = config.GetTimeSpan("driver-timeout")
                    .Requiring(t => t > TimeSpan.Zero, "driver-timeout must be more than zero");
            }
        }

        public class TcpSettings
        {
            // ARTERY: Need implementation
            public TcpSettings(Config config)
            {

            }
        }

        public class CompressionSettings
        {
            // ARTERY: Need implementation
            public CompressionSettings(Config config)
            {

            }
        }

        // ARTERY: Need ITransport implementations
        public interface ITransport
        {
            string ConfigName { get; }
        }


        #endregion

        public Config Config { get; }
        public bool Enabled { get; }
        public CanonicalSettings Canonical { get; }
        public BindSettings Bind { get; }
        public WildcardIndex<NotUsed> LargeMessageDestinations { get; }
        public string SslEngineProviderClassName { get; }
        public bool UntrustedMode { get; }
        public ImmutableHashSet<string> TrustedSelectionPaths { get; }
        public bool LogReceive { get; }
        public bool LogSend { get; }

        public ITransport Transport { get; }

        /// <summary>
        /// Used version of the header format for outbound messages.
        /// To support rolling upgrades this may be a lower version than `ArteryTransport.HighestVersion`,
        /// which is the highest supported version on receiving (decoding) side.
        /// </summary>
        public byte Version { get; }

        public AdvancedSettings Advanced { get; }


        public ArterySettings(Config config)
        {
            Config = config;
            Enabled = Config.GetBoolean("enabled");
            Canonical = new CanonicalSettings(Config.GetConfig("canonical"));
            Bind = new BindSettings(Config.GetConfig("bind"), Canonical);

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

            switch(config.GetString("transport").ToLowerInvariant())
            {
                // ARTERY: figure out a way to best emulate this.
                /*
  val Transport: Transport = toRootLowerCase(getString("transport")) match {
    case AeronUpd.configName => AeronUpd
    case Tcp.configName      => Tcp
    case TlsTcp.configName   => TlsTcp
    case other =>
      throw new IllegalArgumentException(
        s"Unknown transport [$other], possible values: " +
        s""""${AeronUpd.configName}", "${Tcp.configName}", or "${TlsTcp.configName}"""")
  }
                 */
            }

            // ARTERY: ArteryTransport isn't ported yet.
            //Version = ArteryTransport.HighestVersion;

            Advanced = new AdvancedSettings(Config.GetConfig("advanced"));
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

        internal static string GetHostname(Config config, string key)
        {
            var value = config.GetString(key);
            switch (value)
            {
                case "<getHostAddress>":
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                            return ip.ToString();
                    }
                    throw new ConfigurationException("No network adapter with an IPv4 nor IPv6 address found in host machine.");

                case "<getHostName>":
                    return Dns.GetHostName();

                default:
                    return value;
            }
        }

    }
}
