using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Remote.Artery.Utils;
using Akka.Util;

namespace Akka.Remote.Artery.Interfaces
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IOutboundEnvelope : INoSerializationVerificationNeeded
    {
        IOptionVal<RemoteActorRef> Recipient { get; }
        object Message { get; }
        IOptionVal<IActorRef> Sender { get; }

        IOutboundEnvelope WithMessage(object message);
        IOutboundEnvelope Copy();
    }
}
