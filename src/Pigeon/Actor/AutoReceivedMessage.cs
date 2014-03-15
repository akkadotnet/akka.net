using System;

namespace Akka.Actor
{
    public abstract class AutoReceivedMessage : NoSerializationVerificationNeeded
    {
    }

    public class Terminated : AutoReceivedMessage
    {
        public Terminated(ActorRef actorRef, bool existenceConfirmed, bool addressTerminated)
        {
            ActorRef = actorRef;
            ExistenceConfirmed = existenceConfirmed;
            AddressTerminated = addressTerminated;
        }

        public ActorRef ActorRef { get; private set; }


        public bool AddressTerminated { get; private set; }

        public bool ExistenceConfirmed { get; private set; }

        public override string ToString()
        {
            return string.Format("Terminated {0}", ActorRef);
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
        public ActorIdentity(Guid messageId, ActorRef subject)
        {
            MessageId = messageId;
            Subject = subject;
        }

        public Guid MessageId { get; private set; }
        public ActorRef Subject { get; private set; }
    }

    public class PoisonPill : AutoReceivedMessage
    {
    }

    public class Kill : AutoReceivedMessage
    {
    }
}