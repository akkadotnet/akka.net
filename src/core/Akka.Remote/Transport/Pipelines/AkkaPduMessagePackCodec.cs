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
    /// <c>akka.remote.pipe.tcp.envelope = messagepack</c>.  Mixed-codec clusters will
    /// produce <see cref="PduCodecException"/> on decode.
    /// </para>
    ///
    /// <para>
    /// Wire format summary:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Protocol frame</b> — a MessagePack-serialized <see cref="MpProtocolFrame"/>
    ///     with a 1-byte <c>Tag</c> discriminant, optional <c>Payload</c> bytes, and
    ///     optional <c>HandshakeInfo</c>.  Mirrors <c>AkkaProtocolMessage</c>.
    ///   </item>
    ///   <item>
    ///     <b>Message envelope</b> — a MessagePack-serialized <see cref="MpAckAndEnvelope"/>
    ///     containing optional <see cref="MpAck"/> and <see cref="MpRemoteEnvelope"/>.
    ///     Mirrors <c>AckAndEnvelopeContainer</c>.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <!-- CopilotNotes: The codec intentionally avoids ByteString allocations for the
    ///      static control messages (heartbeat, disassociate) by caching their serialised
    ///      bytes as static readonly fields computed once in the static constructor. -->
    /// </summary>
    internal sealed class AkkaPduMessagePackCodec : AkkaPduCodec
    {
        // ── Static cache for allocation-free control messages ─────────────────

        // CopilotNotes: These are safe as static bytes because no ActorSystem state is
        // encoded in heartbeat/disassociate frames — same reasoning as HeartbeatPdu in
        // AkkaPduProtobuffCodec.
        private static readonly byte[] s_heartbeatBytes;
        private static readonly byte[] s_disassociateBytes;
        private static readonly byte[] s_disassociateQuarantinedBytes;
        private static readonly byte[] s_disassociateShuttingDownBytes;

        static AkkaPduMessagePackCodec()
        {
            s_heartbeatBytes               = SerializeFrame(new MpProtocolFrame { Tag = ProtocolTag.Heartbeat });
            s_disassociateBytes            = SerializeFrame(new MpProtocolFrame { Tag = ProtocolTag.Disassociate });
            s_disassociateQuarantinedBytes = SerializeFrame(new MpProtocolFrame { Tag = ProtocolTag.DisassociateQuarantined });
            s_disassociateShuttingDownBytes= SerializeFrame(new MpProtocolFrame { Tag = ProtocolTag.DisassociateShuttingDown });
        }

        // ── Constructor ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new instance of <see cref="AkkaPduMessagePackCodec"/>.
        /// </summary>
        /// <param name="system">The hosting actor system (for path caching).</param>
        public AkkaPduMessagePackCodec(ActorSystem system) : base(system) { }

        // ── Protocol-level encode / decode ─────────────────────────────────────

        /// <inheritdoc/>
        /// <summary>
        /// Decodes a MessagePack-serialized <see cref="MpProtocolFrame"/> from <paramref name="raw"/>
        /// and returns the corresponding <see cref="IAkkaPdu"/> variant.
        /// </summary>
        /// <exception cref="PduCodecException">
        /// Thrown when the bytes cannot be deserialized or the tag is unrecognised.
        /// </exception>
        public override IAkkaPdu DecodePdu(ByteString raw)
        {
            try
            {
                var frame = MP.MessagePackSerializer.Deserialize<MpProtocolFrame>(raw.Memory);
                return frame.Tag switch
                {
                    ProtocolTag.Payload =>
                        new Payload(frame.Payload is { Length: > 0 }
                            ? ByteString.CopyFrom(frame.Payload.Value.Span)
                            : ByteString.Empty),

                    ProtocolTag.Heartbeat => new Heartbeat(),

                    ProtocolTag.Associate => DecodeAssociate(frame),

                    ProtocolTag.Disassociate            => new Disassociate(DisassociateInfo.Unknown),
                    ProtocolTag.DisassociateQuarantined => new Disassociate(DisassociateInfo.Quarantined),
                    ProtocolTag.DisassociateShuttingDown=> new Disassociate(DisassociateInfo.Shutdown),

                    _ => throw new PduCodecException(
                        $"Unknown MessagePack protocol tag: {frame.Tag}. " +
                        "Ensure all cluster nodes use the same envelope codec.")
                };
            }
            catch (MP.MessagePackSerializationException ex)
            {
                throw new PduCodecException("Failed to deserialize MessagePack protocol frame.", ex);
            }
        }

        /// <inheritdoc/>
        /// <summary>
        /// Wraps <paramref name="payload"/> (the serialized <c>AckAndEnvelopeContainer</c> bytes)
        /// in an <see cref="MpProtocolFrame"/> with <see cref="ProtocolTag.Payload"/>.
        /// </summary>
        public override ByteString ConstructPayload(ByteString payload)
        {
            var frame = new MpProtocolFrame
            {
                Tag     = ProtocolTag.Payload,
                Payload = payload.ToByteArray()
            };
            return ByteString.CopyFrom(SerializeFrame(frame));
        }

        public override ByteString ConstructPayload(ReadOnlyMemory<byte> payload)
        {
            var frame = new MpProtocolFrame
            {
                Tag     = ProtocolTag.Payload,
                Payload = payload
            };
            return ByteString.CopyFrom(SerializeFrame(frame));
        }

        /// <inheritdoc/>
        public override ByteString ConstructAssociate(HandshakeInfo info)
        {
            if (string.IsNullOrEmpty(info.Origin.Host) || !info.Origin.Port.HasValue)
                throw new ArgumentException(
                    $"HandshakeInfo origin {info.Origin} is missing host or port.", nameof(info));

            var frame = new MpProtocolFrame
            {
                Tag = ProtocolTag.Associate,
                HandshakeInfo = new MpHandshakeInfo
                {
                    Protocol = info.Origin.Protocol,
                    System   = info.Origin.System,
                    Hostname = info.Origin.Host!,
                    Port     = info.Origin.Port!.Value,
                    Uid      = info.Uid // int → long widening
                }
            };
            return ByteString.CopyFrom(SerializeFrame(frame));
        }

        /// <inheritdoc/>
        public override ByteString ConstructDisassociate(DisassociateInfo reason)
        {
            var bytes = reason switch
            {
                DisassociateInfo.Quarantined => s_disassociateQuarantinedBytes,
                DisassociateInfo.Shutdown    => s_disassociateShuttingDownBytes,
                _                            => s_disassociateBytes
            };
            // CopilotNotes: CopyFrom required because ByteString must own the backing array.
            return ByteString.CopyFrom(bytes);
        }

        /// <inheritdoc/>
        public override ByteString ConstructHeartbeat() =>
            ByteString.CopyFrom(s_heartbeatBytes);

        // ── Message-level encode / decode ──────────────────────────────────────

        /// <inheritdoc/>
        /// <summary>
        /// Deserializes a MessagePack <see cref="MpAckAndEnvelope"/> and reconstructs an
        /// <see cref="AckAndMessage"/>, bridging into the protobuf-based upper-layer types
        /// (<see cref="SerializedMessage"/> etc.) so <c>EndpointWriter</c> / <c>EndpointReader</c>
        /// remain unchanged.
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

                    // Reconstruct the protobuf Payload (SerializedMessage) from the MessagePack fields.
                    var serializedMessage = new SerializedMessage
                    {
                        Message         = env.Message.Message is { Length: > 0 }
                            ? ByteString.CopyFrom(env.Message.Message.Value.Span)
                            : ByteString.Empty,
                        SerializerId    = env.Message.SerializerId,
                        MessageManifest = env.Message.Manifest is { Length: > 0 }
                            ? ByteString.CopyFrom(env.Message.Manifest)
                            : ByteString.Empty
                    };

                    messageOption = new Message(
                        recipient, recipientAddress, serializedMessage,
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

        private static byte[] SerializeFrame(MpProtocolFrame frame) =>
            MP.MessagePackSerializer.Serialize(frame);

        private static Associate DecodeAssociate(MpProtocolFrame frame)
        {
            if (frame.HandshakeInfo is not { } hi)
                throw new PduCodecException(
                    "Associate MessagePack frame is missing HandshakeInfo.");

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
                Message      = msg.Message.IsEmpty      ? null : msg.Message.ToByteArray(),
                SerializerId = msg.SerializerId,
                Manifest     = msg.MessageManifest.IsEmpty ? null : msg.MessageManifest.ToByteArray()
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

