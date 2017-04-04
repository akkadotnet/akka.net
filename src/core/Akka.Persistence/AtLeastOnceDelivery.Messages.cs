//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDelivery.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    /// <summary>
    /// Snapshot of current <see cref="AtLeastOnceDeliveryActor" /> state. Can be retrieved with
    /// <see cref="AtLeastOnceDeliverySemantic.GetDeliverySnapshot" /> and saved with
    /// <see cref="Eventsourced.SaveSnapshot" />.
    /// During recovery the snapshot received in <see cref="SnapshotOffer" /> should be sent with
    /// <see cref="AtLeastOnceDeliverySemantic.SetDeliverySnapshot" />.
    /// </summary>
    public sealed class AtLeastOnceDeliverySnapshot : IMessage, IEquatable<AtLeastOnceDeliverySnapshot>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AtLeastOnceDeliverySnapshot"/> class.
        /// </summary>
        /// <param name="currentDeliveryId">TBD</param>
        /// <param name="unconfirmedDeliveries">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        public AtLeastOnceDeliverySnapshot(long currentDeliveryId, UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            if (unconfirmedDeliveries == null)
                throw new ArgumentNullException(nameof(unconfirmedDeliveries),
                    "AtLeastOnceDeliverySnapshot expects not null array of unconfirmed deliveries");

            CurrentDeliveryId = currentDeliveryId;
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public long CurrentDeliveryId { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public UnconfirmedDelivery[] UnconfirmedDeliveries { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(AtLeastOnceDeliverySnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CurrentDeliveryId, other.CurrentDeliveryId)
                   && UnconfirmedDeliveries.SequenceEqual(other.UnconfirmedDeliveries);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as AtLeastOnceDeliverySnapshot);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = CurrentDeliveryId.GetHashCode();
                hashCode = (hashCode*397) ^ (UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return $"AtLeastOnceDeliverySnapshot<currentDeliveryId: {CurrentDeliveryId}, unconfirmedDeliveries: {UnconfirmedDeliveries.Length}>";
        }
    }

    /// <summary>
    /// Message should be sent after <see cref="AtLeastOnceDeliverySemantic.WarnAfterNumberOfUnconfirmedAttempts" />
    /// limit will is reached.
    /// </summary>
    [Serializable]
    public sealed class UnconfirmedWarning : IEquatable<UnconfirmedWarning>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnconfirmedWarning"/> class.
        /// </summary>
        /// <param name="unconfirmedDeliveries">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        public UnconfirmedWarning(UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            if (unconfirmedDeliveries == null)
                throw new ArgumentNullException(nameof(unconfirmedDeliveries),
                    "UnconfirmedWarning expects not null array of unconfirmed deliveries");

            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public UnconfirmedDelivery[] UnconfirmedDeliveries { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(UnconfirmedWarning other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(UnconfirmedDeliveries, other.UnconfirmedDeliveries);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as UnconfirmedWarning);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return (UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return $"UnconfirmedWarning<unconfirmedDeliveries: {UnconfirmedDeliveries.Length}>";
        }
    }

    /// <summary>
    /// Contains details about unconfirmed messages.
    /// It's included inside <see cref="UnconfirmedWarning" /> and <see cref="AtLeastOnceDeliverySnapshot" />.
    /// <see cref="AtLeastOnceDeliverySemantic.WarnAfterNumberOfUnconfirmedAttempts" />
    /// </summary>
    [Serializable]
    public sealed class UnconfirmedDelivery : IEquatable<UnconfirmedDelivery>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnconfirmedDelivery"/> class.
        /// </summary>
        /// <param name="deliveryId">TBD</param>
        /// <param name="destination">TBD</param>
        /// <param name="message">TBD</param>
        public UnconfirmedDelivery(long deliveryId, ActorPath destination, object message)
        {
            DeliveryId = deliveryId;
            Destination = destination;
            Message = message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public long DeliveryId { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ActorPath Destination { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(UnconfirmedDelivery other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(DeliveryId, other.DeliveryId)
                   && Equals(Destination, other.Destination)
                   && Equals(Message, other.Message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as UnconfirmedDelivery);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DeliveryId.GetHashCode();
                hashCode = (hashCode*397) ^ (Destination != null ? Destination.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (Message != null ? Message.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return $"UnconfirmedDelivery<deliveryId: {DeliveryId}, dest: {Destination}, message: {Message}>";
        }
    }

    /// <summary>
    /// This exception is thrown when the <see cref="AtLeastOnceDeliverySemantic.MaxUnconfirmedMessages" /> threshold has been exceeded.
    /// </summary>
    public class MaxUnconfirmedMessagesExceededException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException"/> class.
        /// </summary>
        public MaxUnconfirmedMessagesExceededException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public MaxUnconfirmedMessagesExceededException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException"/> class.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public MaxUnconfirmedMessagesExceededException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected MaxUnconfirmedMessagesExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    internal sealed class Delivery : IEquatable<Delivery>
    {
        public Delivery(ActorPath destination, object message, DateTime timestamp, int attempt)
        {
            Destination = destination;
            Message = message;
            Timestamp = timestamp;
            Attempt = attempt;
        }

        public int Attempt { get; }

        public ActorPath Destination { get; }

        public object Message { get; }

        public DateTime Timestamp { get; }

        public bool Equals(Delivery other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Attempt, other.Attempt)
                   && Equals(Timestamp, other.Timestamp)
                   && Equals(Destination, other.Destination)
                   && Equals(Message, other.Message);
        }

        public Delivery IncrementedCopy()
        {
            return new Delivery(Destination, Message, Timestamp, Attempt + 1);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Delivery);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (Destination != null ? Destination.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Timestamp.GetHashCode();
                hashCode = (hashCode * 397) ^ Attempt;
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"Delivery<dest: {Destination}, attempt: {Attempt}, timestamp: {Timestamp}, message: {Message}";
        }
    }

    [Serializable]
    internal sealed class RedeliveryTick : INotInfluenceReceiveTimeout
    {
        public static RedeliveryTick Instance { get; } = new RedeliveryTick();

        private RedeliveryTick() { }
    }
}