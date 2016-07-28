//-----------------------------------------------------------------------
// <copyright file="IAutoReceivedMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;

namespace Akka.Actor
{
    public interface IAutoReceivedMessage : INoSerializationVerificationNeeded
    {
    }

    public sealed class
        Terminated : IAutoReceivedMessage, IPossiblyHarmful, IDeadLetterSuppression
    {
        public Terminated(IActorRef actorRef, bool existenceConfirmed, bool addressTerminated)
        {
            ActorRef = actorRef;
            ExistenceConfirmed = existenceConfirmed;
            AddressTerminated = addressTerminated;
        }

        public IActorRef ActorRef { get; private set; }


        public bool AddressTerminated { get; private set; }

        public bool ExistenceConfirmed { get; private set; }

        public override string ToString()
        {
            return "<Terminated>: " + ActorRef + " - ExistenceConfirmed=" + ExistenceConfirmed;
        }
    }

    /// <summary>
    /// Request to an <see cref="ICanTell"/> to get back the identity of the underlying actors.
    /// </summary>
    public sealed class Identify : IAutoReceivedMessage
    {
        public Identify(object messageId)
        {
            MessageId = messageId;
        }

        /// <summary>
        /// A correlating ID used to distinguish multiple <see cref="Identify"/> requests to the same receiver.
        /// </summary>
        public object MessageId { get; private set; }

        public override string ToString()
        {
            return "<Identify>: " + MessageId;
        }
    }

    /// <summary>
    /// Response to the <see cref="Identify"/> message, get identity by Sender
    /// </summary>
    public sealed class ActorIdentity
    {
        public ActorIdentity(object messageId, IActorRef subject)
        {
            MessageId = messageId;
            Subject = subject;
        }

        /// <summary>
        /// The same correlating ID used in the original <see cref="Identify"/> message.
        /// </summary>
        public object MessageId { get; private set; }

        /// <summary>
        /// A reference to the underyling actor.
        /// </summary>
        /// <remarks>Will be <c>null</c> if sent an <see cref="ActorSelection"/> that could not be resolved.</remarks>
        public IActorRef Subject { get; private set; }

        public override string ToString()
        {
            return "<ActorIdentity>: " + Subject + " - MessageId=" + MessageId;
        }
    }

    /// <summary>
    /// Sending a <see cref="PoisonPill"/> to an will stop the actor when the message 
    /// is processed. <see cref="PoisonPill"/> is enqueued as ordinary messages and will be handled after 
    /// messages that were already queued in the mailbox.
    /// <para>See also <see cref="Kill"/> which causes the actor to throw an  <see cref="ActorKilledException"/> when 
    /// it processes the message, which gets handled using the normal supervisor mechanism, and
    /// <see cref="IActorContext.Stop"/> which causes the actor to stop without processing any more messages. </para>
    /// </summary>
    public sealed class PoisonPill : IAutoReceivedMessage, IPossiblyHarmful, IDeadLetterSuppression
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

        public override string ToString()
        {
            return "<PoisonPill>";
        }
    }


    /// <summary>
    /// Sending an <see cref="Kill"/> message to an actor causes the actor to throw an 
    /// <see cref="ActorKilledException"/> when it processes the message, which gets handled using the normal supervisor mechanism.
    /// <para>See also <see cref="PoisonPill"/> which causes the actor to stop when the <see cref="PoisonPill"/>
    /// is processed, without throwing an exception, and 
    /// <see cref="IActorContext.Stop"/> which causes the actor to stop without processing any more messages. </para>
    /// </summary>
    public sealed class Kill : IAutoReceivedMessage
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

        public override string ToString()
        {
            return "<Kill>";
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
    internal class AddressTerminated : IAutoReceivedMessage, IPossiblyHarmful, IDeadLetterSuppression
    {
        public AddressTerminated(Address address)
        {
            Address = address;
        }

        public Address Address { get; private set; }

        public override string ToString()
        {
            return "<AddressTerminated>: " + Address;
        }
    }
}

