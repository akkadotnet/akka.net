//-----------------------------------------------------------------------
// <copyright file="ArterySettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using Akka.Configuration;
using Akka.Util;

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
        /// Capacity of every association's bounded ORDINARY outbound queue (see
        /// <see cref="Akka.Remote.Artery.Association.DefaultOutboundQueueCapacity"/>). Matches
        /// Pekko's <c>outbound-message-queue-size</c> default; left unchanged from the original
        /// G2 constant.
        /// </summary>
        public int OutboundMessageQueueSize { get; }

        /// <summary>
        /// Capacity of every association's bounded CONTROL outbound queue (see
        /// <see cref="Akka.Remote.Artery.Association.DefaultControlQueueCapacity"/>). Matches
        /// Pekko's <c>outbound-control-queue-size</c> default -- widened from this port's original
        /// (undersized) 256 constant, which caused spurious quarantines under a mass-termination
        /// <c>Unwatch</c> burst against an otherwise healthy peer.
        /// </summary>
        public int OutboundControlQueueSize { get; }

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
        /// Resume-writer threshold, in bytes, for the input <see cref="System.IO.Pipelines.Pipe"/>
        /// of every Artery TCP connection (pause-writer threshold is twice this value) -- see
        /// <see cref="Akka.IO.Inet.SO.PipeBufferSize"/>. Defaults to 1 MiB, mirroring the 1 MiB OS
        /// socket buffers Artery already pins (see <c>ArteryRemoting.BuildArterySocketOptions</c>); the
        /// much smaller Akka.IO-wide default (derived from <c>akka.io.tcp.receive-buffer-size</c>,
        /// 8 KiB) throttles the read pump well below what Artery's sockets can sustain.
        /// </summary>
        public int TcpPipeBufferSize { get; }

        /// <summary>
        /// Actor-path patterns (<c>akka.remote.artery.large-message-destinations</c>), each
        /// matched against a send's recipient <c>ActorPath.Elements</c> to decide whether a
        /// message rides the dedicated LARGE-MESSAGE stream (streamId 3) instead of the ordinary
        /// stream -- Pekko-faithful (<c>Association.largeMessageDestinations</c> /
        /// <c>ArteryTransport.largeMessageChannelEnabled</c>). Parsed with
        /// <see cref="Akka.Util.WildcardIndex{T}"/> -- the SAME matcher <see cref="Akka.Actor.Deployer"/>
        /// already uses for actor-deployment path patterns, reused here rather than reimplemented
        /// -- supporting both a single "*" (matches any one name at that segment) and a trailing
        /// "**" (matches any name AT OR BELOW that segment), exactly like Pekko's own
        /// <c>WildcardIndex</c>/<c>WildcardTree</c>.
        ///
        /// <para>
        /// <b>No ordering guarantee vs. ordinary traffic (Pekko-documented; see <see cref="ArteryStreamId.Large"/>).</b>
        /// A large-stream message and an ordinary-stream message sent to the SAME recipient ride
        /// two entirely independent connections/queues, so there is no relative ordering
        /// guarantee between them.
        /// </para>
        /// </summary>
        public WildcardIndex<NotUsed> LargeMessageDestinations { get; }

        /// <summary>
        /// Whether the large-message stream is enabled at all: <see langword="true"/> only when
        /// <see cref="LargeMessageDestinations"/> is non-empty (i.e.
        /// <c>large-message-destinations</c> configures at least one pattern) -- mirrors Pekko's
        /// <c>ArteryTransport.largeMessageChannelEnabled</c> exactly. When
        /// <see langword="false"/> (the default), no large-message outbound queue/connection is
        /// ever materialized for any association and every send is routed exactly as it was
        /// before this feature existed (task 10.2 gate L: default-off behavior is unchanged).
        /// </summary>
        public bool LargeMessageChannelEnabled { get; }

        /// <summary>
        /// Maximum serialized size, in bytes, of a frame sent on the LARGE-MESSAGE stream --
        /// analogous to <see cref="MaximumFrameSize"/> but only enforced for connections whose
        /// preamble declares <see cref="ArteryStreamId.Large"/>. Validated the same way as
        /// <see cref="MaximumFrameSize"/> (greater than 0, no greater than
        /// <see cref="ArteryFrameParser.MaxAllowedFrameLength"/>) -- deliberately NOT also
        /// enforcing Pekko's additional "&gt;= 32 KiB" floor for this setting, to stay consistent
        /// with <see cref="MaximumFrameSize"/>'s own (already more permissive) validation in this
        /// port rather than introduce an asymmetric rule between the two sibling settings.
        /// </summary>
        public int MaximumLargeFrameSize { get; }

        /// <summary>
        /// Size of the dedicated <see cref="System.Buffers.ArrayPool{T}"/> the large-message
        /// stream's outbound <see cref="ArteryEncodeStage"/> rents its encode buffers from --
        /// mirrors Pekko's <c>large-buffer-pool-size</c> (its <c>EnvelopeBufferPool</c> sizing
        /// knob), adapted to this port's <c>ArrayPool&lt;byte&gt;.Create(maxArrayLength,
        /// maxArraysPerBucket)</c> idiom: <see cref="MaximumLargeFrameSize"/> maps to
        /// <c>maxArrayLength</c> and this maps to <c>maxArraysPerBucket</c> -- see
        /// <c>ArteryRemoting.Start</c>'s large-pool construction. This port's inbound decode path
        /// is zero-copy over the TCP pipe's own memory (no pooled "envelope buffer" to size on
        /// the receive side, unlike Pekko's Aeron-oriented <c>EnvelopeBufferPool</c>), so this
        /// setting only tunes the OUTBOUND encode pool; see design.md task 10.2's report for the
        /// full rationale.
        /// </summary>
        public int LargeBufferPoolSize { get; }

        /// <summary>
        /// Capacity of every association's bounded LARGE-MESSAGE outbound queue -- only ever
        /// materialized when <see cref="LargeMessageChannelEnabled"/> is <see langword="true"/>.
        /// Matches Pekko's <c>outbound-large-message-queue-size</c> default (256). Overflow is a
        /// soft drop (published as <see cref="Akka.Event.Dropped"/> directly to the event stream,
        /// mirroring the ordinary queue's PR #8346 observability), never a quarantine.
        /// </summary>
        public int OutboundLargeMessageQueueSize { get; }

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

            OutboundMessageQueueSize = arteryConfig.GetInt("advanced.outbound-message-queue-size", Association.DefaultOutboundQueueCapacity);
            if (OutboundMessageQueueSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.outbound-message-queue-size must be greater than 0, but was [{OutboundMessageQueueSize}].");

            OutboundControlQueueSize = arteryConfig.GetInt("advanced.outbound-control-queue-size", Association.DefaultControlQueueCapacity);
            if (OutboundControlQueueSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.outbound-control-queue-size must be greater than 0, but was [{OutboundControlQueueSize}].");

            SystemMessageBufferSize = arteryConfig.GetInt("advanced.system-message-buffer-size", 20_000);
            if (SystemMessageBufferSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.system-message-buffer-size must be greater than 0, but was [{SystemMessageBufferSize}].");

            SystemMessageResendInterval = GetPositiveTimeSpan(arteryConfig, "advanced.system-message-resend-interval", TimeSpan.FromSeconds(1));
            GiveUpSystemMessageAfter = GetPositiveTimeSpan(arteryConfig, "advanced.give-up-system-message-after", TimeSpan.FromHours(6));

            OutboundRestartBackoff = GetPositiveTimeSpan(arteryConfig, "advanced.outbound-restart-backoff", TimeSpan.FromSeconds(1));

            var tcpPipeBufferSize = arteryConfig.GetByteSize("advanced.tcp.pipe-buffer-size", 1024 * 1024) ?? 1024 * 1024;
            if (tcpPipeBufferSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.tcp.pipe-buffer-size must be greater than 0, but was [{tcpPipeBufferSize}].");
            TcpPipeBufferSize = (int)tcpPipeBufferSize;

            LargeMessageDestinations = ParseLargeMessageDestinations(arteryConfig);
            LargeMessageChannelEnabled = !LargeMessageDestinations.IsEmpty;

            var maximumLargeFrameSize = arteryConfig.GetByteSize("advanced.maximum-large-frame-size", 2 * 1024 * 1024) ?? 2 * 1024 * 1024;
            if (maximumLargeFrameSize <= 0 || maximumLargeFrameSize > ArteryFrameParser.MaxAllowedFrameLength)
                throw new ConfigurationException(
                    "akka.remote.artery.advanced.maximum-large-frame-size must be greater than 0 and no greater than " +
                    $"{ArteryFrameParser.MaxAllowedFrameLength} (0x00FFFFFF) so that Artery envelope literal " +
                    $"offsets stay within their 24-bit tag space, but was [{maximumLargeFrameSize}].");
            if (maximumLargeFrameSize < MaximumFrameSize)
                throw new ConfigurationException(
                    "akka.remote.artery.advanced.maximum-large-frame-size must be greater than or equal to " +
                    $"akka.remote.artery.advanced.maximum-frame-size [{MaximumFrameSize}], but was [{maximumLargeFrameSize}].");
            MaximumLargeFrameSize = (int)maximumLargeFrameSize;

            LargeBufferPoolSize = arteryConfig.GetInt("advanced.large-buffer-pool-size", 32);
            if (LargeBufferPoolSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.large-buffer-pool-size must be greater than 0, but was [{LargeBufferPoolSize}].");

            OutboundLargeMessageQueueSize = arteryConfig.GetInt("advanced.outbound-large-message-queue-size", Association.DefaultLargeQueueCapacity);
            if (OutboundLargeMessageQueueSize <= 0)
                throw new ConfigurationException(
                    $"akka.remote.artery.advanced.outbound-large-message-queue-size must be greater than 0, but was [{OutboundLargeMessageQueueSize}].");
        }

        /// <summary>
        /// Parses <c>akka.remote.artery.large-message-destinations</c> (a string list of actor
        /// path patterns, e.g. <c>"/user/supervisor/actor/*"</c>) into a
        /// <see cref="WildcardIndex{T}"/>, mirroring Pekko's <c>ArterySettings.LargeMessageDestinations</c>
        /// parsing exactly: each entry is split on <c>/</c> and its leading (empty, from the
        /// leading slash) segment is dropped before inserting the remaining path elements.
        /// </summary>
        private static WildcardIndex<NotUsed> ParseLargeMessageDestinations(Config arteryConfig)
        {
            var index = new WildcardIndex<NotUsed>();
            foreach (var entry in arteryConfig.GetStringList("large-message-destinations", Array.Empty<string>()))
            {
                var elements = entry.Split('/').Skip(1).ToArray();
                if (elements.Length == 0)
                    continue;

                index = index.Insert(elements, NotUsed.Instance);
            }

            return index;
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
