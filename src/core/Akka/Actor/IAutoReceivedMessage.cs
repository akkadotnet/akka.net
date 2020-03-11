//-----------------------------------------------------------------------
// <copyright file="IAutoReceivedMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Marker trait to show which Messages are automatically handled by Akka.NET
    /// </summary>
    public interface IAutoReceivedMessage
    {
    }

    /// <summary>
    /// When Death Watch is used, the watcher will receive a Terminated(watched) message when watched is terminated.
    /// Terminated message can't be forwarded to another actor, since that actor might not be watching the subject.
    /// Instead, if you need to forward Terminated to another actor you should send the information in your own message.
    /// </summary>
    public sealed class Terminated : IAutoReceivedMessage, IPossiblyHarmful, IDeadLetterSuppression, INoSerializationVerificationNeeded, IEquatable<Terminated>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Terminated" /> class.
        /// </summary>
        /// <param name="actorRef">the watched actor that terminated</param>
        /// <param name="existenceConfirmed">is false when the Terminated message was not sent directly from the watched actor, but derived from another source, such as when watching a non-local ActorRef, which might not have been resolved</param>
        /// <param name="addressTerminated">the Terminated message was derived from that the remote node hosting the watched actor was detected as unreachable</param>
        public Terminated(IActorRef actorRef, bool existenceConfirmed, bool addressTerminated)
        {
            ActorRef = actorRef;
            ExistenceConfirmed = existenceConfirmed;
            AddressTerminated = addressTerminated;
        }

        /// <summary>
        /// The watched actor that terminated
        /// </summary>
        public IActorRef ActorRef { get; }

        /// <summary>
        /// Is false when the Terminated message was not sent
        /// directly from the watched actor, but derived from another source, such as 
        /// when watching a non-local ActorRef, which might not have been resolved
        /// </summary>
        public bool AddressTerminated { get; }

        /// <summary>
        /// The Terminated message was derived from that the remote node hosting the watched actor was detected as unreachable
        /// </summary>
        public bool ExistenceConfirmed { get; }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="string" /> that represents this instance.</returns>
        public override string ToString()
        {
            return $"<Terminated>: {ActorRef} - ExistenceConfirmed={ExistenceConfirmed}";
        }

        public bool Equals(Terminated other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(ActorRef, other.ActorRef) && AddressTerminated == other.AddressTerminated && ExistenceConfirmed == other.ExistenceConfirmed;
        }

        public override bool Equals(object obj) => obj is Terminated terminated && Equals(terminated);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (ActorRef != null ? ActorRef.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ AddressTerminated.GetHashCode();
                hashCode = (hashCode * 397) ^ ExistenceConfirmed.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Request to an <see cref="ICanTell"/> to get back the identity of the underlying actors.
    /// </summary>
    public sealed class Identify : IAutoReceivedMessage, INotInfluenceReceiveTimeout
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Identify" /> class.
        /// </summary>
        /// <param name="messageId">A correlating ID used to distinguish multiple <see cref="Identify"/> requests to the same receiver.</param>
        public Identify(object messageId)
        {
            MessageId = messageId;
        }

        /// <summary>
        /// A correlating ID used to distinguish multiple <see cref="Identify"/> requests to the same receiver.
        /// </summary>
        public object MessageId { get; }

        #region Equals
        private bool Equals(Identify other)
        {
            return Equals(MessageId, other.MessageId);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Identify && Equals((Identify)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (MessageId != null ? MessageId.GetHashCode() : 0);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"<Identify>: {MessageId}";
        }
        #endregion
    }

    /// <summary>
    /// Response to the <see cref="Identify"/> message, get identity by Sender
    /// </summary>
    public sealed class ActorIdentity
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorIdentity" /> class.
        /// </summary>
        /// <param name="messageId">The same correlating ID used in the original <see cref="Identify"/> message.</param>
        /// <param name="subject">A reference to the underyling actor.</param>
        public ActorIdentity(object messageId, IActorRef subject)
        {
            MessageId = messageId;
            Subject = subject;
        }

        /// <summary>
        /// The same correlating ID used in the original <see cref="Identify"/> message.
        /// </summary>
        public object MessageId { get; }

        /// <summary>
        /// A reference to the underyling actor.
        /// </summary>
        /// <remarks>Will be <c>null</c> if sent an <see cref="ActorSelection"/> that could not be resolved.</remarks>
        public IActorRef Subject { get; }

        #region Equals
        private bool Equals(ActorIdentity other)
        {
            return Equals(MessageId, other.MessageId) && Equals(Subject, other.Subject);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is ActorIdentity && Equals((ActorIdentity)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((MessageId != null ? MessageId.GetHashCode() : 0) * 397) ^ (Subject != null ? Subject.GetHashCode() : 0);
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"<ActorIdentity>: {Subject} - MessageId={MessageId}";
        }
        #endregion
    }

    /// <summary>
    /// Sending a <see cref="PoisonPill"/> to an actor will stop the actor when the message 
    /// is processed. <see cref="PoisonPill"/> is enqueued as ordinary messages and will be handled after 
    /// messages that were already queued in the mailbox.
    /// <para>See also <see cref="Kill"/> which causes the actor to throw an  <see cref="ActorKilledException"/> when 
    /// it processes the message, which gets handled using the normal supervisor mechanism, and
    /// <see cref="IActorContext.Stop"/> which causes the actor to stop without processing any more messages. </para>
    /// </summary>
    public sealed class PoisonPill : IAutoReceivedMessage, IPossiblyHarmful, IDeadLetterSuppression
    {
        private PoisonPill() { }

        /// <summary>
        /// The singleton instance of PoisonPill.
        /// </summary>
        public static PoisonPill Instance { get; } = new PoisonPill();

        /// <inheritdoc/>
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

        /// <summary>
        /// The singleton instance of Kill.
        /// </summary>
        public static Kill Instance { get; } = new Kill();

        /// <inheritdoc/>
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
        /// <summary>
        /// Initializes a new instance of the <see cref="AddressTerminated" /> class.
        /// </summary>
        /// <param name="address">TBD</param>
        public AddressTerminated(Address address)
        {
            Address = address;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Address { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"<AddressTerminated>: {Address}";
        }
    }
}
