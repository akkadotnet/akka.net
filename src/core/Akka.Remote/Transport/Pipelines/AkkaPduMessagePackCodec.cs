//-----------------------------------------------------------------------
// <copyright file="AkkaPduMessagePackCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using Akka.Actor;
using Akka.Remote.Transport.Pipelines.MessagePack;
using Google.Protobuf;
using MP = global::MessagePack;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Transport.Pipelines
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// MessagePack-based implementation of <see cref="AkkaPduCodec"/> that mirrors
    /// the <c>AkkaProtocolMessage</c> / <c>AckAndEnvelopeContainer</c> protobuf schema
    /// using source-generated (or dynamically emitted) MessagePack formatters.
    ///
    /// <para>
    /// This codec is <b>cluster-wide opt-in</b>: enable it only when every node in the
    /// cluster is running the pipelines transport with
    /// <c>akka.remote.pipe.tcp.envelope = messagepack</c>. Mixed-codec clusters will
    /// produce <see cref="PduCodecException"/> on decode.
    /// </para>
    ///
    /// <para>
    /// Wire format summary:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Protocol frame</b> — a single discriminator byte (<see cref="ProtocolTag"/>)
    ///     followed by tag-specific payload bytes. This replaces the previous
    ///     <c>MpProtocolFrame</c> envelope so that the <see cref="ProtocolTag.Payload"/>
    ///     case can carry the inner <see cref="MpAckAndEnvelope"/> bytes <i>verbatim</i> with
    ///     no extra MessagePack header / length prefix and no extra buffer copy.
    ///   </item>
    ///   <item>
    ///     <b>Message envelope</b> — a MessagePack-serialized <see cref="MpAckAndEnvelope"/>
    ///     containing optional <see cref="MpAck"/> and <see cref="MpRemoteEnvelope"/>.
    ///     Mirrors <c>AckAndEnvelopeContainer</c>.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <!-- CopilotNotes: The codec caches the static control frames (heartbeat,
    ///      disassociate variants) as 1-byte ByteStrings shared across the process.
    ///      The Payload path is fully zero-copy on decode (UnsafeWrap on a slice of
    ///      the inbound buffer) and a single-allocation prefix-and-copy on encode. -->
    /// </summary>
    internal sealed class AkkaPduMessagePackCodec : AkkaPduCodec
    {
        // ── Static cache for allocation-free control frames ─────────────────
        // CopilotNotes: Each cached frame is exactly one byte. We share them as
        // ByteStrings so the write path returns the same instance every time.

        private static readonly ByteString s_heartbeatBytes               = SingleByte(ProtocolTag.Heartbeat);
        private static readonly ByteString s_disassociateBytes            = SingleByte(ProtocolTag.Disassociate);
        private static readonly ByteString s_disassociateQuarantinedBytes = SingleByte(ProtocolTag.DisassociateQuarantined);
        private static readonly ByteString s_disassociateShuttingDownBytes= SingleByte(ProtocolTag.DisassociateShuttingDown);

        // Cached PDU singletons — these types carry no per-instance state on the
        // decode side, so we share them and skip per-frame allocations.
        private static readonly Heartbeat    s_heartbeatPdu                = new();
        private static readonly Disassociate s_disassociateUnknownPdu      = new(DisassociateInfo.Unknown);
        private static readonly Disassociate s_disassociateQuarantinedPdu  = new(DisassociateInfo.Quarantined);
        private static readonly Disassociate s_disassociateShutdownPdu     = new(DisassociateInfo.Shutdown);

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new instance of <see cref="AkkaPduMessagePackCodec"/>.
        /// </summary>
        /// <param name="system">The hosting actor system (for path caching).</param>
        public AkkaPduMessagePackCodec(ActorSystem system) : base(system) { }

        // ── Protocol-level encode / decode ─────────────────────────────────────

        /// <inheritdoc/>
        /// <summary>
        /// Reads the leading discriminator byte and dispatches to the matching PDU.
        /// For <see cref="ProtocolTag.Payload"/> the inner envelope bytes are returned
        /// as a zero-copy <see cref="ByteString"/> slice of <paramref name="raw"/>.
        /// </summary>
        /// <exception cref="PduCodecException">
        /// Thrown when the buffer is empty, the tag is unrecognised, or an Associate
        /// frame fails to deserialize.
        /// </exception>
        public override IAkkaPdu DecodePdu(ByteString raw)
        {
            if (raw.Length == 0)
                throw new PduCodecException("Empty MessagePack PDU frame.");

            var tag = raw.Span[0];
            try
            {
                switch (tag)
                {
                    case ProtocolTag.Payload:
                        // CopilotNotes: zero-copy slice — UnsafeWrap shares the underlying
                        // buffer, no allocation, no memcpy. Inner envelope bytes start at offset 1.
                        return new Payload(UnsafeByteOperations.UnsafeWrap(raw.Memory.Slice(1)));

                    case ProtocolTag.Heartbeat:
                        return s_heartbeatPdu;

                    case ProtocolTag.Associate:
                        return DecodeAssociate(raw.Memory.Slice(1));

                    case ProtocolTag.Disassociate:
                        return s_disassociateUnknownPdu;
                    case ProtocolTag.DisassociateQuarantined:
                        return s_disassociateQuarantinedPdu;
                    case ProtocolTag.DisassociateShuttingDown:
                        return s_disassociateShutdownPdu;

                    default:
                        throw new PduCodecException(
                            $"Unknown MessagePack protocol tag: {tag}. " +
                            "Ensure all cluster nodes use the same envelope codec.");
                }
            }
            catch (MP.MessagePackSerializationException ex)
            {
                throw new PduCodecException("Failed to deserialize MessagePack protocol frame.", ex);
            }
        }

        /// <inheritdoc/>
        /// <summary>
        /// Wraps already-serialized inner-envelope bytes by prepending the
        /// <see cref="ProtocolTag.Payload"/> discriminator byte.
        /// Single allocation, single buffer copy of <paramref name="payload"/>.
        /// </summary>
        public override ByteString ConstructPayload(ByteString payload) =>
            PrependTag(ProtocolTag.Payload, payload.Span);

        /// <inheritdoc cref="ConstructPayload(ByteString)"/>
        public override ByteString ConstructPayload(ReadOnlyMemory<byte> payload) =>
            PrependTag(ProtocolTag.Payload, payload.Span);

        /// <inheritdoc/>
        public override ByteString ConstructAssociate(HandshakeInfo info)
        {
            if (string.IsNullOrEmpty(info.Origin.Host) || !info.Origin.Port.HasValue)
                throw new ArgumentException(
                    $"HandshakeInfo origin {info.Origin} is missing host or port.", nameof(info));

            var hi = new MpHandshakeInfo
            {
                Protocol = info.Origin.Protocol,
                System   = info.Origin.System,
                Hostname = info.Origin.Host!,
                Port     = info.Origin.Port!.Value,
                Uid      = info.Uid // int → long widening
            };

            // CopilotNotes: Could be optimized further with a pooled IBufferWriter that
            // writes the tag byte directly before MessagePack writes the body — but
            // Associate is sent only once per handshake so the simple path is fine.
            var body = MP.MessagePackSerializer.Serialize(hi);
            return PrependTag(ProtocolTag.Associate, body);
        }

        /// <inheritdoc/>
        public override ByteString ConstructDisassociate(DisassociateInfo reason) => reason switch
        {
            DisassociateInfo.Quarantined => s_disassociateQuarantinedBytes,
            DisassociateInfo.Shutdown    => s_disassociateShuttingDownBytes,
            _                            => s_disassociateBytes
        };

        /// <inheritdoc/>
        public override ByteString ConstructHeartbeat() => s_heartbeatBytes;

        // ── Message-level encode / decode ──────────────────────────────────────

        /// <inheritdoc/>
        /// <summary>
        /// Deserializes a MessagePack <see cref="MpAckAndEnvelope"/> and reconstructs an
        /// <see cref="AckAndMessage"/>.
        ///
        /// <para>
        /// Unlike the protobuf codec, this path creates a <see cref="MsgPackSerializedMessage"/>
        /// instead of a protobuf <c>SerializedMessage</c>, avoiding two <c>ByteString.CopyFrom</c>
        /// allocations per inbound message. The <see cref="Message"/> type carries a
        /// <c>HasMsgPackPayload</c> flag so <c>EndpointReader</c> can route to the correct
        /// Dispatch overload.
        /// </para>
        ///
        /// <!-- CopilotNotes: MpPayload.Message / MpPayload.Manifest are already
        ///      ReadOnlyMemory<byte> values whose backing byte[] is owned by the MessagePack
        ///      deserializer's output — safe to hold long-term even inside the reliable-delivery
        ///      receive buffer. -->
        /// </summary>
        public override AckAndMessage DecodeMessage(
            ByteString raw,
            IRemoteActorRefProvider provider,
            Address localAddress)
        {
            try
            {
                var msg = MP.MessagePackSerializer.Deserialize<MpAckAndEnvelope>(raw.Memory);

                // ── ACK half ──────────────────────────────────────────────────
                Ack? ackOption = null;
                if (msg.Ack is { } mpAck)
                {
                    ackOption = new Ack(
                        new SeqNo(mpAck.CumulativeAck),
                        mpAck.Nacks?.Select(n => new SeqNo(n)) ?? Enumerable.Empty<SeqNo>());
                }

                // ── Message half ──────────────────────────────────────────────
                Message? messageOption = null;
                if (msg.Envelope is { } env)
                {
                    var recipient = provider.ResolveActorRefWithLocalAddress(
                        env.RecipientPath, localAddress);

                    // CopilotNotes: Mirrors the ActorPathCache pattern in AkkaPduProtobuffCodec
                    // so we hit the same thread-local path-parse cache.
                    var recipientAddress = ActorPathCache.Cache
                        .GetOrCompute(env.RecipientPath).Address;

                    IActorRef? senderOption = null;
                    if (!string.IsNullOrEmpty(env.SenderPath))
                        senderOption = provider.ResolveActorRefWithLocalAddress(
                            env.SenderPath, localAddress);

                    SeqNo? seqOption = null;
                    if (env.Seq != MpRemoteEnvelope.SeqUndefined)
                    {
                        unchecked { seqOption = new SeqNo((long)env.Seq); }
                    }

                    // CopilotNotes: Build MsgPackSerializedMessage — no ByteString allocation!
                    // ReadOnlyMemory<byte> slices point directly into the MpPayload fields.
                    // Property pattern on a nullable struct unwraps it, so 'm' and 'mf' are
                    // already ReadOnlyMemory<byte> (non-nullable) — no .Value needed.
                    var msgPackPayload = new MsgPackSerializedMessage(
                        bytes:        env.Message.Message  is { Length: > 0 } m  ? m  : ReadOnlyMemory<byte>.Empty,
                        serializerId: env.Message.SerializerId,
                        manifest:     env.Message.Manifest is { Length: > 0 } mf ? mf : ReadOnlyMemory<byte>.Empty);

                    messageOption = new Message(
                        recipient, recipientAddress, msgPackPayload,
                        senderOption, seqOption);
                }

                return new AckAndMessage(ackOption, messageOption);
            }
            catch (MP.MessagePackSerializationException ex)
            {
                throw new PduCodecException(
                    "Failed to deserialize MessagePack AckAndEnvelope.", ex);
            }
        }

        /// <inheritdoc/>
        public override ByteString ConstructMessage(
            Address localAddress,
            IActorRef recipient,
            SerializedMessage serializedMessage,
            IActorRef? senderOption       = null,
            SeqNo? seqOption              = null,
            Ack? ackOption                = null)
        {
            var env = new MpRemoteEnvelope
            {
                RecipientPath = SerializeActorRef(recipient.Path.Address, recipient),
                Message       = BuildMpPayload(serializedMessage),
                Seq           = seqOption.HasValue
                    ? (ulong)seqOption.Value.RawValue
                    : MpRemoteEnvelope.SeqUndefined
            };

            if (senderOption?.Path is not null)
                env.SenderPath = SerializeActorRef(localAddress, senderOption);

            var container = new MpAckAndEnvelope
            {
                Envelope = env,
                Ack      = ackOption is not null ? BuildMpAck(ackOption) : null
            };

            return ByteString.CopyFrom(
                MP.MessagePackSerializer.Serialize(container));
        }

        /// <inheritdoc/>
        public override ByteString ConstructPureAck(Ack ack)
        {
            var container = new MpAckAndEnvelope { Ack = BuildMpAck(ack) };
            return ByteString.CopyFrom(
                MP.MessagePackSerializer.Serialize(container));
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Allocates a single <c>byte[1 + tail.Length]</c>, writes <paramref name="tag"/>
        /// at index 0, copies <paramref name="tail"/> after it, and returns a zero-copy
        /// <see cref="ByteString"/> wrapping the array via <see cref="UnsafeByteOperations.UnsafeWrap(System.ReadOnlyMemory{byte})"/>.
        /// </summary>
        private static ByteString PrependTag(byte tag, ReadOnlySpan<byte> tail)
        {
            var buf = new byte[1 + tail.Length];
            buf[0] = tag;
            tail.CopyTo(buf.AsSpan(1));
            return UnsafeByteOperations.UnsafeWrap(buf);
        }

        /// <summary>Returns a one-byte <see cref="ByteString"/> containing <paramref name="tag"/>.</summary>
        private static ByteString SingleByte(byte tag) =>
            UnsafeByteOperations.UnsafeWrap(new[] { tag });

        private static Associate DecodeAssociate(ReadOnlyMemory<byte> body)
        {
            if (body.IsEmpty)
                throw new PduCodecException(
                    "Associate MessagePack frame is missing HandshakeInfo body.");

            var hi = MP.MessagePackSerializer.Deserialize<MpHandshakeInfo>(body);
            var origin = new Address(hi.Protocol, hi.System, hi.Hostname, hi.Port);
            return new Associate(new HandshakeInfo(origin, (int)hi.Uid));
        }

        private static MpAck BuildMpAck(Ack ack) =>
            new()
            {
                CumulativeAck = ack.CumulativeAck.RawValue,
                Nacks         = ack.Nacks.Select(n => n.RawValue).ToArray()
            };

        private static MpPayload BuildMpPayload(SerializedMessage msg) =>
            new()
            {
                Message      = msg.Message.IsEmpty      ? null : msg.Message.Memory,
                SerializerId = msg.SerializerId,
                Manifest     = msg.MessageManifest.IsEmpty ? null : msg.MessageManifest.Memory
            };

        /// <summary>
        /// Returns the serialized actor ref path as a string.
        /// Uses the full canonical format when the actor has a remote address,
        /// or appends <paramref name="defaultAddress"/> for local actors.
        /// </summary>
        private static string SerializeActorRef(Address defaultAddress, IActorRef actorRef) =>
            !string.IsNullOrEmpty(actorRef.Path.Address.Host)
                ? actorRef.Path.ToSerializationFormat()
                : actorRef.Path.ToSerializationFormatWithAddress(defaultAddress);
    }
}

