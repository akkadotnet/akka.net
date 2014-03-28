using System;
using Akka.Actor;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class PduCodecException : AkkaException
    {
        public PduCodecException(string msg, Exception cause) : base(msg, cause){}
    }

    /*
     * Interface used to represent Akka PDUs (Protocol Data Unit)
     */
    public interface IAkkaPdu { }

    public sealed class Associate : IAkkaPdu
    {
        public Associate(HandshakeInfo info)
        {
            Info = info;
        }

        public HandshakeInfo Info { get; private set; }
    }

    public sealed class Disassociate : IAkkaPdu
    {
        public Disassociate(DisassociateInfo reason)
        {
            Reason = reason;
        }

        public DisassociateInfo Reason { get; private set; }
    }
    public sealed class Heartbeat : IAkkaPdu { }

    public sealed class Payload : IAkkaPdu
    {
        public Payload(ByteString bytes)
        {
            Bytes = bytes;
        }

        public ByteString Bytes { get; private set; }
    }

    public sealed class Message : IAkkaPdu
    {
        public Message(InternalActorRef recipient, Address recipientAddress, SerializedMessage serializedMessage, ActorRef senderOptional = null)
        {
            SenderOptional = senderOptional;
            SerializedMessage = serializedMessage;
            RecipientAddress = recipientAddress;
            Recipient = recipient;
        }

        public InternalActorRef Recipient { get; private set; }

        public Address RecipientAddress { get; private set; }

        public SerializedMessage SerializedMessage { get; private set; }

        public ActorRef SenderOptional { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// A Codec that is able to convert Akka PDUs from and to <see cref="ByteString"/>
    /// </summary>
    public abstract class AkkaPduCodecBase
    {
        /// <summary>
        /// Return an <see cref="IAkkaPdu"/> instance that represents a PDU contained in the raw
        /// <see cref="ByteString"/>.
        /// </summary>
        /// <param name="raw">Encoded raw byte representation of an Akka PDU</param>
        /// <returns>Class representation of a PDU that can be used in a <see cref="PatternMatch"/>.</returns>
        public abstract IAkkaPdu DecodePdu(ByteString raw);

        /// <summary>
        /// Takes an <see cref="IAkkaPdu"/> representation of an Akka PDU and returns its encoded form
        /// as a <see cref="ByteString"/>.
        /// </summary>
        /// <param name="pdu"></param>
        /// <returns></returns>
        public virtual ByteString EncodePdu(IAkkaPdu pdu)
        {
            ByteString finalBytes = null;
            pdu.Match()
                .With<Associate>(a => finalBytes = ConstructAssociate(a.Info))
                .With<Payload>(p => finalBytes = ConstructPayload(p.Bytes))
                .With<Disassociate>(d => finalBytes = ConstructDisassociate(d.Reason))
                .With<Heartbeat>(h => finalBytes = ConstructHeartbeat());
            
            return finalBytes;
        }

        public abstract ByteString ConstructPayload(ByteString payload);

        public abstract ByteString ConstructAssociate(HandshakeInfo info);

        public abstract ByteString ConstructDisassociate(DisassociateInfo reason);

        public abstract ByteString ConstructHeartbeat();

        public abstract ByteString ConstructMessage(Address localAddress, ActorRef recipient,
            SerializedMessage serializedMessage, ActorRef senderOption = null);
    }

    public class AkkaPduProtobuffCodec : AkkaPduCodecBase
    {
        public override IAkkaPdu DecodePdu(ByteString raw)
        {
            throw new NotImplementedException();
        }

        public override ByteString ConstructPayload(ByteString payload)
        {
            throw new NotImplementedException();
        }

        public override ByteString ConstructAssociate(HandshakeInfo info)
        {
            throw new NotImplementedException();
            //var handshakeInfo = AkkaHandshakeInfo.CreateBuilder().SetOrigin()
        }

        public override ByteString ConstructDisassociate(DisassociateInfo reason)
        {
            throw new NotImplementedException();
        }

        public override ByteString ConstructHeartbeat()
        {
            throw new NotImplementedException();
        }

        public override ByteString ConstructMessage(Address localAddress, ActorRef recipient, SerializedMessage serializedMessage,
            ActorRef senderOption = null)
        {
            var envelopeBuilder = RemoteEnvelope.CreateBuilder();
            envelopeBuilder.SetRecipient(SerializeActorRef(recipient.Path.Address, recipient));
            if(senderOption != null){ envelopeBuilder.SetSender(SerializeActorRef(localAddress, senderOption)); }
            envelopeBuilder.SetMessage(serializedMessage);

            return envelopeBuilder.Build().ToByteString();
        }

        #region Internal methods

        private ActorRefData SerializeActorRef(Address defaultAddress, ActorRef actorRef)
        {
            return ActorRefData.CreateBuilder()
                .SetPath(!string.IsNullOrEmpty(actorRef.Path.Address.Host)
                    ? actorRef.Path.ToSerializationFormat()
                    : actorRef.Path.ToStringWithAddress(defaultAddress))
                .Build();
        }

        private AddressData SerializeAddress(Address address)
        {
            if (string.IsNullOrEmpty(address.Host) || !address.Port.HasValue) throw new ArgumentException(string.Format("Address {0} could not be serialized: host or port missing", address));
            return AddressData.CreateBuilder()
                .SetHostname(address.Host)
                .SetPort((uint) address.Port.Value)
                .SetSystem(address.System)
                .SetProtocol(address.Protocol)
                .Build();
        }

        #endregion
    }
}
