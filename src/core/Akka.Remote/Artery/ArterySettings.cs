//-----------------------------------------------------------------------
// <copyright file="ArterySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Configuration;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Parsed, validated settings for the Artery TCP remoting transport, sourced from the
    /// <c>akka.remote.artery</c> HOCON block (see <c>Akka.Remote/Configuration/Remote.conf</c>
    /// and <c>openspec/changes/artery-tcp-remoting/design.md</c>, "Provider integration").
    ///
    /// <para>
    /// The constructor accepts the <c>akka.remote.artery</c> sub-config directly (i.e. the
    /// caller is expected to have already navigated there via <c>rootConfig.GetConfig("akka.remote.artery")</c>),
    /// mirroring the shape of other per-transport settings types in this assembly (e.g.
    /// <c>DotNettyTransportSettings.Create(Config)</c>, which is likewise handed the
    /// transport-specific sub-config rather than the whole system config).
    /// </para>
    /// </summary>
    internal sealed class ArterySettings
    {
        /// <summary>
        /// Whether the Artery TCP remoting transport is enabled. When <see langword="false"/>
        /// (the default), classic DotNetty remoting is used and every other setting on this
        /// type is irrelevant.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// The configured transport substrate. Only <c>"tcp"</c> is supported for now.
        /// </summary>
        public string Transport { get; }

        /// <summary>
        /// The hostname (or IP) this system advertises for other systems to connect to.
        /// </summary>
        public string CanonicalHostname { get; }

        /// <summary>
        /// The port this system advertises for other systems to connect to.
        /// </summary>
        public int CanonicalPort { get; }

        /// <summary>
        /// Maximum serialized size, in bytes, of any Artery frame (envelope header + payload).
        /// Validated to be greater than zero and no greater than
        /// <see cref="ArteryFrameParser.MaxAllowedFrameLength"/> -- the hard cap that protects
        /// the envelope's 24-bit literal-offset tag space (see design.md, "Envelope wire layout").
        /// </summary>
        public int MaximumFrameSize { get; }

        /// <summary>
        /// Number of inbound lanes ordinary-stream messages are fanned out across for parallel
        /// deserialization/dispatch. Parsed and validated now but NOT used until the lanes work
        /// lands at gate G5 -- see design.md's milestone table.
        /// </summary>
        public int InboundLanes { get; }

        /// <summary>
        /// Number of outbound connections (lanes) used to send ordinary-stream messages. Parsed
        /// and validated now but NOT used until gate G5; see <see cref="InboundLanes"/>.
        /// </summary>
        public int OutboundLanes { get; }

        /// <summary>
        /// How long an association will wait for a handshake to complete before failing and
        /// retrying.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; }

        /// <summary>
        /// How often a pending outbound handshake request is retried while awaiting a response.
        /// </summary>
        public TimeSpan HandshakeRetryInterval { get; }

        /// <summary>
        /// How often the outbound handshake stage injects a <c>HandshakeReq</c> while user
        /// traffic is queued behind an in-progress handshake.
        /// </summary>
        public TimeSpan InjectHandshakeInterval { get; }

        /// <summary>
        /// How often the control stream sends a liveness <c>ArteryHeartbeat</c> while otherwise
        /// idle (task group 6, "Control Stream" -- task 6.4). See <see cref="ArteryHeartbeatStage"/>.
        /// </summary>
        public TimeSpan ControlHeartbeatInterval { get; }

        /// <summary>
        /// Maximum number of unacknowledged system-message envelopes <see cref="SystemMessageDeliveryStage"/>
        /// buffers for possible resend before giving up (and quarantining the association) --
        /// design.md gate G3, "Reliable system-message delivery". Matches Pekko's default.
        /// </summary>
        public int SystemMessageBufferSize { get; }

        /// <summary>
        /// How often <see cref="SystemMessageDeliveryStage"/> resends its whole unacknowledged
        /// window while any entry remains unacked.
        /// </summary>
        public TimeSpan SystemMessageResendInterval { get; }

        /// <summary>
        /// How long the OLDEST unacknowledged system-message envelope may wait before
        /// <see cref="SystemMessageDeliveryStage"/> gives up and quarantines the association.
        ///
        /// <para>
        /// <b>Default divergence from classic remoting (deliberate design.md open decision, now
        /// DECIDED).</b> Classic Akka.NET's analogous <c>akka.remote.retry-gate-closed-for</c>-style
        /// timeouts are on the order of ~3 minutes; Pekko's Artery <c>give-up-system-message-after</c>
        /// defaults to 6 HOURS. This port takes Pekko's 6h default deliberately: system-message
        /// delivery backs DeathWatch, and giving up (and thereby losing the ability to ever detect a
        /// remote actor's termination for this association) after only ~3 minutes of network trouble
        /// would be far too eager -- classic's short timeout is a documented historical wart, not a
        /// property to preserve. Tests shrink this value explicitly (design.md's G3 correctness
        /// suite -- "give-up-timeout -&gt; quarantine (shrink the timeout in test config)").
        /// </para>
        /// </summary>
        public TimeSpan GiveUpSystemMessageAfter { get; }

        /// <summary>
        /// How long to wait before re-materializing an association's outbound stream (ordinary
        /// or control, independently) after it terminates for any reason other than this
        /// system's own transport <c>Shutdown()</c> -- design.md group 9, "Association
        /// outbound-stream lifecycle: reconnect". Restarts are unlimited at this fixed backoff;
        /// there is deliberately no restart-count give-up (see design.md's rationale).
        /// </summary>
        public TimeSpan OutboundRestartBackoff { get; }

        /// <summary>
        /// The largest compression-table index that fits the envelope's 16-bit COMPRESSED tag, and
        /// therefore the hard upper bound on <see cref="CompressionActorRefsMax"/> /
        /// <see cref="CompressionManifestsMax"/> (see design.md "Risks / edge cases -- Index space").
        /// </summary>
        public const int MaxCompressionTableSize = (int)ArteryEnvelopeHeader.CompressedIndexMask; // 65535

        /// <summary>
        /// Whether Artery actor-ref/manifest compression is enabled (design.md
        /// "artery-ref-manifest-compression"). Off by default; when off, no compression table is ever
        /// advertised, the encoder emits only LITERAL tags, and the wire is byte-identical to a build
        /// without compression. This flag gates the whole feature.
        ///
        /// <para>
        /// Parsed now (following the "parse now, use later" precedent of <see cref="InboundLanes"/>);
        /// the advertisement protocol + live encode/decode wiring land in later stages.
        /// </para>
        /// </summary>
        public bool CompressionEnabled { get; }

        /// <summary>
        /// Maximum number of actor paths retained in a single advertised actor-ref compression table
        /// (the receiver's top heavy hitters). Default 256; <c>off</c> (parsed as <c>0</c>) disables
        /// actor-ref compression. Validated to <c>0..<see cref="MaxCompressionTableSize"/></c> so every
        /// index fits the 16-bit COMPRESSED tag.
        /// </summary>
        public int CompressionActorRefsMax { get; }

        /// <summary>
        /// Maximum number of class manifests retained in a single advertised manifest compression
        /// table. Default 256; <c>off</c> (parsed as <c>0</c>) disables manifest compression. Same
        /// <c>0..<see cref="MaxCompressionTableSize"/></c> validation as <see cref="CompressionActorRefsMax"/>.
        /// </summary>
        public int CompressionManifestsMax { get; }

        /// <summary>
        /// How often a receiver rebuilds and re-advertises its compression tables to each origin.
        /// Default 1 minute (matches Pekko). Parsed now, used once the advertisement protocol lands.
        /// </summary>
        public TimeSpan CompressionAdvertisementInterval { get; }

        /// <summary>
        /// Selects the frequency-sketch implementation used to detect heavy hitters (design.md
        /// Decision 6). Only the MVP <c>bounded</c> count-based sketch exists today; Pekko's aging
        /// <c>fast-frequency-sketch</c> is deferred behind the <c>IFrequencySketch</c> seam. Parsed now,
        /// used once the inbound-compression state machine lands.
        /// </summary>
        public string CompressionFrequencySketchImplementation { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArterySettings"/> class from the
        /// <c>akka.remote.artery</c> sub-config.
        /// </summary>
        /// <param name="arteryConfig">
        /// The <c>akka.remote.artery</c> HOCON sub-config (NOT the system root config).
        /// </param>
        /// <exception cref="ConfigurationException">
        /// <paramref name="arteryConfig"/> is null/empty, or one of the settings fails validation
        /// (unsupported transport, out-of-range frame size, out-of-range lane count, or a
        /// non-positive timeout).
        /// </exception>
        public ArterySettings(Config arteryConfig)
        {
            if (arteryConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ArterySettings>("akka.remote.artery");

            Enabled = arteryConfig.GetBoolean("enabled");

            Transport = arteryConfig.GetString("transport", "tcp");
            if (Transport != "tcp")
                throw new ConfigurationException(
                    $"Unsupported akka.remote.artery.transport [{Transport}]. Only \"tcp\" is supported at this time.");

            CanonicalHostname = arteryConfig.GetString("canonical.hostname", "localhost");
            CanonicalPort = arteryConfig.GetInt("canonical.port", 25520);

            var maximumFrameSize = arteryConfig.GetByteSize("advanced.maximum-frame-size", 256 * 1024) ?? 256 * 1024;
            if (maximumFrameSize <= 0 || maximumFrameSize > ArteryFrameParser.MaxAllowedFrameLength)
                throw new ConfigurationException(
                    "akka.remote.artery.advanced.maximum-frame-size must be greater than 0 and no greater than " +
                    $"{ArteryFrameParser.MaxAllowedFrameLength} (0x00FFFFFF) so that Artery envelope literal " +
                    $"offsets stay within their 24-bit tag space, but was [{maximumFrameSize}].");
            MaximumFrameSize = (int)maximumFrameSize;

            InboundLanes = arteryConfig.GetInt("advanced.inbound-lanes", 4);
            if (InboundLanes < 1)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.inbound-lanes must be >= 1, but was [{InboundLanes}].");

            OutboundLanes = arteryConfig.GetInt("advanced.outbound-lanes", 1);
            if (OutboundLanes < 1)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.outbound-lanes must be >= 1, but was [{OutboundLanes}].");

            HandshakeTimeout = GetPositiveTimeSpan(arteryConfig, "advanced.handshake-timeout", TimeSpan.FromSeconds(20));
            HandshakeRetryInterval = GetPositiveTimeSpan(arteryConfig, "advanced.handshake-retry-interval", TimeSpan.FromSeconds(1));
            InjectHandshakeInterval = GetPositiveTimeSpan(arteryConfig, "advanced.inject-handshake-interval", TimeSpan.FromSeconds(1));
            ControlHeartbeatInterval = GetPositiveTimeSpan(arteryConfig, "advanced.control-heartbeat-interval", TimeSpan.FromSeconds(5));

            SystemMessageBufferSize = arteryConfig.GetInt("advanced.system-message-buffer-size", 20_000);
            if (SystemMessageBufferSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.system-message-buffer-size must be greater than 0, but was [{SystemMessageBufferSize}].");

            SystemMessageResendInterval = GetPositiveTimeSpan(arteryConfig, "advanced.system-message-resend-interval", TimeSpan.FromSeconds(1));
            GiveUpSystemMessageAfter = GetPositiveTimeSpan(arteryConfig, "advanced.give-up-system-message-after", TimeSpan.FromHours(6));

            OutboundRestartBackoff = GetPositiveTimeSpan(arteryConfig, "advanced.outbound-restart-backoff", TimeSpan.FromSeconds(1));

            // Actor-ref / manifest compression (design.md "artery-ref-manifest-compression").
            // "Parse now, use later" -- Enabled gates the whole feature; when off, the maxes/interval
            // are irrelevant but still validated so a bad value is caught at startup, not first-use.
            CompressionEnabled = arteryConfig.GetBoolean("advanced.compression.enabled");
            CompressionActorRefsMax = GetCompressionMax(arteryConfig, "advanced.compression.actor-refs.max", 256);
            CompressionManifestsMax = GetCompressionMax(arteryConfig, "advanced.compression.manifests.max", 256);
            CompressionAdvertisementInterval = GetPositiveTimeSpan(arteryConfig, "advanced.compression.advertisement-interval", TimeSpan.FromMinutes(1));
            CompressionFrequencySketchImplementation = arteryConfig.GetString("advanced.compression.frequency-sketch-implementation", "bounded");
        }

        /// <summary>
        /// Parses a compression-table size cap: an <c>off</c>/<c>false</c> value disables that
        /// category (returned as <c>0</c>); any other value must be a non-negative integer no greater
        /// than <see cref="MaxCompressionTableSize"/> (so every dense index fits the 16-bit tag).
        /// </summary>
        private static int GetCompressionMax(Config config, string path, int @default)
        {
            var raw = config.GetString(path, @default.ToString(System.Globalization.CultureInfo.InvariantCulture));

            int max;
            if (string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                max = 0;
            }
            else if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out max))
            {
                throw new ConfigurationException(
                    $"akka.remote.artery.{path} must be a non-negative integer or \"off\", but was [{raw}].");
            }

            if (max < 0 || max > MaxCompressionTableSize)
                throw new ConfigurationException(
                    $"akka.remote.artery.{path} must be between 0 and {MaxCompressionTableSize} (the 16-bit compression-tag index space), but was [{max}].");

            return max;
        }

        private static TimeSpan GetPositiveTimeSpan(Config config, string path, TimeSpan @default)
        {
            var value = config.GetTimeSpan(path, @default);
            if (value <= TimeSpan.Zero)
                throw new ConfigurationException(
                    $"akka.remote.artery.{path} must be greater than zero, but was [{value}].");

            return value;
        }
    }
}
