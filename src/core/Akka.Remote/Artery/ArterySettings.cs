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
