//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Linq;
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
        public virtual IAkkaPdu DecodePdu(ByteString raw)
        {
            return DecodePdu(new ReadOnlySequence<byte>(raw.Memory));
        }

        /// <summary>
        /// Return an <see cref="IAkkaPdu"/> instance that represents a PDU contained in the raw
        /// <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <param name="raw">Encoded raw byte representation of an Akka PDU</param>
        /// <returns>Class representation of a PDU.</returns>
        public abstract IAkkaPdu DecodePdu(ReadOnlySequence<byte> raw);

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
        public virtual ByteString ConstructPayload(ByteString payload)
        {
            return ConstructByteString(writer => ConstructPayload(payload, writer), payload.Length + 8);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <param name="writer">TBD</param>
        public abstract void ConstructPayload(ByteString payload, IBufferWriter<byte> writer);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <returns>TBD</returns>
        public virtual ByteString ConstructAssociate(HandshakeInfo info)
        {
            return ConstructByteString(writer => ConstructAssociate(info, writer));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <param name="writer">TBD</param>
        public abstract void ConstructAssociate(HandshakeInfo info, IBufferWriter<byte> writer);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <returns>TBD</returns>
        public virtual ByteString ConstructDisassociate(DisassociateInfo reason)
        {
            return ConstructByteString(writer => ConstructDisassociate(reason, writer));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="writer">TBD</param>
        public abstract void ConstructDisassociate(DisassociateInfo reason, IBufferWriter<byte> writer);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public virtual ByteString ConstructHeartbeat()
        {
            return ConstructByteString(ConstructHeartbeat);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="writer">TBD</param>
        public abstract void ConstructHeartbeat(IBufferWriter<byte> writer);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="raw">TBD</param>
        /// <param name="provider">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <returns>TBD</returns>
        public virtual AckAndMessage DecodeMessage(ByteString raw, IRemoteActorRefProvider provider, Address localAddress)
        {
            return DecodeMessage(new ReadOnlySequence<byte>(raw.Memory), provider, localAddress);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="raw">TBD</param>
        /// <param name="provider">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <returns>TBD</returns>
        public abstract AckAndMessage DecodeMessage(ReadOnlySequence<byte> raw, IRemoteActorRefProvider provider, Address localAddress);

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
        public virtual ByteString ConstructMessage(Address localAddress, IActorRef recipient,
            SerializedMessage serializedMessage, IActorRef senderOption = null, SeqNo? seqOption = null, Ack ackOption = null)
        {
            return ConstructByteString(writer => ConstructMessage(localAddress, recipient, serializedMessage, writer, senderOption, seqOption, ackOption));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="recipient">TBD</param>
        /// <param name="serializedMessage">TBD</param>
        /// <param name="writer">TBD</param>
        /// <param name="senderOption">TBD</param>
        /// <param name="seqOption">TBD</param>
        /// <param name="ackOption">TBD</param>
        public abstract void ConstructMessage(Address localAddress, IActorRef recipient, SerializedMessage serializedMessage,
            IBufferWriter<byte> writer, IActorRef senderOption = null, SeqNo? seqOption = null, Ack ackOption = null);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <returns>TBD</returns>
        public virtual ByteString ConstructPureAck(Ack ack)
        {
            return ConstructByteString(writer => ConstructPureAck(ack, writer));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <param name="writer">TBD</param>
        public abstract void ConstructPureAck(Ack ack, IBufferWriter<byte> writer);

        private static ByteString ConstructByteString(Action<IBufferWriter<byte>> write, int initialCapacity = 0)
        {
            var writer = initialCapacity > 0
                ? new ArrayBufferWriter<byte>(initialCapacity)
                : new ArrayBufferWriter<byte>();
            write(writer);
            return ByteString.CopyFrom(writer.WrittenSpan);
        }
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
        public override IAkkaPdu DecodePdu(ReadOnlySequence<byte> raw)
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
            return CreatePayloadPdu(payload).ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <param name="writer">TBD</param>
        public override void ConstructPayload(ByteString payload, IBufferWriter<byte> writer)
        {
            CreatePayloadPdu(payload).WriteTo(writer);
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
        /// <param name="info">TBD</param>
        /// <param name="writer">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="info"/> contains an invalid address.
        /// </exception>
        public override void ConstructAssociate(HandshakeInfo info, IBufferWriter<byte> writer)
        {
            var handshakeInfo = new AkkaHandshakeInfo()
            {
                Origin = SerializeAddress(info.Origin),
                Uid = (ulong)info.Uid
            };

            ConstructControlMessagePdu(CommandType.Associate, handshakeInfo, writer);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="writer">TBD</param>
        public override void ConstructDisassociate(DisassociateInfo reason, IBufferWriter<byte> writer)
        {
            switch (reason)
            {
                case DisassociateInfo.Quarantined:
                    ConstructControlMessagePdu(CommandType.DisassociateQuarantined, null, writer);
                    break;
                case DisassociateInfo.Shutdown:
                    ConstructControlMessagePdu(CommandType.DisassociateShuttingDown, null, writer);
                    break;
                case DisassociateInfo.Unknown:
                default:
                    ConstructControlMessagePdu(CommandType.Disassociate, null, writer);
                    break;
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
        /// Creates a new Heartbeat message instance.
        /// </summary>
        /// <param name="writer">The writer receiving the heartbeat message.</param>
        public override void ConstructHeartbeat(IBufferWriter<byte> writer)
        {
            var span = writer.GetSpan(HeartbeatPdu.Length);
            HeartbeatPdu.Span.CopyTo(span);
            writer.Advance(HeartbeatPdu.Length);
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
        public override AckAndMessage DecodeMessage(ReadOnlySequence<byte> raw, IRemoteActorRefProvider provider, Address localAddress)
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

        private static AkkaProtocolMessage CreatePayloadPdu(ByteString payload)
        {
            return new AkkaProtocolMessage() { Payload = payload };
        }

        private AckAndEnvelopeContainer CreateAckAndEnvelope(Address localAddress, IActorRef recipient,
            SerializedMessage serializedMessage, IActorRef senderOption = null, SeqNo? seqOption = null,
            Ack ackOption = null)
        {
            var ackAndEnvelope = new AckAndEnvelopeContainer();
            var envelope = new RemoteEnvelope() { Recipient = SerializeActorRef(recipient.Path.Address, recipient) };
            if (senderOption != null && senderOption.Path != null) { envelope.Sender = SerializeActorRef(localAddress, senderOption); }
            if (seqOption is { } seq) { envelope.Seq = (ulong)seq.RawValue; } else envelope.Seq = SeqUndefined;
            if (ackOption != null) { ackAndEnvelope.Ack = AckBuilder(ackOption); }
            envelope.Message = serializedMessage;
            ackAndEnvelope.Envelope = envelope;

            return ackAndEnvelope;
        }

        private AckAndEnvelopeContainer CreatePureAck(Ack ack)
        {
            return new AckAndEnvelopeContainer() { Ack = AckBuilder(ack) };
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
            return CreateAckAndEnvelope(localAddress, recipient, serializedMessage, senderOption, seqOption, ackOption).ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="recipient">TBD</param>
        /// <param name="serializedMessage">TBD</param>
        /// <param name="writer">TBD</param>
        /// <param name="senderOption">TBD</param>
        /// <param name="seqOption">TBD</param>
        /// <param name="ackOption">TBD</param>
        public override void ConstructMessage(Address localAddress, IActorRef recipient, SerializedMessage serializedMessage,
            IBufferWriter<byte> writer, IActorRef senderOption = null, SeqNo? seqOption = null, Ack ackOption = null)
        {
            CreateAckAndEnvelope(localAddress, recipient, serializedMessage, senderOption, seqOption, ackOption).WriteTo(writer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <returns>TBD</returns>
        public override ByteString ConstructPureAck(Ack ack)
        {
            return CreatePureAck(ack).ToByteString();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <param name="writer">TBD</param>
        public override void ConstructPureAck(Ack ack, IBufferWriter<byte> writer)
        {
            CreatePureAck(ack).WriteTo(writer);
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
            return CreateControlMessagePdu(code, handshakeInfo).ToByteString();
        }

        private static void ConstructControlMessagePdu(CommandType code, AkkaHandshakeInfo handshakeInfo, IBufferWriter<byte> writer)
        {
            CreateControlMessagePdu(code, handshakeInfo).WriteTo(writer);
        }

        private static AkkaProtocolMessage CreateControlMessagePdu(CommandType code, AkkaHandshakeInfo handshakeInfo = null)
        {
            var controlMessage = new AkkaControlMessage() { CommandType = code };
            if (handshakeInfo != null)
            {
                controlMessage.HandshakeInfo = handshakeInfo;
            }

            return new AkkaProtocolMessage() { Instruction = controlMessage };
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
