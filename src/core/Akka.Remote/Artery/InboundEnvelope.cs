using Akka.Actor;
using Akka.Remote.Artery.Utils;

namespace Akka.Remote.Artery
{
    internal static class InboundEnvelope
    {
        /// <summary>
        /// Only used in tests
        /// </summary>
        /// <param name="recipient"></param>
        /// <param name="message"></param>
        /// <param name="sender"></param>
        /// <param name="originUid"></param>
        /// <param name="association"></param>
        /// <returns></returns>
        public static IInboundEnvelope Create(
            IOptionVal<IInternalActorRef> recipient,
            object message,
            IOptionVal<IActorRef> sender,
            long originUid,
            IOptionVal<IOutboundContext> association)
            => new ReusableInboundEnvelope()
                .Init(recipient, sender, originUid, -1, "", 0, null, association, 0)
                .WithMessage(message);
    }

    internal interface IInboundEnvelope : INoSerializationVerificationNeeded
    {
        IOptionVal<IInternalActorRef> Recipient { get; }
        IOptionVal<IActorRef> Sender { get; }
        long OriginUid { get; }
        IOptionVal<IOutboundContext> Association { get; }

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

    internal class ReusableInboundEnvelope : IInboundEnvelope
    {
        public static ObjectPool<ReusableInboundEnvelope> CreateObjectPool(int capacity)
            => new ObjectPool<ReusableInboundEnvelope>(
                capacity, 
                create: () => new ReusableInboundEnvelope(), 
                clear: env => env.Clear());

        public IOptionVal<IInternalActorRef> Recipient { get; private set; } = OptionVal.None<IInternalActorRef>();
        public IOptionVal<IActorRef> Sender { get; private set; } = OptionVal.None<IActorRef>();
        public long OriginUid { get; private set; } = 0L;
        public IOptionVal<IOutboundContext> Association { get; private set; } = OptionVal.None<IOutboundContext>();
        public int Serializer { get; private set; } = 01;
        public string ClassManifest { get; private set; } = null;
        public byte Flags { get; private set; } = (byte)0;
        public int Lane { get; private set; } = 0;
        public object Message { get; private set; } = null;
        public EnvelopeBuffer EnvelopeBuffer { get; private set; } = null;

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
            Recipient = OptionVal.Apply(@ref);
            return this;
        }

        public void Clear()
        {
            Recipient = OptionVal.None<IInternalActorRef>();
            Message = null;
            Sender = OptionVal.None<IActorRef>();
            OriginUid = 0;
            Association = OptionVal.None<IOutboundContext>();
            Lane = 0;
        }

        public IInboundEnvelope Init(
            IOptionVal<IInternalActorRef> recipient,
            IOptionVal<IActorRef> sender,
            long originUid,
            int serializer,
            string classManifest,
            byte flags,
            EnvelopeBuffer envelopeBuffer,
            IOptionVal<IOutboundContext> association,
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
            var buf = EnvelopeBuffer?.Copy();
            var env = new ReusableInboundEnvelope();
            return env.Init(Recipient, Sender, OriginUid, Serializer, ClassManifest, Flags, buf, Association, Lane)
                .WithMessage(Message);
        }

        public override string ToString()
            => $"IInboundEnvelope({Recipient}, {Message}, {Sender}, {OriginUid}, {Association})";
    }
}
