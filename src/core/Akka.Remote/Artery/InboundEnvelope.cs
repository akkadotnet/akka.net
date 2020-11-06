using Akka.Actor;
using Akka.Remote.Artery.Interfaces;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal static class InboundEnvelope
    {
        public static IInboundEnvelope Create(
            Option<IInternalActorRef> recipient,
            Option<IActorRef> sender,
            long originUid,
            int serializer,
            string classManifest,
            byte flags,
            // ARTERY: EnvelopeBuffer not implemented yet
            // EnvelopeBuffer envelopeBuffer, 
            Option<IOutboundContext> association,
            int lane)
        {
            return ReusableInboundEnvelope.Create(
                recipient,
                sender,
                originUid,
                serializer,
                classManifest,
                flags,
                // ARTERY: EnvelopeBuffer not implemented yet
                // envelopeBuffer,
                association,
                lane);
        }
    }

    internal class ReusableInboundEnvelope : IInboundEnvelope
    {
        public static ObjectPool<ReusableInboundEnvelope> CreateObjectPool(int capacity)
            => new ObjectPool<ReusableInboundEnvelope>(capacity, create: () => new ReusableInboundEnvelope(), clear: env => env.Clear());

        public Option<IInternalActorRef> Recipient { get; private set; }

        public Option<IActorRef> Sender { get; private set; }

        public long OriginUid { get; private set; }

        public Option<IOutboundContext> Association { get; private set; }

        public int Serializer { get; private set; }

        public string ClassManifest { get; private set; }

        public object Message { get; private set; }

        // ARTERY: EnvelopeBuffer not implemented yet
        // public EnvelopeBuffer EnvelopeBuffer { get; private set; }

        public byte Flags { get; private set; }

        // ARTERY: ByteFlag not implemented yet
        // public bool Flag(ByteFlag byteFlag);

        public int Lane { get; private set; }

        public IInboundEnvelope WithMessage(object message)
        {
            Message = message;
            return this;
        }

        public IInboundEnvelope ReleaseEnvelopeBuffer()
        {
            // ARTERY: EnvelopeBuffer not implemented yet
            // EnvelopeBuffer = null;
            return this;
        }

        public IInboundEnvelope WithRecipient(IInternalActorRef @ref)
        {
            Recipient = new Option<IInternalActorRef>(@ref);
            return this;
        }

        internal void Clear()
        {
            Recipient = Option<IInternalActorRef>.None;
            Message = null;
            Sender = Option<IActorRef>.None;
            OriginUid = 0;
            Association = Option<IOutboundContext>.None;
            Lane = 0;
        }

        private ReusableInboundEnvelope(
            Option<IInternalActorRef> recipient,
            Option<IActorRef> sender,
            long originUid,
            int serializer,
            string classManifest,
            byte flags,
            // ARTERY: EnvelopeBuffer not implemented yet
            // EnvelopeBuffer envelopeBuffer, 
            Option<IOutboundContext> association,
            int lane)
        {
            Recipient = recipient;
            Sender = sender;
            OriginUid = originUid;
            Serializer = serializer;
            ClassManifest = classManifest;
            Flags = flags;
            // ARTERY: EnvelopeBuffer not implemented yet
            // EnvelopeBuffer = envelopeBuffer;
            Association = association;
            Lane = lane;
        }

        public static ReusableInboundEnvelope Create(
            Option<IInternalActorRef> recipient,
            Option<IActorRef> sender,
            long originUid,
            int serializer,
            string classManifest,
            byte flags,
            // ARTERY: EnvelopeBuffer not implemented yet
            // EnvelopeBuffer envelopeBuffer, 
            Option<IOutboundContext> association,
            int lane)
        {
            return new ReusableInboundEnvelope(
                    recipient,
                    sender,
                    originUid,
                    serializer,
                    classManifest,
                    flags,
                    // ARTERY: EnvelopeBuffer not implemented yet
                    // buf,
                    association,
                    lane);
        }

        // ARTERY: EnvelopeBuffer not implemented yet
        /*
        internal IInboundEnvelope WithEnvelopeBuffer(EnvelopeBuffer envelopeBuffer)
        {
            EnvelopeBuffer = envelopeBuffer;
            return this;
        }
        */

        public IInboundEnvelope CopyForLane(int lane)
        {
            //var buf = EnvelopeBuffer?.Copy();
            return new ReusableInboundEnvelope(
                    Recipient,
                    Sender,
                    OriginUid,
                    Serializer,
                    ClassManifest,
                    Flags,
                    // ARTERY: EnvelopeBuffer not implemented yet
                    // buf,
                    Association,
                    lane)
                .WithMessage(Message);
        }

        public override string ToString()
            => $"IInboundEnvelope({Recipient}, {Message}, {Sender}, {OriginUid}, {Association})";
    }
}
