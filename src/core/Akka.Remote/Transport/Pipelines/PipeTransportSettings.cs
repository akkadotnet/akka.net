//-----------------------------------------------------------------------
// <copyright file="PipeTransportSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty; // SslSettings lives here; same assembly so internal access is fine

namespace Akka.Remote.Transport.Pipelines
{
    /// <summary>
    /// Selects which PDU envelope codec the Pipelines transport uses for
    /// the <c>AkkaProtocol</c> wire layer.
    ///
    /// <!-- CopilotNotes: Only switch to MessagePack when every node in the cluster
    ///      is running the PipeTransport with this setting — mixed clusters will throw
    ///      PduCodecException when trying to decode the incorrect wire format. -->
    /// </summary>
    internal enum EnvelopeCodecKind
    {
        /// <summary>
        /// Default — the same protobuf-based <c>AkkaPduProtobuffCodec</c> used by DotNetty.
        /// Wire-compatible with all existing Akka.Remote nodes.
        /// </summary>
        Protobuf,

        /// <summary>
        /// Opt-in — source-generated MessagePack codec.  Smaller frames, lower GC pressure,
        /// but <b>not</b> wire-compatible with protobuf nodes.
        /// </summary>
        MessagePack
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Configuration for <see cref="TcpPipeTransport"/>, parsed from the
    /// <c>akka.remote.pipe.tcp</c> HOCON block.
    ///
    /// <!-- CopilotNotes: Sealed class (not record) so per-property XML docs work cleanly in C# 12.
    ///      The factory method pattern mirrors DotNettyTransportSettings.Create(). -->
    /// </summary>
    internal sealed class PipeTransportSettings
    {
        // ── Minimum frame size guard mirrors the DotNetty transport constraint ─
        private const int MinFrameSize = 32_000;

        // ── Properties ─────────────────────────────────────────────────────────

        /// <summary>The hostname or IP address to bind to (empty → 0.0.0.0).</summary>
        public string Hostname { get; }

        /// <summary>Public-facing hostname advertised via Akka addresses (e.g. for NAT / Docker).</summary>
        public string PublicHostname { get; }

        /// <summary>TCP port to listen on. 0 = random.</summary>
        public int Port { get; }

        /// <summary>Public port to advertise (<c>null</c> = use <see cref="Port"/>).</summary>
        public int? PublicPort { get; }

        /// <summary>Enable TLS/SSL on this transport.</summary>
        public bool EnableSsl { get; }

        /// <summary>Timeout for outbound TCP connect attempts.</summary>
        public TimeSpan ConnectTimeout { get; }

        /// <summary>Maximum allowed frame payload size in bytes. Must be ≥ 32 000.</summary>
        public int MaxFrameSize { get; }

        /// <summary>Socket <c>SO_SNDBUF</c> in bytes.</summary>
        public int SendBufferSize { get; }

        /// <summary>Socket <c>SO_RCVBUF</c> in bytes.</summary>
        public int ReceiveBufferSize { get; }

        /// <summary>Server listen backlog (passed to <c>Socket.Listen</c>).</summary>
        public int Backlog { get; }

        /// <summary>Enable TCP keepalive probes.</summary>
        public bool TcpKeepAlive { get; }

        /// <summary>Disable Nagle's algorithm (<c>TCP_NODELAY</c>) for lower latency.</summary>
        public bool TcpNoDelay { get; }

        /// <summary>Prefer IPv6 when resolving hostnames via DNS.</summary>
        public bool DnsUseIpv6 { get; }

        /// <summary>
        /// Bounded capacity of the per-connection outbound write channel.
        /// When full, <see cref="AssociationHandle.Write"/> returns <c>false</c>
        /// (matching DotNetty water-mark semantics: write was dropped, no duplicate).
        /// </summary>
        public int WriteChannelCapacity { get; }

        /// <summary>SSL/TLS settings. Only meaningful when <see cref="EnableSsl"/> is <c>true</c>.</summary>
        public SslSettings Ssl { get; }

        /// <summary>
        /// Selects which PDU codec is used for the AkkaProtocol wire layer.
        /// Defaults to <see cref="EnvelopeCodecKind.Protobuf"/> for wire-compatibility
        /// with DotNetty nodes.
        /// </summary>
        public EnvelopeCodecKind EnvelopeCodec { get; }

        // ── Constructor ────────────────────────────────────────────────────────

        private PipeTransportSettings(
            string hostname, string publicHostname, int port, int? publicPort,
            bool enableSsl, TimeSpan connectTimeout, int maxFrameSize,
            int sendBufferSize, int receiveBufferSize, int backlog,
            bool tcpKeepAlive, bool tcpNoDelay, bool dnsUseIpv6,
            int writeChannelCapacity, SslSettings ssl,
            EnvelopeCodecKind envelopeCodec)
        {
            Hostname             = hostname;
            PublicHostname       = publicHostname;
            Port                 = port;
            PublicPort           = publicPort;
            EnableSsl            = enableSsl;
            ConnectTimeout       = connectTimeout;
            MaxFrameSize         = maxFrameSize;
            SendBufferSize       = sendBufferSize;
            ReceiveBufferSize    = receiveBufferSize;
            Backlog              = backlog;
            TcpKeepAlive         = tcpKeepAlive;
            TcpNoDelay           = tcpNoDelay;
            DnsUseIpv6           = dnsUseIpv6;
            WriteChannelCapacity = writeChannelCapacity;
            Ssl                  = ssl;
            EnvelopeCodec        = envelopeCodec;
        }

        // ── Factory ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parse settings from the provided <paramref name="config"/> block
        /// (expected to be the resolved <c>akka.remote.pipe.tcp</c> sub-config).
        /// </summary>
        /// <exception cref="ConfigurationException">
        /// Thrown when the config block is null or empty, or <c>maximum-frame-size</c>
        /// is below the minimum.
        /// </exception>
        public static PipeTransportSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<PipeTransportSettings>("akka.remote.pipe.tcp");

            var host = config.GetString("hostname", "");
            if (string.IsNullOrWhiteSpace(host))
                host = IPAddress.Any.ToString();

            var publicHost = config.GetString("public-hostname", "");
            var enableSsl  = config.GetBoolean("enable-ssl");
            var publicPort = config.GetInt("public-port");
            var maxFrame   = (int)(config.GetByteSize("maximum-frame-size", null) ?? 128_000L);

            if (maxFrame < MinFrameSize)
                throw new ArgumentException(
                    $"akka.remote.pipe.tcp.maximum-frame-size must be at least {MinFrameSize} bytes",
                    nameof(maxFrame));

            var envelopeStr = config.GetString("envelope", "protobuf");
            var envelopeCodec = string.Equals(envelopeStr, "messagepack", StringComparison.OrdinalIgnoreCase)
                ? EnvelopeCodecKind.MessagePack
                : EnvelopeCodecKind.Protobuf;

            return new PipeTransportSettings(
                hostname:            host,
                publicHostname:      !string.IsNullOrEmpty(publicHost) ? publicHost : host,
                port:                config.GetInt("port", 2552),
                publicPort:          publicPort > 0 ? publicPort : null,
                enableSsl:           enableSsl,
                connectTimeout:      config.GetTimeSpan("connection-timeout", TimeSpan.FromSeconds(15)),
                maxFrameSize:        maxFrame,
                sendBufferSize:      (int)(config.GetByteSize("send-buffer-size",    null) ?? 256_000L),
                receiveBufferSize:   (int)(config.GetByteSize("receive-buffer-size", null) ?? 256_000L),
                backlog:             config.GetInt("backlog", 4096),
                tcpKeepAlive:        config.GetBoolean("tcp-keepalive", true),
                tcpNoDelay:          config.GetBoolean("tcp-nodelay",   true),
                dnsUseIpv6:          config.GetBoolean("dns-use-ipv6",  false),
                writeChannelCapacity: config.GetInt("write-channel-capacity", 1024),
                ssl: enableSsl
                    ? SslSettings.Create(config.GetConfig("ssl"))
                    : SslSettings.Empty,
                envelopeCodec: envelopeCodec
            );
        }

        /// <summary>
        /// Creates the <see cref="AkkaPduCodec"/> appropriate for the current
        /// <paramref name="remoteConfig"/> (<c>akka.remote</c> block).
        ///
        /// <para>
        /// When <c>akka.remote.pipe.tcp</c> is in <c>enabled-transports</c> and its
        /// <c>envelope</c> key is <c>"messagepack"</c>, returns an
        /// <see cref="AkkaPduMessagePackCodec"/>; otherwise returns the default
        /// <see cref="AkkaPduProtobuffCodec"/>.
        /// </para>
        ///
        /// <!-- CopilotNotes: Called from EndpointManager and AkkaProtocolManager to
        ///      ensure the same codec is used consistently for both the protocol-level
        ///      (handshake/heartbeat/disassociate) and message-level (AckAndEnvelope) frames. -->
        /// </summary>
        /// <param name="remoteConfig">The resolved <c>akka.remote</c> config block.</param>
        /// <param name="system">The hosting actor system.</param>
        /// <returns>The most appropriate <see cref="AkkaPduCodec"/> instance.</returns>
        public static AkkaPduCodec CreateCodec(Config remoteConfig, ActorSystem system)
        {
            IList<string>? enabledTransports = null;
            try { enabledTransports = remoteConfig.GetStringList("enabled-transports"); }
            catch (Exception) { /* config key absent — fall back to protobuf */ }

            if (enabledTransports != null
                && enabledTransports.Contains("akka.remote.pipe.tcp"))
            {
                var pipeConfig = remoteConfig.GetConfig("pipe.tcp");
                if (!pipeConfig.IsNullOrEmpty())
                {
                    var envelope = pipeConfig.GetString("envelope", "protobuf");
                    if (string.Equals(envelope, "messagepack", StringComparison.OrdinalIgnoreCase))
                        return new AkkaPduMessagePackCodec(system);
                }
            }

            return new AkkaPduProtobuffCodec(system);
        }
    }
}
