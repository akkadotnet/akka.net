using System;
using Akka.Event;

namespace Akka.Actor
{
    public interface AutoReceivedMessage : NoSerializationVerificationNeeded
    {
    }

    public sealed class Terminated : AutoReceivedMessage, PossiblyHarmful
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
            return string.Format("Terminated - ActorRef: {0} - ExistenceConfirmed: {1}", ActorRef,ExistenceConfirmed);
        }
    }

    //request to an actor ref, to get back the identity of the underlying actors
    public sealed class Identify : AutoReceivedMessage
    {
        public Identify(object messageId)
        {
            MessageId = messageId;
        }

        public object MessageId { get; private set; }
    }

    //response to the Identity message, get identity by Sender
    public sealed class ActorIdentity
    {
        public ActorIdentity(object messageId, ActorRef subject)
        {
            MessageId = messageId;
            Subject = subject;
        }

        public object MessageId { get; private set; }
        public ActorRef Subject { get; private set; }
    }

    public sealed class PoisonPill : AutoReceivedMessage 
    {
        private PoisonPill() { }
        private static readonly PoisonPill _instance = new PoisonPill();
        public static PoisonPill Instance
        {
            get
            {
                return _instance;
            }
        }
    }

    public sealed class Kill : AutoReceivedMessage
    {
        private Kill() { }

        private static readonly Kill _instance = new Kill();
        public static Kill Instance
        {
            get
            {
                return _instance;
            }
        }
    }



    /// <summary>
    /// INTERNAL API
    /// 
    /// Used for remote death watch. Failure detectors publish this to the
    /// <see cref="AddressTerminatedTopic"/> when a remote node is detected to be unreachable and / or decided
    /// to be removed.
    /// 
    /// The watcher <see cref="DeathWatch"/> subscribes to the <see cref="AddressTerminatedTopic"/> and translates this
    /// event to <see cref="Terminated"/>, which is sent to itself.
    /// </summary>
    internal class AddressTerminated : AutoReceivedMessage, PossiblyHarmful
    {
        public AddressTerminated(Address address)
        {
            Address = address;
        }

        public Address Address { get; private set; }
    }
}