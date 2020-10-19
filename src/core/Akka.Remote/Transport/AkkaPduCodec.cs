//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Google.Protobuf;
using System.Runtime.Serialization;
using System.Text;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.Transport;
using Akka.Remote.Transport.Streaming;
using Akka.Util.Internal;
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

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="PduCodecException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected PduCodecException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
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
        public Message(IInternalActorRef recipient, Address recipientAddress, SerializedMessage serializedMessage, IActorRef senderOptional = null, SeqNo seq = null)
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
        /// TBD
        /// </summary>
        public SeqNo Seq { get; private set; }
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
        protected readonly AddressThreadLocalCache AddressCache;

        protected AkkaPduCodec(ActorSystem system)
        {
            System = system;
            AddressCache = AddressThreadLocalCache.For(system);
        }

        /// <summary>
        /// Return an <see cref="IAkkaPdu"/> instance that represents a PDU contained in the raw
        /// <see cref="ByteString"/>.
        /// </summary>
        /// <param name="raw">Encoded raw byte representation of an Akka PDU</param>
        /// <returns>Class representation of a PDU that can be used in a <see cref="PatternMatch"/>.</returns>
        public abstract IAkkaPdu DecodePdu(ByteString raw);
        
        public abstract IAkkaPdu DecodePdu(ArraySegment<byte> raw);

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

        public abstract IO.ByteString ConstructByteString(ByteString payload);
        public abstract IO.ByteString ConstructByteString(IO.ByteString payload);
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

        public abstract AckAndMessage DecodeMessage(ArraySegment<byte> raw, IRemoteActorRefProvider provider, Address localAddress);
        
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
            SerializedMessage serializedMessage, IActorRef senderOption = null, SeqNo seqOption = null, Ack ackOption = null);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ack">TBD</param>
        /// <returns>TBD</returns>
        public abstract ByteString ConstructPureAck(Ack ack);

        public abstract IO.ByteString ConstructMessage(Address localAddress, IActorRef recipient, SerializedIOBSMessage serializedMessage, IActorRef senderOption = null, SeqNo seqOption = null, Ack ackOption = null);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class AkkaPduProtobuffCodec : AkkaPduCodec
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

        public override IAkkaPdu DecodePdu(ArraySegment<byte> raw)
        {
            try
            {
                var pdu = AkkaProtocolMessage.Parser.ParseFrom(raw.Array,raw.Offset,raw.Count);
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

        public override IO.ByteString ConstructByteString(ByteString payload)
        {
            var payloadBytes =
                ByteStringConverters._getByteArrayUnsafeFunc(payload);
            return IO.ByteString.FromBytes(protoLengthDelimitedHeader(1, payloadBytes.Length))
                .Concat(IO.ByteString.FromBytes(payloadBytes));
        }
        
        public override IO.ByteString ConstructByteString(IO.ByteString payload)
        {

            return IO.ByteString
                .FromBytes(protoLengthDelimitedHeader(1, payload.Count))
                .Concat(payload);
        }
        
         
        /// <summary>
        /// Creates a Protobuf header for a Length delimited field.
        /// </summary>
        /// <param name="fieldPosition">the position of field in protobuf contract</param>
        /// <param name="length">length of data</param>
        /// <returns>The Headerr</returns>
        public static ArraySegment<byte> protoLengthDelimitedHeader(int fieldPosition, int length, int extraBytes =0)
        {
            byte[] buffer;
            if (length < (Int32.MaxValue / 8))
            {
                //optimal case; Below Int32.MaxValue/8
                //We will fit full signature (header+payload) in 4 bytes
                //Won't worry about smaller sizes here b/c
                //4 bytes is a good alignment for elsewhere.
                buffer = new byte[4+extraBytes];
                buffer[0] = (byte)(fieldPosition << 3 | 2);
                var position = 1;
                while (length > 127)
                {
                    buffer[position++] = (byte)((length & 0x7F) | 0x80);
                    length >>= 7;
                }
                buffer[position] = (byte)(length);
                return new ArraySegment<byte>(buffer, 0, position + 1);
            }
            else
            {
                //worst case, just grab 16 bytes.
                //We could probably get away with as few as  actually...
                buffer = new byte[16+extraBytes];
                buffer[0] = (byte)(fieldPosition << 3 | 2);
                var position = 1;
                while (length > 127)
                {
                    buffer[position++] = (byte)((length & 0x7F) | 0x80);
                    length >>= 7;
                }
                buffer[position] = (byte)(length);
                return new ArraySegment<byte>(buffer, 0, position + 1);
            }

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

            return BuildAckAndMessage(provider, localAddress, ackAndEnvelope);
        }
        public override AckAndMessage DecodeMessage(ArraySegment<byte> raw, IRemoteActorRefProvider provider, Address localAddress)
        {
            var ackAndEnvelope = AckAndEnvelopeContainer.Parser.ParseFrom(raw.Array,raw.Offset,raw.Count);

            return BuildAckAndMessage(provider, localAddress, ackAndEnvelope);
        }

        private AckAndMessage BuildAckAndMessage(IRemoteActorRefProvider provider,
            Address localAddress, AckAndEnvelopeContainer ackAndEnvelope)
        {
            Ack ackOption = null;

            if (ackAndEnvelope.Ack != null)
            {
                ackOption = new Ack(new SeqNo((long)ackAndEnvelope.Ack.CumulativeAck),
                    ackAndEnvelope.Ack.Nacks.Select(x => new SeqNo((long)x)));
            }

            Message messageOption = null;

            if (ackAndEnvelope.Envelope != null)
            {
                var envelopeContainer = ackAndEnvelope.Envelope;
                if (envelopeContainer != null)
                {
                    var recipient =
                        provider.ResolveActorRefWithLocalAddress(
                            envelopeContainer.Recipient.Path, localAddress);
                    Address recipientAddress;
                    if (AddressCache != null)
                    {
                        recipientAddress =
                            AddressCache.Cache.GetOrCompute(envelopeContainer.Recipient
                                .Path);
                    }
                    else
                    {
                        ActorPath.TryParseAddress(envelopeContainer.Recipient.Path,
                            out recipientAddress);
                    }

                    var serializedMessage = envelopeContainer.Message;
                    IActorRef senderOption = null;
                    if (envelopeContainer.Sender != null)
                    {
                        senderOption =
                            provider.ResolveActorRefWithLocalAddress(
                                envelopeContainer.Sender.Path, localAddress);
                    }

                    SeqNo seqOption = null;
                    if (envelopeContainer.Seq != SeqUndefined)
                    {
                        unchecked
                        {
                            seqOption =
                                new SeqNo((long)envelopeContainer
                                    .Seq); //proto takes a ulong
                        }
                    }

                    messageOption = new Message(recipient, recipientAddress,
                        serializedMessage, senderOption, seqOption);
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
            IActorRef senderOption = null, SeqNo seqOption = null, Ack ackOption = null)
        {
            var ackAndEnvelope = new AckAndEnvelopeContainer();
            var envelope = new RemoteEnvelope() { Recipient = SerializeActorRef(recipient.Path.Address, recipient) };
            if (senderOption != null && senderOption.Path != null) { envelope.Sender = SerializeActorRef(localAddress, senderOption); }
            if (seqOption != null) { envelope.Seq = (ulong)seqOption.RawValue; } else envelope.Seq = SeqUndefined;
            if (ackOption != null) { ackAndEnvelope.Ack = AckBuilder(ackOption); }
            
            envelope.Message = serializedMessage;
            ackAndEnvelope.Envelope = envelope;
            //Console.Write("PROTO-env " + envelope.ToByteArray().Length);
            //Console.Write("PROTO-AckEnv"+(ackAndEnvelope.ToByteString().Length));
            return ackAndEnvelope.ToByteString();
        }

        public ByteString constructMessageEnvelopePB(Address localAddress, IActorRef recipient, SerializedMessage serializedMessage,
            IActorRef senderOption = null, SeqNo seqOption = null, Ack ackOption = null)
        {
            var ackAndEnvelope = new AckAndEnvelopeContainer();
            var envelope = new RemoteEnvelope() { Recipient = SerializeActorRef(recipient.Path.Address, recipient) };
            if (senderOption != null && senderOption.Path != null) { envelope.Sender = SerializeActorRef(localAddress, senderOption); }
            if (seqOption != null) { envelope.Seq = (ulong)seqOption.RawValue; } else envelope.Seq = SeqUndefined;
            if (ackOption != null) { ackAndEnvelope.Ack = AckBuilder(ackOption); }
            
            envelope.Message = serializedMessage;
            
            return envelope.ToByteString();
        }
        public override IO.ByteString ConstructMessage(Address localAddress, IActorRef recipient, SerializedIOBSMessage serializedMessage,
            IActorRef senderOption = null, SeqNo seqOption = null, Ack ackOption = null)
        {
            
            var seq = seqOption != null ? (ulong?)seqOption.RawValue : null;
            var retStr = IO.ByteString.Empty;
            if (ackOption != null)
            {
                var ackInfo = CreateAckInfo(ackOption);
                retStr = ackInfo;
            }

            var remoteEnvelopeBs = CreateRemoteEnvelopeBS(
                SerializeActorRef(recipient.Path.Address, recipient),
                serializedMessage,
                (senderOption != null && senderOption.Path != null)
                    ? SerializeActorRef(localAddress, senderOption)
                    : null, seq ?? SeqUndefined);
            var newEnvelope = retStr.Concat(EncodeMessageAsField(2,
                remoteEnvelopeBs));
            //Console.WriteLine("IO-Ackenv"+(int)(newEnvelope.Count));
            return newEnvelope;
        }
        
        //public  IO.ByteString ConstructMainMessageBS(Address localAddress, IActorRef recipient, SerializedIOBSMessage serializedMessage,
        //    IActorRef senderOption = null, SeqNo seqOption = null, Ack ackOption = null)
        //{
        //    
        //    var ackAndEnvelope = new AckAndEnvelopeContainer();
        //
        //    var seq = seqOption != null ? (ulong?)seqOption.RawValue : null;
        //    var retStr = IO.ByteString.Empty;
        //    if (ackOption != null)
        //    {
        //        var ackInfo = CreateAckInfo(ackOption);
        //        retStr = retStr.Concat(ackInfo);
        //    }
        //
        //    var remoteEnvelopeBs = CreateRemoteEnvelopeBS(
        //        SerializeActorRef(recipient.Path.Address, recipient),
        //        serializedMessage,
        //        (senderOption != null && senderOption.Path != null)
        //            ? SerializeActorRef(localAddress, senderOption)
        //            : null, seq ?? SeqUndefined);
        //    return remoteEnvelopeBs;
        //
        //}
        public IO.ByteString CreateRemoteEnvelopeBS(ActorRefData ad, SerializedIOBSMessage message, ActorRefData sender, ulong seq)
        {
            
            var recipient = receiverCache.GetOrCompute(ad.Path);
            var ret = recipient;
            var codedMessage = EncodeMessageAsField(2,EncodePayload( message));
            
            ret = ret.Concat(codedMessage);
            if (sender != null)
            {
                
                ret = ret.Concat(senderCache.GetOrCompute(sender.Path));
            }
            ret = ret.Concat(WriteSingleLongAs64Bit(5, (ulong)seq));
            
            //Console.WriteLine("IO-env " + ret.Count);
            return ret;
        }

        internal static IO.ByteString CreateDestinationByteString(string path)
        {
            var pathUtf8 = Encoding.UTF8.GetBytes(path);
            var recipient = IO.ByteString.FromBytes(
                    protoLengthDelimitedHeader(1, pathUtf8.Length))
                .Concat(IO.ByteString.FromBytes(pathUtf8));
            return EncodeMessageAsField(1, recipient);
        }

        internal static IO.ByteString CreateSenderPayload(string path)
        {
            
            var senderPathUtf8 = Encoding.UTF8.GetBytes(path);
            var mySender = IO.ByteString.FromBytes(
                    protoLengthDelimitedHeader(1, senderPathUtf8.Length))
                .Concat(IO.ByteString.FromBytes(senderPathUtf8));
            
            return EncodeMessageAsField(4, mySender);
        }

        IO.ByteString CreateAckInfo(Ack ackOption)
        {
            return WriteSingleLongAs64Bit(1, (ulong)ackOption.CumulativeAck.RawValue)
                .Concat(WriteRepeatedLongsAs64Bit(2, ackOption.Nacks));
        }

        IO.ByteString WriteSingleLongAs64Bit(int position, ulong value)
        {
            var ret1 = new byte[9];
            ret1[0] = (byte)(position << 3 | 1);
            BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(ret1,1,8),value );
            return IO.ByteString.FromBytes(ret1);
            //var ret = new byte[1];
            //ret[0] = (byte)(position << 3 | 1);
            //var final =IO.ByteString.FromBytes(ret)
            //    .Concat(IO.ByteString.FromBytes(BitConverter.GetBytes(value)));
            //if (final.Count != ret1.Length)
            //{
            //    Console.WriteLine("oops" + ret1.ToString() + final.ToArray().ToString());
            //}
            //
            //return final;
        }

        IO.ByteString WriteRepeatedLongsAs64Bit(int position, SortedSet<SeqNo> longs)
        {
            IO.ByteString ret = IO.ByteString.Empty;
            //var seq = longs.Count;
            //var c = longs.Count;
            var byteArr = new byte[longs.Count * 8];
            var ctr = 0;
            foreach (var l in longs)
            {
                
                BinaryPrimitives.WriteUInt64LittleEndian(new Span<byte>(byteArr,ctr,8),(ulong)l.RawValue );
                ctr = ctr + 8;
            }

            //ret =  IO.ByteString.FromBytes(longs.Select(r =>
            //{
            //    var lb = BitConverter.GetBytes(r.RawValue);
            //    return new ArraySegment<byte>(lb, 0, lb.Length);
            //}));
            var headerBuffer = EncodeVarIntPreBuffer(ret.Count);
            headerBuffer.Array[0] = (byte)(position << 3 | 2);
            return IO.ByteString.FromBytes(headerBuffer).Concat(ret);
        }

        private static ArraySegment<byte> EncodeVarIntPreBuffer(long value)
        {
            if (value < (Int32.MaxValue / 8))
            {
                //optimal case; Below Int32.MaxValue/8
                //We will fit full signature (header+payload) in 4 bytes
                //Won't worry about smaller sizes here b/c
                //4 bytes is a good alignment for elsewhere.
                var buffer = new byte[4];
                //buffer[0] = (byte)(fieldPosition << 3 | 2);
                var position = 1;
                while (value > 127)
                {
                    buffer[position++] = (byte)((value & 0x7F) | 0x80);
                    value >>= 7;
                }
                buffer[position] = (byte)(value);
                return new ArraySegment<byte>(buffer, 0, position + 1);
            }
            else
            {
                //worst case, just grab 16 bytes.
                //We could probably get away with as few as  actually...
                var buffer = new byte[16];
                //buffer[0] = (byte)(fieldPosition << 3 | 2);
                var position = 1;
                while (value > 127)
                {
                    buffer[position++] = (byte)((value & 0x7F) | 0x80);
                    value >>= 7;
                }
                buffer[position] = (byte)(value);
                return new ArraySegment<byte>(buffer, 0, position + 1);
            }

        }

        public static IO.ByteString EncodeMessageAsField(int position,
            IO.ByteString data)
        {
            var header = EncodeVarIntPreBuffer(data.Count);
            header.Array[0] = (byte)(position << 3 | 2);
            return IO.ByteString
                .FromBytes(header.Array, header.Offset, header.Count)
                .Concat(data);
        }

        public static readonly byte payloadSerIdByte = (byte)(2 << 3 | 0); 
        public IO.ByteString EncodePayload(SerializedIOBSMessage data)
        {

            int id = data.serId;
            var serId = serializerIdByteStringCache.GetOrCompute(id);
                IO.ByteString msg = null;

                if (data?.message == null)
                {
                    msg = IO.ByteString.Empty;
                }
                else
                {
                    msg = IO.ByteString.FromBytes(
                        new[]
                        {
                            protoLengthDelimitedHeader(1,
                                data.message.Length),
                            new ArraySegment<byte>(data.message, 0,
                                data.message.Length)
                        });
                }
                    
                //var serId = IO.ByteString.FromBytes(serIdBytes.Array,
                //    serIdBytes.Offset, serIdBytes.Count);

                var manifest = data.manifest==null?IO.ByteString.Empty: manifestCache.GetOrCompute(data.manifest);
                var ret = msg.Concat(serId).Concat(manifest);
                return ret;
        }

        internal static ArraySegment<byte> CreateSerIdHeader(int id)
        {
            var serIdBytes = EncodeVarIntPreBuffer(id);
            serIdBytes.Array[0] = payloadSerIdByte;
            return serIdBytes;
        }

        private ReceiverBytestringCache receiverCache =
            new ReceiverBytestringCache();
            private SenderBytestringCache senderCache =
                        new SenderBytestringCache();
        ManifestBytestringCache manifestCache = new ManifestBytestringCache();
        SerializerIdByteStringCache serializerIdByteStringCache = new SerializerIdByteStringCache();
        internal static IO.ByteString CreateManifest(string manifestStr)
        {
            if (string.IsNullOrWhiteSpace(manifestStr))
            {
                return IO.ByteString.Empty;
            }
            var data = Encoding.UTF8.GetBytes(manifestStr);
                return IO.ByteString.FromBytes(
                        protoLengthDelimitedHeader(3, data.Length))
                    .Concat(IO.ByteString.FromBytes(data));
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
    }
    internal sealed class SerializerIdByteStringCache : LruBoundedCache<int, IO.ByteString>
    {
            
            public SerializerIdByteStringCache(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
            {
            }
    
            protected override int Hash(int k)
            {
                return k;
            }
    
            protected override Akka.IO.ByteString Compute(int k)
            {
                return IO.ByteString.FromBytes(AkkaPduProtobuffCodec.CreateSerIdHeader(k)).Compact();
            }
    
            protected override bool IsCacheable(IO.ByteString v)
            {
                return v != null;
            }
    }
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ReceiverBytestringCache : LruBoundedCache<string, Akka.IO.ByteString>
    {
        
        public ReceiverBytestringCache(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
        }

        protected override int Hash(string k)
        {
            return FastHash.OfStringFast(k);
        }

        protected override Akka.IO.ByteString Compute(string k)
        {
            return AkkaPduProtobuffCodec.CreateDestinationByteString(k).Compact();
        }

        protected override bool IsCacheable(IO.ByteString v)
        {
            return v != null;
        }
    }
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SenderBytestringCache : LruBoundedCache<string, Akka.IO.ByteString>
    {
        
        public SenderBytestringCache(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
        }

        protected override int Hash(string k)
        {
            return FastHash.OfStringFast(k);
        }

        protected override Akka.IO.ByteString Compute(string k)
        {
            return AkkaPduProtobuffCodec.CreateSenderPayload(k).Compact();
        }

        protected override bool IsCacheable(IO.ByteString v)
        {
            return v != null;
        }
    }
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ManifestBytestringCache : LruBoundedCache<string, Akka.IO.ByteString>
    {
        
        public ManifestBytestringCache(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
        }

        protected override int Hash(string k)
        {
            return FastHash.OfStringFast(k);
        }

        protected override Akka.IO.ByteString Compute(string k)
        {
            return AkkaPduProtobuffCodec.CreateManifest(k).Compact();
        }

        protected override bool IsCacheable(IO.ByteString v)
        {
            return v != null;
        }
    }
}
