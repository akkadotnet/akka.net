//-----------------------------------------------------------------------
// <copyright file="MpProtocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using global::MessagePack;

namespace Akka.Remote.Transport.Pipelines.MessagePack
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Tag constants used as the leading discriminator byte of every MessagePack
    /// protocol frame produced by <see cref="AkkaPduMessagePackCodec"/>.
    ///
    /// <para>
    /// Conceptually equivalent to a MessagePack <c>[Union]</c> discriminator, but the
    /// codec writes the tag as a single raw byte at offset <c>0</c> and the tail of the
    /// buffer is interpreted per-tag. This avoids the array/map header MessagePack would
    /// otherwise add for a polymorphic envelope, and — crucially — lets the
    /// <see cref="Payload"/> tag carry the inner <see cref="MpAckAndEnvelope"/> bytes
    /// verbatim with zero double-encoding overhead.
    /// </para>
    ///
    /// Wire layout per tag:
    /// <list type="bullet">
    ///   <item><see cref="Payload"/>: <c>[tag][raw inner MpAckAndEnvelope MessagePack bytes…]</c></item>
    ///   <item><see cref="Heartbeat"/>, <see cref="Disassociate"/>,
    ///         <see cref="DisassociateQuarantined"/>, <see cref="DisassociateShuttingDown"/>:
    ///         <c>[tag]</c> only — single byte on the wire.</item>
    ///   <item><see cref="Associate"/>: <c>[tag][MessagePack-serialized MpHandshakeInfo]</c></item>
    /// </list>
    ///
    /// <!-- CopilotNotes: Values mirror the protobuf CommandType enum 1:1 so semantics
    ///      stay identical between codecs even though the wire layouts differ. -->
    /// </summary>
    internal static class ProtocolTag
    {
        /// <summary>A raw payload frame; tail bytes are the inner <see cref="MpAckAndEnvelope"/> verbatim.</summary>
        public const byte Payload = 0;
        /// <summary>Heartbeat — no tail.</summary>
        public const byte Heartbeat = 1;
        /// <summary>Associate handshake — tail is a serialized <see cref="MpHandshakeInfo"/>.</summary>
        public const byte Associate = 2;
        /// <summary>Disassociate (unknown reason) — no tail.</summary>
        public const byte Disassociate = 3;
        /// <summary>Disassociate: remote quarantined this node — no tail.</summary>
        public const byte DisassociateQuarantined = 4;
        /// <summary>Disassociate: remote is shutting down — no tail.</summary>
        public const byte DisassociateShuttingDown = 5;
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Mirror of <c>AkkaHandshakeInfo { origin: AddressData, uid: fixed64 }</c>.
    /// The <c>AddressData</c> fields are inlined to reduce nested allocations.
    /// Serialized as the tail of an <see cref="ProtocolTag.Associate"/> frame.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MpHandshakeInfo
    {
        /// <summary>Akka address protocol string, e.g. <c>"akka.tcp"</c>.</summary>
        [Key(0)] public string Protocol { get; set; } = "";

        /// <summary>Actor system name.</summary>
        [Key(1)] public string System { get; set; } = "";

        /// <summary>Hostname or IP address string.</summary>
        [Key(2)] public string Hostname { get; set; } = "";

        /// <summary>TCP port number.</summary>
        [Key(3)] public int Port { get; set; }

        /// <summary>
        /// System UID. Stored as <c>long</c> for MessagePack efficiency;
        /// the value is cast from <see cref="Akka.Remote.Transport.HandshakeInfo.Uid"/>
        /// which is <c>int</c> on the .NET side.
        /// </summary>
        [Key(4)] public long Uid { get; set; }
    }

    // ── Application-level message envelope ───────────────────────────────────
    // Mirrors AckAndEnvelopeContainer { ack, envelope } + RemoteEnvelope + AcknowledgementInfo.
    //
    // CopilotNotes: For the hot path (Payload frames) these bytes are written *directly*
    // after the 1-byte protocol tag — no double envelope, no MessagePack `bin` length
    // prefix, and decoded with a zero-copy ByteString slice. Very speed, much wow ✨

    /// <summary>
    /// INTERNAL API.
    ///
    /// Mirror of <c>AckAndEnvelopeContainer { ack, envelope }</c>.
    /// Both fields are optional; a pure-ack has no <see cref="Envelope"/> and a
    /// heartbeat-payload frame has no <see cref="Ack"/>.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MpAckAndEnvelope
    {
        [Key(0)] public MpAck? Ack { get; set; }
        [Key(1)] public MpRemoteEnvelope? Envelope { get; set; }
    }

    /// <summary>
    /// INTERNAL API. Mirror of <c>AcknowledgementInfo { cumulativeAck, nacks }</c>.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MpAck
    {
        /// <summary>Mirrors <c>cumulativeAck: fixed64</c>; stored as <c>long</c> (same bit pattern).</summary>
        [Key(0)] public long CumulativeAck { get; set; }

        /// <summary>Selective-NAK sequence numbers; may be null for a pure cumulative ACK.</summary>
        [Key(1)] public long[]? Nacks { get; set; }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Mirror of <c>RemoteEnvelope { recipient, message, sender, seq }</c>.
    /// ActorRef paths are stored as strings (same as <c>ActorRefData.path</c>).
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MpRemoteEnvelope
    {
        /// <summary>Full serialized path of the recipient actor ref (mirrors <c>ActorRefData.path</c>).</summary>
        [Key(0)] public string RecipientPath { get; set; } = "";

        /// <summary>The serialized message payload (mirrors <c>Payload</c> protobuf).</summary>
        [Key(1)] public MpPayload Message { get; set; } = new();

        /// <summary>Full serialized path of the sender. Null / empty when no sender is provided.</summary>
        [Key(2)] public string? SenderPath { get; set; }

        /// <summary>
        /// Reliable-delivery sequence number. <c>ulong.MaxValue</c> (= <see cref="SeqUndefined"/>)
        /// means no sequence number is assigned, matching the protobuf convention.
        /// </summary>
        [Key(3)] public ulong Seq { get; set; } = SeqUndefined;

        /// <summary>Sentinel value meaning "no sequence number" — mirrors <c>ulong.MaxValue</c> in the protobuf codec.</summary>
        public const ulong SeqUndefined = ulong.MaxValue;
    }

    /// <summary>
    /// INTERNAL API. Mirror of the <c>Payload</c> protobuf message.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MpPayload
    {
        /// <summary>The opaque serialized actor message bytes.</summary>
        [Key(0)] public ReadOnlyMemory<byte>? Message { get; set; }

        /// <summary>Akka serializer ID (matches <c>Payload.serializerId</c>).</summary>
        [Key(1)] public int SerializerId { get; set; }

        /// <summary>
        /// Optional type manifest bytes (matches <c>Payload.messageManifest</c>).
        /// Null is used instead of an empty array to save 1 byte on the wire.
        /// </summary>
        [Key(2)] public ReadOnlyMemory<byte>? Manifest { get; set; }
    }
}
