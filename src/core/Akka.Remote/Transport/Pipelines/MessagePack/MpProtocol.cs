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
    /// Tag constants for <see cref="MpProtocolFrame.Tag"/>, mirroring the
    /// <c>CommandType</c> enum in <c>WireFormats.proto</c>.
    /// </summary>
    internal static class ProtocolTag
    {
        // CopilotNotes: These map 1:1 to the protobuf CommandType enum values so the
        // semantics are identical regardless of which codec is used on each hop.
        
        /// <summary>A raw payload frame (wraps serialized <c>AckAndEnvelopeContainer</c> bytes).</summary>
        public const byte Payload = 0;
        /// <summary>Heartbeat — no additional payload.</summary>
        public const byte Heartbeat = 1;
        /// <summary>Associate handshake — <see cref="MpProtocolFrame.HandshakeInfo"/> must be populated.</summary>
        public const byte Associate = 2;
        /// <summary>Disassociate (unknown reason).</summary>
        public const byte Disassociate = 3;
        /// <summary>Disassociate: remote quarantined this node.</summary>
        public const byte DisassociateQuarantined = 4;
        /// <summary>Disassociate: remote is shutting down.</summary>
        public const byte DisassociateShuttingDown = 5;
    }

    // ── Protocol-level outer envelope ────────────────────────────────────────
    // Mirrors AkkaProtocolMessage { payload | instruction } + AkkaControlMessage + AkkaHandshakeInfo.
    // We flatten into a single tagged struct to avoid an extra allocation for the control path.

    /// <summary>
    /// INTERNAL API.
    ///
    /// Outer protocol-level frame, equivalent to the protobuf <c>AkkaProtocolMessage</c>
    /// union. Uses a single <see cref="Tag"/> discriminant instead of a protobuf
    /// <c>oneof</c> field selection.
    ///
    /// <!-- CopilotNotes: Keeping this a single flat type avoids the union-boxing overhead
    ///      of MessagePack's [Union] attribute approach. The Tag byte is in Key(0) so
    ///      reading it costs only 1 byte before we decide what else to deserialize.
    ///      AllowPrivate = true is required because the type is internal and the
    ///      source generator needs access to non-public members. -->
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MpProtocolFrame
    {
        /// <summary>One of the <see cref="ProtocolTag"/> constants.</summary>
        [Key(0)] public byte Tag { get; set; }

        /// <summary>Serialized inner <see cref="MpAckAndEnvelope"/> bytes. Non-null when <see cref="Tag"/> == <see cref="ProtocolTag.Payload"/>.</summary>
        [Key(1)] public byte[]? Payload { get; set; }

        /// <summary>Handshake origin info. Non-null when <see cref="Tag"/> == <see cref="ProtocolTag.Associate"/>.</summary>
        [Key(2)] public MpHandshakeInfo? HandshakeInfo { get; set; }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Mirror of <c>AkkaHandshakeInfo { origin: AddressData, uid: fixed64 }</c>.
    /// The <c>AddressData</c> fields are inlined to reduce nested allocations.
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
        [Key(2)] public byte[]? Manifest { get; set; }
    }
}







