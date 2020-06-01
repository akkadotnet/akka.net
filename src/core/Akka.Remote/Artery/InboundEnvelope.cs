using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Util;

// ARTERY: Incomplete implementation
namespace Akka.Remote.Artery
{
    internal interface IInboundEnvelope : INoSerializationVerificationNeeded
    {
        Option<IInternalActorRef> InternalActorRef { get; }
        Option<IActorRef> Sender { get; }
        long OriginUid { get; }
        Option<IOutboundContext> Association { get; }

        int Serializer { get; }
        string ClassManifest { get; }
        object Message { get; }
        // ARTERY: EnvelopeBuffer not implemented yet
        // EnvelopeBuffer EnvelopeBuffer { get; }

        byte Flags { get; }
        // ARTERY: ByteFlag not implemented yet
        // bool Flag(ByteFlag byteFlag);

        IInboundEnvelope WithMessage(object message);

        IInboundEnvelope ReleaseEnvelopeBuffer();

        IInboundEnvelope WithRecipient(IInternalActorRef @ref);

        int Lane { get; }
        IInboundEnvelope CopyForLane(int lane);
    }
}
