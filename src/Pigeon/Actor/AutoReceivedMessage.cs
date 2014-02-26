using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public abstract class AutoReceivedMessage : NoSerializationVerificationNeeded
    {
    }

    public class Terminated : AutoReceivedMessage
    {
        public ActorRef actorRef { get; private set; }

        public Terminated(ActorRef actorRef)
        {
            this.actorRef = actorRef;
        }
    }

    //request to an actor ref, to get back the identity of the underlying actors
    public class Identify : AutoReceivedMessage
    {
        public Identify(Guid messageId)
        {
            MessageId = messageId;
        }
        public Guid MessageId { get; private set; }
    }

    //response to the Identity message, get identity by Sender
    public class ActorIdentity : AutoReceivedMessage
    {
        public Guid MessageId { get; private set; }
        public ActorRef Subject { get; private set; }

        public ActorIdentity(Guid messageId, ActorRef subject)
        {
            this.MessageId = messageId;
            this.Subject = subject;
        }
    }

    public class PoisonPill : AutoReceivedMessage
    {
    }
    public class Kill : AutoReceivedMessage
    {
    }
}
