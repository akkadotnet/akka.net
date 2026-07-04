//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Actor;
using Google.Protobuf;
using System.Runtime.Serialization;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class PduCodecException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PduCodecException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public PduCodecException(string message, Exception cause = null) : base(message, cause) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="PduCodecException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected PduCodecException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /*
     * Interface used to represent Akka PDUs (Protocol Data Unit)
     */
    /// <summary>
    /// TBD
    /// </summary>
    internal interface IAkkaPdu { }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Associate : IAkkaPdu
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        public Associate(HandshakeInfo info)
        {
            Info = info;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public HandshakeInfo Info { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Disassociate : IAkkaPdu
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        public Disassociate(DisassociateInfo reason)
        {
            Reason = reason;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public DisassociateInfo Reason { get; private set; }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Represents a heartbeat on the wire.
    /// </summary>
    internal sealed class Heartbeat : IAkkaPdu { }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Payload : IAkkaPdu
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bytes">TBD</param>
        public Payload(ByteString bytes)
        {
            Bytes = bytes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ByteString Bytes { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class Message : IAkkaPdu, IHasSequenceNumber
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="recipient">TBD</param>
        /// <param name="recipientAddress">TBD</param>
        /// <param name="serializedMessage">TBD</param>
        /// <param name="senderOptional">TBD</param>
        /// <param name="seq">TBD</param>
        public Message(IInternalActorRef recipient, Address recipientAddress, SerializedMessage serializedMessage, IActorRef senderOptional = null, SeqNo? seq = null)
        {
            Seq = seq;
            SenderOptional = senderOptional;
            SerializedMessage = serializedMessage;
            RecipientAddress = recipientAddress;
            Recipient = recipient;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IInternalActorRef Recipient { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Address RecipientAddress { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public SerializedMessage SerializedMessage { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef SenderOptional { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool ReliableDeliveryEnabled { get { return Seq != null; } }

        /// <summary>
        /// The optional sequence number for reliable delivery. Null when reliable delivery is not used.
        /// </summary>
        public SeqNo? Seq { get; private set; }

        /// <inheritdoc/>
        SeqNo IHasSequenceNumber.Seq => Seq!.Value;
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class AckAndMessage
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ackOption">TBD</param>
        /// <param name="messageOption">TBD</param>
        public AckAndMessage(Ack ackOption, Message messageOption)
        {
            MessageOption = messageOption;
            AckOption = ackOption;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Ack AckOption { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Message MessageOption { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// A codec that is able to convert Akka PDUs from and to <see cref="ByteString"/>
    /// </summary>
    internal abstract class AkkaPduCodec
    {
        protected readonly ActorSystem System;
        protected readonly ActorPathThreadLocalCache ActorPathCache;

        protected AkkaPduCodec(ActorSystem system)
        {
            System = system;
            ActorPathCache = ActorPathThreadLocalCache.For(system);
        }

        /// <summary>
        /// Return an <see cref="IAkkaPdu"/> instance that represents a PDU contained in the raw
        /// <see cref="ByteString"/>.
        /// </summary>
        /// <param name="raw">Encoded raw byte representation of an Akka PDU</param>
        /// <returns>Class representation of a PDU.</returns>
        public abstract IAkkaPdu DecodePdu(ByteString raw);

        /// <summary>
        /// Takes an <see cref="IAkkaPdu"/> representation of an Akka PDU and returns its encoded form
        /// as a <see cref="ByteString"/>.
        /// </summary>
        /// <param name="pdu">TBD</param>
        /// <returns>TBD</returns>
        public virtual ByteString EncodePdu(IAkkaPdu pdu)
        {
            switch (pdu)
            {
                case Payload p:
                    return ConstructPayload(p.Bytes);
                case Heartbeat h:
                    return ConstructHeartbeat();
                case Associate a:
                    return ConstructAssociate(a.Info);
                case Disassociate d:
                    return ConstructDisassociate(d.Reason);
                default:
                    return null; // unsupported message type
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructPayload(ByteString payload);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructAssociate(HandshakeInfo info);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructDisassociate(DisassociateInfo reason);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructHeartbeat();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="raw">TBD</param>
        /// <param name="provider">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <returns>TBD</returns>
        public abstract AckAndMessage DecodeMessage(ByteString raw, IRemoteActorRefProvider provider, Address localAddress);

        /// <summary>
        /// INTERNAL API. Lower-allocation inbound decode. Codecs that do not provide a specialized
        /// implementation fall back to <see cref="DecodeMessage(ByteString,IRemoteActorRefProvider,Address)"/>.
        /// </summary>
        public virtual AckAndMessage DecodeMessageFast(ReadOnlySequence<byte> raw, IRemoteActorRefProvider provider, Address localAddress)
        {
            // Default fallback for codecs without a specialized fast path: materialize a ByteString and use the
            // generated decoder. (The concrete AkkaPduProtobuffCodec overrides this with the low-allocation path.)
            return DecodeMessage(ByteString.CopyFrom(raw.ToArray()), provider, localAddress);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="recipient">TBD</param>
        /// <param name="serializedMessage">TBD</param>
        /// <param name="senderOption">TBD</param>
        /// <param name="seqOption">TBD</param>
        /// <param name="ackOption">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructMessage(Address localAddress, IActorRef recipient,
            SerializedMessage serializedMessage, IActorRef senderOption = null, SeqNo? seqOption = null, Ack ackOption = null);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructPureAck(Ack ack);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class AkkaPduProtobuffCodec : AkkaPduCodec
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="raw">TBD</param>
        /// <exception cref="PduCodecException">
        /// This exception is thrown when the Akka PDU in the specified byte string,
        /// <paramref name="raw" />, meets one of the following conditions:
        /// <ul>
        /// <li>The PDU is neither a message or a control message.</li>
        /// <li>The PDU is a control message with an invalid format. </li>
        /// </ul>
        /// </exception>
        /// <returns>TBD</returns>
        public override IAkkaPdu DecodePdu(ByteString raw)
        {
            try
            {
                var pdu = AkkaProtocolMessage.Parser.ParseFrom(raw);
                if (pdu.Instruction != null) return DecodeControlPdu(pdu.Instruction);
                else if (!pdu.Payload.IsEmpty) return new Payload(pdu.Payload); // TODO HasPayload
                else throw new PduCodecException("Error decoding Akka PDU: Neither message nor control message were contained");
            }
            catch (InvalidProtocolBufferException ex)
            {
                throw new PduCodecException("Decoding PDU failed", ex);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public override ByteString ConstructPayload(ByteString payload)
        {
            return new AkkaProtocolMessage() { Payload = payload }.ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="info"/> contains an invalid address.
        /// </exception>
        /// <returns>TBD</returns>
        public override ByteString ConstructAssociate(HandshakeInfo info)
        {
            var handshakeInfo = new AkkaHandshakeInfo()
            {
                Origin = SerializeAddress(info.Origin),
                Uid = (ulong)info.Uid
            };

            return ConstructControlMessagePdu(CommandType.Associate, handshakeInfo);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <returns>TBD</returns>
        public override ByteString ConstructDisassociate(DisassociateInfo reason)
        {
            switch (reason)
            {
                case DisassociateInfo.Quarantined:
                    return DISASSOCIATE_QUARANTINED;
                case DisassociateInfo.Shutdown:
                    return DISASSOCIATE_SHUTTING_DOWN;
                case DisassociateInfo.Unknown:
                default:
                    return DISASSOCIATE;
            }
        }

        /*
         * Since there's never any ActorSystem-specific information coded directly
         * into the heartbeat messages themselves (i.e. no handshake info,) there's no harm in caching in the
         * same heartbeat byte buffer and re-using it.
         */
        private static readonly ByteString HeartbeatPdu = ConstructControlMessagePdu(CommandType.Heartbeat);

        /// <summary>
        /// Creates a new Heartbeat message instance.
        /// </summary>
        /// <returns>The Heartbeat message.</returns>
        public override ByteString ConstructHeartbeat()
        {
            return HeartbeatPdu;
        }

        /// <summary>
        /// Indicated RemoteEnvelope.Seq is not defined (order is irrelevant)
        /// </summary>
        private const ulong SeqUndefined = ulong.MaxValue;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="raw">TBD</param>
        /// <param name="provider">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <returns>TBD</returns>
        public override AckAndMessage DecodeMessage(ByteString raw, IRemoteActorRefProvider provider, Address localAddress)
        {
            var ackAndEnvelope = AckAndEnvelopeContainer.Parser.ParseFrom(raw);

            Ack ackOption = null;

            if (ackAndEnvelope.Ack != null)
            {
                ackOption = new Ack(new SeqNo((long)ackAndEnvelope.Ack.CumulativeAck), ackAndEnvelope.Ack.Nacks.Select(x => new SeqNo((long)x)));
            }

            Message messageOption = null;

            if (ackAndEnvelope.Envelope != null)
            {
                var envelopeContainer = ackAndEnvelope.Envelope;
                if (envelopeContainer != null)
                {
                    var recipient = provider.ResolveActorRefWithLocalAddress(envelopeContainer.Recipient.Path, localAddress);

                    // RecipientAddress is functionally consumed by DefaultMessageDispatcher.Dispatch only
                    // for NON-local (remote-deployed) recipients (the Transport.Addresses.Contains check).
                    // In that case the resolved ref is a RemoteActorRef whose Path IS the parsed wire path,
                    // so recipient.Path.Address is byte-identical to the value the previous
                    // ActorPathCache.GetOrCompute(path).Address produced — but WITHOUT a second FastHash +
                    // path-cache probe per inbound message. For local recipients the field is unused by the
                    // dispatcher (the only observable delta is the address printed in an obscure
                    // dropped-message error log for a local ref that is neither ILocalRef nor Repointable).
                    var recipientAddress = recipient.Path.Address;
                    
                    var serializedMessage = envelopeContainer.Message;
                    IActorRef senderOption = null;
                    if (envelopeContainer.Sender != null)
                        senderOption = provider.ResolveActorRefWithLocalAddress(envelopeContainer.Sender.Path, localAddress);
                    
                    SeqNo? seqOption = null;
                    if (envelopeContainer.Seq != SeqUndefined)
                    {
                        unchecked
                        {
                            seqOption = new SeqNo((long)envelopeContainer.Seq); //proto takes a ulong
                        }
                    }

                    messageOption = new Message(recipient, recipientAddress, serializedMessage, senderOption, seqOption);
                }
            }


            return new AckAndMessage(ackOption, messageOption);
        }

        // ===========================================================================================
        //  Hand-rolled tag-dispatch decode of AckAndEnvelopeContainer (Phase-2 lever B2a).
        //
        //  Produces results IDENTICAL to DecodeMessage(...) (the generated-protobuf path, kept as the
        //  differential-test oracle) but WITHOUT allocating the AckAndEnvelopeContainer / RemoteEnvelope /
        //  ActorRefData wrapper objects. The inner message payload (the SerializedMessage/Payload bytes) is
        //  extracted as an opaque byte range and parsed with the SAME generated parser, so message
        //  (de)serialization is unchanged.
        //
        //  CRITICAL (wire interop is the whole point): protobuf fields may arrive in ANY order with
        //  unknown fields interspersed (a different/older/newer encoder, JVM Akka). So this is a real
        //  tag-dispatch loop with unknown-field skipping — NOT positional. Wire tags = (field<<3)|wireType:
        //    AckAndEnvelopeContainer: ack=1 (msg, tag 10), envelope=2 (msg, tag 18)
        //    RemoteEnvelope:          recipient=1 (msg, 10), message=2 (msg, 18), sender=4 (msg, 34),
        //                             seq=5 (fixed64, tag 41)
        //    ActorRefData:            path=1 (string, tag 10)
        //    AcknowledgementInfo:     cumulativeAck=1 (fixed64, tag 9), nacks=2 (fixed64; tag 17 unpacked,
        //                             tag 18 packed)
        // ===========================================================================================
        private const int WireVarint = 0;
        private const int WireFixed64 = 1;
        private const int WireLengthDelimited = 2;
        private const int WireFixed32 = 5;

        // ===========================================================================================
        //  Byte-keyed inbound actor-ref resolve cache (Phase-2 lever B2b).
        //
        //  Inbound recipient/sender paths repeat massively per association (same handful of actors),
        //  so caching the resolved IInternalActorRef keyed by the raw UTF-8 path BYTES lets the hot path
        //  skip String materialization + Utf8 transcode + FastHash(UTF-16) + the string-keyed resolve/path
        //  caches entirely on a hit. Also subsumes lever B1: the remote SENDER ref (which the codec
        //  otherwise re-creates via CreateRemoteRef => new RemoteActorRef every message) is cached here.
        //
        //  Correctness: the codec is created per association (one per EndpointWriter, line ~919 in
        //  Endpoint.cs) and decode runs only on that association's single EndpointReader actor, so
        //  localAddress is constant and access is single-threaded. The cache is nonetheless made
        //  lock-free + race-SAFE as defense-in-depth: each slot holds an IMMUTABLE entry published/read
        //  via Volatile, so any unexpected concurrent decode can only miss or read a self-consistent
        //  entry — never tear. Every hit is validated by localAddress equality + full path-byte equality,
        //  so a hash collision or a localAddress change can only cause a (correct) miss, never a wrong ref.
        // ===========================================================================================
        private const int RefCacheSize = 256; // power of two
        private readonly RefCacheEntry[] _refCache = new RefCacheEntry[RefCacheSize];

        private sealed class RefCacheEntry
        {
            public readonly byte[] PathBytes;
            public readonly Address LocalAddress;
            public readonly IInternalActorRef Ref;

            public RefCacheEntry(byte[] pathBytes, Address localAddress, IInternalActorRef @ref)
            {
                PathBytes = pathBytes;
                LocalAddress = localAddress;
                Ref = @ref;
            }
        }

        private IInternalActorRef ResolveCached(ReadOnlySequence<byte> pathBytes, Address localAddress, IRemoteActorRefProvider provider)
        {
            // Single-segment (the common case) avoids any copy; multi-segment paths (split across TCP
            // chunks) are materialized contiguously for hashing + comparison.
            ReadOnlySpan<byte> span;
            byte[] contiguous = null;
            if (pathBytes.IsSingleSegment)
            {
                span = pathBytes.FirstSpan;
            }
            else
            {
                contiguous = pathBytes.ToArray();
                span = contiguous;
            }

            var slot = (int)(Fnv1a(span) & (RefCacheSize - 1));
            var entry = Volatile.Read(ref _refCache[slot]);
            if (entry != null && entry.LocalAddress.Equals(localAddress) && span.SequenceEqual(entry.PathBytes))
                return entry.Ref;

            // Miss: materialize the path string and resolve through the canonical (correct) provider path,
            // which keeps populating the existing string-keyed caches too.
            var path = Encoding.UTF8.GetString(span);
            var resolved = provider.ResolveActorRefWithLocalAddress(path, localAddress);
            var key = contiguous ?? span.ToArray();
            Volatile.Write(ref _refCache[slot], new RefCacheEntry(key, localAddress, resolved));
            return resolved;
        }

        private static uint Fnv1a(ReadOnlySpan<byte> data)
        {
            uint hash = 2166136261u;
            for (var i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= 16777619u;
            }

            return hash;
        }

        public override AckAndMessage DecodeMessageFast(ReadOnlySequence<byte> raw, IRemoteActorRefProvider provider, Address localAddress)
        {
            Ack ackOption = null;
            Message messageOption = null;

            var reader = new SequenceReader<byte>(raw);
            while (!reader.End)
            {
                if (!TryReadVarint32(ref reader, out var tag))
                    throw new PduCodecException("Decoding PDU failed: truncated tag in AckAndEnvelopeContainer");

                switch (tag)
                {
                    case 10: // ack = AcknowledgementInfo
                        ackOption = ParseAck(ReadLengthDelimited(raw, ref reader));
                        break;
                    case 18: // envelope = RemoteEnvelope
                        messageOption = ParseEnvelope(ReadLengthDelimited(raw, ref reader), provider, localAddress);
                        break;
                    default:
                        SkipField(raw, ref reader, tag);
                        break;
                }
            }

            return new AckAndMessage(ackOption, messageOption);
        }

        private Message ParseEnvelope(ReadOnlySequence<byte> envelope, IRemoteActorRefProvider provider, Address localAddress)
        {
            bool hasRecipient = false;
            ReadOnlySequence<byte> recipientPathBytes = default;
            bool hasSender = false;
            ReadOnlySequence<byte> senderPathBytes = default;
            ReadOnlySequence<byte> messageBytes = default;
            bool hasMessage = false;
            // proto3 default for the (uint64) seq field is 0, NOT the SeqUndefined sentinel. The generated
            // parser therefore maps an ABSENT seq field to Seq=0 => SeqNo(0) (because 0 != SeqUndefined),
            // and only a present SeqUndefined (MaxValue) maps to null. Mirror that exactly. (A real Akka
            // encoder always writes seq explicitly — as a value or SeqUndefined — but a different encoder
            // could omit it, and matching proto semantics keeps interop correct.)
            ulong seq = 0;

            var reader = new SequenceReader<byte>(envelope);
            while (!reader.End)
            {
                if (!TryReadVarint32(ref reader, out var tag))
                    throw new PduCodecException("Decoding PDU failed: truncated tag in RemoteEnvelope");

                switch (tag)
                {
                    case 10: // recipient = ActorRefData
                        recipientPathBytes = ParseActorRefPathSlice(ReadLengthDelimited(envelope, ref reader));
                        hasRecipient = true;
                        break;
                    case 18: // message = SerializedMessage (Payload) — kept opaque
                        messageBytes = ReadLengthDelimited(envelope, ref reader);
                        hasMessage = true;
                        break;
                    case 34: // sender = ActorRefData
                        senderPathBytes = ParseActorRefPathSlice(ReadLengthDelimited(envelope, ref reader));
                        hasSender = true;
                        break;
                    case 41: // seq = fixed64
                        if (!reader.TryReadLittleEndian(out long seqRaw))
                            throw new PduCodecException("Decoding PDU failed: truncated seq fixed64");
                        seq = unchecked((ulong)seqRaw);
                        break;
                    default:
                        SkipField(envelope, ref reader, tag);
                        break;
                }
            }

            // A well-formed RemoteEnvelope always carries recipient + message; the generated path would
            // NPE on a missing recipient/message. Surface a codec exception instead of a raw NRE.
            if (!hasRecipient)
                throw new PduCodecException("Decoding PDU failed: RemoteEnvelope missing recipient");

            // ResolveCached materializes the path string + resolves on a miss; on a hit (the common case)
            // it skips String.Ctor + Utf8 transcode + FastHash entirely. An ActorRefData with an absent
            // path sub-field yields an empty slice => "" path, mirroring protobuf's default-string behavior.
            var recipient = ResolveCached(recipientPathBytes, localAddress, provider);
            // See B0: recipient.Path.Address is byte-identical to the parsed wire address where it is
            // functionally consumed (non-local recipients), without a second FastHash.
            var recipientAddress = recipient.Path.Address;

            IActorRef senderOption = hasSender
                ? ResolveCached(senderPathBytes, localAddress, provider)
                : null;

            SeqNo? seqOption = seq != SeqUndefined ? new SeqNo(unchecked((long)seq)) : (SeqNo?)null;

            var serializedMessage = hasMessage ? SerializedMessage.Parser.ParseFrom(messageBytes) : null;

            return new Message(recipient, recipientAddress, serializedMessage, senderOption, seqOption);
        }

        private static ReadOnlySequence<byte> ParseActorRefPathSlice(ReadOnlySequence<byte> actorRefData)
        {
            ReadOnlySequence<byte> path = default; // absent path sub-field => empty (proto3 default "")
            var reader = new SequenceReader<byte>(actorRefData);
            while (!reader.End)
            {
                if (!TryReadVarint32(ref reader, out var tag))
                    throw new PduCodecException("Decoding PDU failed: truncated tag in ActorRefData");

                switch (tag)
                {
                    case 10: // path = string
                        path = ReadLengthDelimited(actorRefData, ref reader);
                        break;
                    default:
                        SkipField(actorRefData, ref reader, tag);
                        break;
                }
            }

            return path;
        }

        private static Ack ParseAck(ReadOnlySequence<byte> ackData)
        {
            long cumulativeAck = 0;
            List<SeqNo> nacks = null;

            var reader = new SequenceReader<byte>(ackData);
            while (!reader.End)
            {
                if (!TryReadVarint32(ref reader, out var tag))
                    throw new PduCodecException("Decoding PDU failed: truncated tag in AcknowledgementInfo");

                switch (tag)
                {
                    case 9: // cumulativeAck = fixed64
                        if (!reader.TryReadLittleEndian(out cumulativeAck))
                            throw new PduCodecException("Decoding PDU failed: truncated cumulativeAck fixed64");
                        break;
                    case 17: // nacks (unpacked repeated fixed64) — one element
                        if (!reader.TryReadLittleEndian(out long nack))
                            throw new PduCodecException("Decoding PDU failed: truncated nack fixed64");
                        (nacks ??= new List<SeqNo>()).Add(new SeqNo(nack));
                        break;
                    case 18: // nacks (packed repeated fixed64)
                    {
                        var packed = ReadLengthDelimited(ackData, ref reader);
                        var packedReader = new SequenceReader<byte>(packed);
                        while (!packedReader.End)
                        {
                            if (!packedReader.TryReadLittleEndian(out long packedNack))
                                throw new PduCodecException("Decoding PDU failed: truncated packed nack fixed64");
                            (nacks ??= new List<SeqNo>()).Add(new SeqNo(packedNack));
                        }

                        break;
                    }
                    default:
                        SkipField(ackData, ref reader, tag);
                        break;
                }
            }

            // Matches DecodeMessage: Ack(SeqNo, nacks). Empty/absent nacks => empty list (the single-arg
            // ctor delegates to new List<SeqNo>()).
            return nacks is null
                ? new Ack(new SeqNo(cumulativeAck))
                : new Ack(new SeqNo(cumulativeAck), nacks);
        }

        private static ReadOnlySequence<byte> ReadLengthDelimited(ReadOnlySequence<byte> source, ref SequenceReader<byte> reader)
        {
            if (!TryReadVarint32(ref reader, out var length) || length < 0)
                throw new PduCodecException("Decoding PDU failed: invalid length-delimited field length");
            if (reader.Remaining < length)
                throw new PduCodecException("Decoding PDU failed: truncated length-delimited field");

            var slice = source.Slice(reader.Position, length);
            reader.Advance(length);
            return slice;
        }

        private static void SkipField(ReadOnlySequence<byte> source, ref SequenceReader<byte> reader, int tag)
        {
            switch (tag & 0x7)
            {
                case WireVarint:
                    SkipVarint(ref reader);
                    break;
                case WireFixed64:
                    if (reader.Remaining < 8)
                        throw new PduCodecException("Decoding PDU failed: truncated fixed64 field");
                    reader.Advance(8);
                    break;
                case WireFixed32:
                    if (reader.Remaining < 4)
                        throw new PduCodecException("Decoding PDU failed: truncated fixed32 field");
                    reader.Advance(4);
                    break;
                case WireLengthDelimited:
                    ReadLengthDelimited(source, ref reader);
                    break;
                default:
                    throw new PduCodecException($"Decoding PDU failed: unsupported wire type {tag & 0x7} (tag {tag})");
            }
        }

        private static bool TryReadVarint32(ref SequenceReader<byte> reader, out int value)
        {
            long result = 0;
            var shift = 0;

            while (shift < 35)
            {
                if (!reader.TryRead(out var b))
                {
                    value = 0;
                    return false;
                }

                result |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                    {
                        value = 0;
                        return false;
                    }

                    value = (int)result;
                    return true;
                }

                shift += 7;
            }

            value = 0;
            return false;
        }

        private static void SkipVarint(ref SequenceReader<byte> reader)
        {
            // up to 10 bytes for a 64-bit varint
            for (var i = 0; i < 10; i++)
            {
                if (!reader.TryRead(out var b))
                    throw new PduCodecException("Decoding PDU failed: truncated varint");
                if ((b & 0x80) == 0)
                    return;
            }

            throw new PduCodecException("Decoding PDU failed: varint exceeds 10 bytes");
        }

        private AcknowledgementInfo AckBuilder(Ack ack)
        {
            var acki = new AcknowledgementInfo();
            acki.CumulativeAck = (ulong)ack.CumulativeAck.RawValue;
            acki.Nacks.Add(from nack in ack.Nacks select (ulong)nack.RawValue);

            return acki;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="recipient">TBD</param>
        /// <param name="serializedMessage">TBD</param>
        /// <param name="senderOption">TBD</param>
        /// <param name="seqOption">TBD</param>
        /// <param name="ackOption">TBD</param>
        /// <returns>TBD</returns>
        public override ByteString ConstructMessage(Address localAddress, IActorRef recipient, SerializedMessage serializedMessage,
            IActorRef senderOption = null, SeqNo? seqOption = null, Ack ackOption = null)
        {
            var ackAndEnvelope = new AckAndEnvelopeContainer();
            var envelope = new RemoteEnvelope() { Recipient = SerializeActorRef(recipient.Path.Address, recipient) };
            if (senderOption != null && senderOption.Path != null) { envelope.Sender = SerializeActorRef(localAddress, senderOption); }
            if (seqOption is { } seq) { envelope.Seq = (ulong)seq.RawValue; } else envelope.Seq = SeqUndefined;
            if (ackOption != null) { ackAndEnvelope.Ack = AckBuilder(ackOption); }
            envelope.Message = serializedMessage;
            ackAndEnvelope.Envelope = envelope;

            return ackAndEnvelope.ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <returns>TBD</returns>
        public override ByteString ConstructPureAck(Ack ack)
        {
            return new AckAndEnvelopeContainer() { Ack = AckBuilder(ack) }.ToByteString();
        }

#region Internal methods
        private IAkkaPdu DecodeControlPdu(AkkaControlMessage controlPdu)
        {
            switch (controlPdu.CommandType)
            {
                case CommandType.Associate:
                    var handshakeInfo = controlPdu.HandshakeInfo;
                    if (handshakeInfo != null) // HasHandshakeInfo
                    {
                        return new Associate(new HandshakeInfo(DecodeAddress(handshakeInfo.Origin), (long)handshakeInfo.Uid));
                    }
                    break;
                case CommandType.Disassociate:
                    return new Disassociate(DisassociateInfo.Unknown);
                case CommandType.DisassociateQuarantined:
                    return new Disassociate(DisassociateInfo.Quarantined);
                case CommandType.DisassociateShuttingDown:
                    return new Disassociate(DisassociateInfo.Shutdown);
                case CommandType.Heartbeat:
                    return new Heartbeat();
            }

            throw new PduCodecException($"Decoding of control PDU failed, invalid format, unexpected {controlPdu}");
        }



        private ByteString DISASSOCIATE
        {
            get { return ConstructControlMessagePdu(CommandType.Disassociate); }
        }

        private ByteString DISASSOCIATE_SHUTTING_DOWN
        {
            get { return ConstructControlMessagePdu(CommandType.DisassociateShuttingDown); }
        }

        private ByteString DISASSOCIATE_QUARANTINED
        {
            get { return ConstructControlMessagePdu(CommandType.DisassociateQuarantined); }
        }

        private static ByteString ConstructControlMessagePdu(CommandType code, AkkaHandshakeInfo handshakeInfo = null)
        {
            var controlMessage = new AkkaControlMessage() { CommandType = code };
            if (handshakeInfo != null)
            {
                controlMessage.HandshakeInfo = handshakeInfo;
            }

            return new AkkaProtocolMessage() { Instruction = controlMessage }.ToByteString();
        }

        private static Address DecodeAddress(AddressData origin)
        {
            return new Address(origin.Protocol, origin.System, origin.Hostname, (int)origin.Port);
        }

        private static ActorRefData SerializeActorRef(Address defaultAddress, IActorRef actorRef)
        {
            return new ActorRefData()
            {
                Path = (!string.IsNullOrEmpty(actorRef.Path.Address.Host))
                    ? actorRef.Path.ToSerializationFormat()
                    : actorRef.Path.ToSerializationFormatWithAddress(defaultAddress)
            };
        }

        private static AddressData SerializeAddress(Address address)
        {
            if (string.IsNullOrEmpty(address.Host) || !address.Port.HasValue)
                throw new ArgumentException($"Address {address} could not be serialized: host or port missing");
            return new AddressData()
            {
                Hostname = address.Host,
                Port = (uint)address.Port.Value,
                System = address.System,
                Protocol = address.Protocol
            };
        }

#endregion

        public AkkaPduProtobuffCodec(ActorSystem system) : base(system)
        {
        }
    }
}
