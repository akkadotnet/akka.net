using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Util;

namespace Akka.Remote.Artery.Interfaces
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IOutboundEnvelope : INoSerializationVerificationNeeded
    {
        Option<RemoteActorRef> Recipient { get; }
        object Message { get; }
        Option<IActorRef> Sender { get; }

        IOutboundEnvelope WithMessage(object message);
        IOutboundEnvelope Copy();
    }

}
