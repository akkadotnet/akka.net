using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal interface IInboundEnvelope : INoSerializationVerificationNeeded
    {
        Option<IInternalActorRef> Recipient { get; }
        Option<IActorRef> Sender { get; }
        long OriginUid { get; }
        Option<OutboundContext> Association { get; }

        int Serializer { get; }
        string ClassManifest { get; }
        object Message { get; }
        EnvelopeBuffer EnvelopeBuffer { get; }

        byte Flags { get; }
        bool Flag(ByteFlag byteFlag);

        IInboundEnvelope WithMessage(object message);

        IInboundEnvelope ReleaseEnvelopeBuffer();

        IInboundEnvelope WithRecipient(IInternalActorRef @ref);
        IInboundEnvelope WithEnvelopeBuffer(EnvelopeBuffer envelopeBuffer);

        int Lane { get; }
        IInboundEnvelope CopyForLane(int lane);
    }

    internal static class InboundEnvelope
    {
        public static IInboundEnvelope Create(
            Option<IInternalActorRef> recipient,
            object message,
            Option<IActorRef> sender,
            long originUid,
            Option<OutboundContext> association)
            => new ReusableInboundEnvelope()
                .Init(recipient, sender, originUid, -1, "", 0, null, association, 0)
                .WithMessage(message);
    }

    internal class ReusableInboundEnvelope : IInboundEnvelope
    {
        // ARTERY: ObjectPool isn't implemented yet
        //public static ObjectPool<ReusableInboundEnvelope> CreateObjectPool(int capacity)
        //    => new ObjectPool<ReusableInboundEnvelope>(capacity, create: () => new ReusableInboundEnvelope(), clear: env => env.clear());

        public Option<IInternalActorRef> Recipient { get; private set; }
        public Option<IActorRef> Sender { get; private set; }
        public long OriginUid { get; private set; }
        public Option<OutboundContext> Association { get; private set; }
        public int Serializer { get; private set; }
        public string ClassManifest { get; private set; }
        public object Message { get; private set; }
        public EnvelopeBuffer EnvelopeBuffer { get; private set; }
        public byte Flags { get; private set; }
        public int Lane { get; private set; }

        public bool Flag(ByteFlag byteFlag) => byteFlag.IsEnabled(Flags);

        public IInboundEnvelope WithMessage(object message)
        {
            Message = message;
            return this;
        }

        public IInboundEnvelope ReleaseEnvelopeBuffer()
        {
            EnvelopeBuffer = null;
            return this;
        }

        public IInboundEnvelope WithRecipient(IInternalActorRef @ref)
        {
            Recipient = new Option<IInternalActorRef>(@ref);
            return this;
        }

        public void Clear()
        {
            Recipient = Option<IInternalActorRef>.None;
            Message = null;
            Sender = Option<IActorRef>.None;
            OriginUid = 0;
            Association = Option<OutboundContext>.None;
            Lane = 0;
        }

        public IInboundEnvelope Init(
            Option<IInternalActorRef> recipient,
            Option<IActorRef> sender,
            long originUid,
            int serializer,
            string classManifest,
            byte flags,
            EnvelopeBuffer envelopeBuffer,
            Option<OutboundContext> association,
            int lane)
        {
            Recipient = recipient;
            Sender = sender;
            OriginUid = originUid;
            Serializer = serializer;
            ClassManifest = classManifest;
            Flags = flags;
            EnvelopeBuffer = envelopeBuffer;
            Association = association;
            Lane = lane;

            return this;
        }

        public IInboundEnvelope WithEnvelopeBuffer(EnvelopeBuffer envelopeBuffer)
        {
            EnvelopeBuffer = envelopeBuffer;
            return this;
        }

        public IInboundEnvelope CopyForLane(int lane)
        {
            var buf = EnvelopeBuffer is null ? null : EnvelopeBuffer.Copy();
            var env = new ReusableInboundEnvelope();
            return env
                .Init(Recipient, Sender, OriginUid, Serializer, ClassManifest, Flags, buf, Association, lane)
                .WithMessage(Message);
        }

        public override string ToString()
            => $"InboundEnvelope({Recipient}, {Message}, {Sender}, {OriginUid}, {Association}";
    }
}
