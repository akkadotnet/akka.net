//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Google.Protobuf;
using System.Runtime.Serialization;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.Serialization.V2;
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
                    
                    //todo get parsed address from provider
                    var recipientAddress = ActorPathCache.Cache.GetOrCompute(envelopeContainer.Recipient.Path).Address;
                    
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
                        return new Associate(new HandshakeInfo(DecodeAddress(handshakeInfo.Origin), (int)handshakeInfo.Uid));
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

        // ─── V2 wrap-pipeline (experimental) ──────────────────────────────────
        //
        // Skips the MessageSerializer.Serialize + ConstructMessage two-step (which
        // builds a SerializedMessage proto, ByteString.CopyFroms the inner bytes,
        // then builds RemoteEnvelope + AckAndEnvelopeContainer proto graphs, then
        // serializes that graph via ToByteString()). Instead, hand-writes the
        // AckAndEnvelopeContainer wire format directly into a buffer, with the inner
        // V2 serializer invoked inline. Wire format is unchanged — V1 peers parse
        // V2 output transparently.
        //
        // See src/core/Akka.Remote/Serialization/V2/V2Codec.cs for the writer/reader
        // implementation and the fixed-width-varint patching technique.

        private V2SerializerRegistry? _v2Registry;
        private V2RemoteEnvelopeWriter? _v2Writer;

        private V2RemoteEnvelopeWriter V2Writer
        {
            get
            {
                if (_v2Writer is not null)
                    return _v2Writer;

                var serialization = ((ExtendedActorSystem)System).Serialization;
                _v2Registry = new V2SerializerRegistry(serialization);
                _v2Writer = new V2RemoteEnvelopeWriter(_v2Registry);
                return _v2Writer;
            }
        }

        // Per-thread scratch buffer for V2 wire-format writes. EndpointWriter actors run on
        // dispatcher threads; one buffer per thread, reset between calls. Avoids per-call
        // allocation of the buffer object + its backing byte[].
        [System.ThreadStatic]
        private static PatchingBufferWriter _threadBuffer;

        /// <summary>
        /// V2 send path. Equivalent in wire format to the V1
        /// <see cref="ConstructMessage(Address, IActorRef, SerializedMessage, IActorRef, SeqNo?, Ack)"/>,
        /// but skips the intermediate <see cref="SerializedMessage"/> proto construction and
        /// the AckAndEnvelopeContainer.ToByteString() serialize step.
        /// </summary>
        public ByteString ConstructMessageV2(
            Address localAddress,
            IActorRef recipient,
            object payload,
            IActorRef senderOption = null,
            SeqNo? seqOption = null,
            Ack ackOption = null)
        {
            // Recipient/sender ActorRefData wire bytes — V1 also builds these per call
            // inside ConstructMessage via SerializeActorRef, so the per-call cost is the
            // same here. (Caching is a follow-on optimization for both V1 and V2.)
            var recipientBytes = SerializeActorRef(recipient.Path.Address, recipient).ToByteArray();
            var senderBytes = (senderOption is not null && senderOption.Path is not null)
                ? SerializeActorRef(localAddress, senderOption).ToByteArray()
                : Array.Empty<byte>();

            var seq = seqOption is { } s ? (ulong)s.RawValue : SeqUndefined;

            ulong? ackCumulative = null;
            IReadOnlyList<ulong> ackNacks = null;
            if (ackOption is not null)
            {
                ackCumulative = (ulong)ackOption.CumulativeAck.RawValue;
                ackNacks = ackOption.Nacks.Select(n => (ulong)n.RawValue).ToArray();
            }

            // ThreadStatic pooled buffer — first call on a thread allocates, subsequent calls
            // reset and reuse. EndpointWriter dispatch is sequential on the actor's thread.
            var buffer = _threadBuffer;
            if (buffer is null)
            {
                buffer = new PatchingBufferWriter(initialCapacity: 1024);
                _threadBuffer = buffer;
            }
            else
            {
                buffer.Reset();
            }

            V2Writer.Serialize(buffer, recipientBytes, senderBytes, seq, payload, ackCumulative, ackNacks);

            // One final byte[] copy into the ByteString. V1 also allocates here via
            // ackAndEnvelope.ToByteString(). UnsafeByteOperations.UnsafeWrap would
            // avoid the copy but requires sole ownership of the byte[] — left for a
            // follow-on with pooled buffers.
            return ByteString.CopyFrom(buffer.WrittenSpan);
        }

        /// <summary>
        /// V2 receive path. Parses the AckAndEnvelopeContainer wire bytes directly into
        /// an <see cref="AckAndMessage"/> in the same shape V1's <see cref="DecodeMessage"/>
        /// produces, so the downstream AckedReceiveBuffer / DeliverAndAck / Dispatch pipeline
        /// is unchanged (reliable delivery semantics preserved across reconnects).
        ///
        /// What V2 saves on this path:
        ///   - No <c>AckAndEnvelopeContainer.Parser.ParseFrom</c> proto graph allocation
        ///     (parses field tags directly, doesn't materialize <c>AckAndEnvelopeContainer</c>,
        ///     <c>RemoteEnvelope</c>, or <c>ActorRefData</c> proto objects).
        ///   - Inner payload bytes are wrapped zero-copy via
        ///     <see cref="UnsafeByteOperations.UnsafeWrap(ReadOnlyMemory{byte})"/> instead of
        ///     copied through <c>ByteString.CopyFrom</c>. The downstream
        ///     <c>payload.Message.ToByteArray()</c> in <see cref="MessageSerializer.Deserialize"/>
        ///     still materializes once for the V1-typed Serialize/Deserialize bridge.
        /// </summary>
        public AckAndMessage DecodeMessageV2(
            ByteString raw,
            IRemoteActorRefProvider provider,
            Address localAddress)
        {
            // ByteString.Memory is zero-copy. The V2 parser slices into it for the inner
            // payload bytes, which are then handed to UnsafeWrap below — also zero-copy.
            var meta = ParseEnvelopeMetadata(raw.Memory);

            Ack ackOption = null;
            if (meta.AckCumulative.HasValue)
            {
                var nacks = meta.AckNacks is null || meta.AckNacks.Length == 0
                    ? Enumerable.Empty<SeqNo>()
                    : meta.AckNacks.Select(n => new SeqNo((long)n));
                ackOption = new Ack(new SeqNo((long)meta.AckCumulative.Value), nacks);
            }

            Message messageOption = null;
            if (!string.IsNullOrEmpty(meta.RecipientPath))
            {
                var recipient = provider.ResolveActorRefWithLocalAddress(meta.RecipientPath, localAddress);
                var recipientAddress = ActorPathCache.Cache.GetOrCompute(meta.RecipientPath).Address;

                IActorRef senderOption = null;
                if (!string.IsNullOrEmpty(meta.SenderPath))
                    senderOption = provider.ResolveActorRefWithLocalAddress(meta.SenderPath, localAddress);

                SeqNo? seqOption = null;
                if (meta.Seq != SeqUndefined)
                    seqOption = new SeqNo(unchecked((long)meta.Seq));

                // Build a SerializedMessage that points at the wire bytes zero-copy via
                // UnsafeWrap. The downstream MessageSerializer.Deserialize handles it the
                // same way as a V1-decoded SerializedMessage.
                var serializedMessage = new SerializedMessage
                {
                    Message = meta.InnerBytes.IsEmpty
                        ? ByteString.Empty
                        : UnsafeByteOperations.UnsafeWrap(meta.InnerBytes),
                    SerializerId = meta.InnerSerializerId,
                };
                if (!string.IsNullOrEmpty(meta.InnerManifest))
                    serializedMessage.MessageManifest = ByteString.CopyFromUtf8(meta.InnerManifest);

                messageOption = new Message(recipient, recipientAddress, serializedMessage, senderOption, seqOption);
            }

            return new AckAndMessage(ackOption, messageOption);
        }

        /// <summary>
        /// Raw envelope metadata extracted from the wire bytes — no proto objects materialized,
        /// no inner payload deserialization.
        /// </summary>
        private readonly struct EnvelopeMetadata
        {
            public EnvelopeMetadata(
                string recipientPath, string senderPath, ulong seq,
                int innerSerializerId, string innerManifest, ReadOnlyMemory<byte> innerBytes,
                ulong? ackCumulative, ulong[] ackNacks)
            {
                RecipientPath = recipientPath;
                SenderPath = senderPath;
                Seq = seq;
                InnerSerializerId = innerSerializerId;
                InnerManifest = innerManifest;
                InnerBytes = innerBytes;
                AckCumulative = ackCumulative;
                AckNacks = ackNacks;
            }

            public string RecipientPath { get; }
            public string SenderPath { get; }
            public ulong Seq { get; }
            public int InnerSerializerId { get; }
            public string InnerManifest { get; }
            public ReadOnlyMemory<byte> InnerBytes { get; }
            public ulong? AckCumulative { get; }
            public ulong[] AckNacks { get; }
        }

        /// <summary>
        /// Parses AckAndEnvelopeContainer wire bytes into raw metadata. No proto objects
        /// allocated, no inner deserialization. The inner payload bytes are a zero-copy
        /// slice of <paramref name="wireBytes"/>.
        /// </summary>
        private static EnvelopeMetadata ParseEnvelopeMetadata(ReadOnlyMemory<byte> wireBytes)
        {
            var span = wireBytes.Span;
            string recipientPath = string.Empty;
            string senderPath = string.Empty;
            ulong seq = 0;
            int innerSerializerId = 0;
            string innerManifest = string.Empty;
            ReadOnlyMemory<byte> innerBytes = default;
            ulong? ackCumulative = null;
            ulong[] ackNacks = null;

            while (!span.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref span);
                switch (fieldNumber)
                {
                    case 1: // ack (AcknowledgementInfo, length-delimited)
                    {
                        var ackBytes = ProtoWire.ReadLengthDelimited(ref span);
                        ParseAckMetadata(ackBytes, out ackCumulative, out ackNacks);
                        break;
                    }
                    case 2: // envelope (RemoteEnvelope, length-delimited)
                    {
                        var envLen = (int)ProtoWire.ReadVarint32(ref span);
                        var envOffset = wireBytes.Length - span.Length;
                        var envSpan = span.Slice(0, envLen);
                        ParseEnvelopeFields(
                            envSpan,
                            wireBytes.Slice(envOffset, envLen),
                            out recipientPath, out senderPath, out seq,
                            out innerSerializerId, out innerManifest, out innerBytes);
                        span = span.Slice(envLen);
                        break;
                    }
                    default:
                        ProtoWire.SkipField(ref span, wireType);
                        break;
                }
            }

            return new EnvelopeMetadata(recipientPath, senderPath, seq, innerSerializerId, innerManifest, innerBytes, ackCumulative, ackNacks);
        }

        private static void ParseAckMetadata(ReadOnlySpan<byte> ackBytes, out ulong? cumulative, out ulong[] nacks)
        {
            cumulative = null;
            nacks = null;
            List<ulong> nackList = null;
            var bytes = ackBytes;
            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                switch (fieldNumber)
                {
                    case 1: cumulative = ProtoWire.ReadFixed64(ref bytes); break;
                    case 2: (nackList ??= new List<ulong>()).Add(ProtoWire.ReadFixed64(ref bytes)); break;
                    default: ProtoWire.SkipField(ref bytes, wireType); break;
                }
            }
            if (nackList is { Count: > 0 })
                nacks = nackList.ToArray();
        }

        private static void ParseEnvelopeFields(
            ReadOnlySpan<byte> envSpan,
            ReadOnlyMemory<byte> envMemory,
            out string recipientPath, out string senderPath, out ulong seq,
            out int innerSerializerId, out string innerManifest, out ReadOnlyMemory<byte> innerBytes)
        {
            recipientPath = string.Empty;
            senderPath = string.Empty;
            seq = 0;
            innerSerializerId = 0;
            innerManifest = string.Empty;
            innerBytes = default;

            var bytes = envSpan;
            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                switch (fieldNumber)
                {
                    case 1: // recipient
                    {
                        var actorRefBytes = ProtoWire.ReadLengthDelimited(ref bytes);
                        recipientPath = ExtractActorRefPath(actorRefBytes);
                        break;
                    }
                    case 2: // message (Payload)
                    {
                        var payloadLen = (int)ProtoWire.ReadVarint32(ref bytes);
                        var payloadOffset = envSpan.Length - bytes.Length;
                        var payloadSpan = bytes.Slice(0, payloadLen);
                        var payloadMemory = envMemory.Slice(payloadOffset, payloadLen);
                        ParsePayloadFields(
                            payloadSpan, payloadMemory,
                            out innerSerializerId, out innerManifest, out innerBytes);
                        bytes = bytes.Slice(payloadLen);
                        break;
                    }
                    case 4: // sender
                    {
                        var actorRefBytes = ProtoWire.ReadLengthDelimited(ref bytes);
                        senderPath = ExtractActorRefPath(actorRefBytes);
                        break;
                    }
                    case 5: // seq (fixed64)
                        seq = ProtoWire.ReadFixed64(ref bytes);
                        break;
                    default:
                        ProtoWire.SkipField(ref bytes, wireType);
                        break;
                }
            }
        }

        private static void ParsePayloadFields(
            ReadOnlySpan<byte> payloadSpan,
            ReadOnlyMemory<byte> payloadMemory,
            out int innerSerializerId, out string innerManifest, out ReadOnlyMemory<byte> innerBytes)
        {
            innerSerializerId = 0;
            innerManifest = string.Empty;
            innerBytes = default;

            var bytes = payloadSpan;
            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                switch (fieldNumber)
                {
                    case 1: // message bytes
                    {
                        var len = (int)ProtoWire.ReadVarint32(ref bytes);
                        var offset = payloadSpan.Length - bytes.Length;
                        innerBytes = payloadMemory.Slice(offset, len);
                        bytes = bytes.Slice(len);
                        break;
                    }
                    case 2: innerSerializerId = (int)ProtoWire.ReadVarint32(ref bytes); break;
                    case 3: innerManifest = ProtoWire.ReadString(ref bytes); break;
                    default: ProtoWire.SkipField(ref bytes, wireType); break;
                }
            }
        }

        private static string ExtractActorRefPath(ReadOnlySpan<byte> actorRefDataBytes)
        {
            var bytes = actorRefDataBytes;
            while (!bytes.IsEmpty)
            {
                var (fieldNumber, wireType) = ProtoWire.ReadTag(ref bytes);
                if (fieldNumber == 1 && wireType == ProtoWire.WireTypeLengthDelimited)
                    return ProtoWire.ReadString(ref bytes);
                ProtoWire.SkipField(ref bytes, wireType);
            }
            return string.Empty;
        }
    }
}
