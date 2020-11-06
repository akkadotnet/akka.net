using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Remote.Artery.Utils;
using Akka.Util;

namespace Akka.Remote.Artery.Interfaces
{
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

}
