//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliverySemantic.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    #region Messages

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
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="unconfirmedDeliveries"/> array is undefined.
        /// </exception>
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

        /// <inheritdoc/>
        public bool Equals(AtLeastOnceDeliverySnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CurrentDeliveryId, other.CurrentDeliveryId)
                   && UnconfirmedDeliveries.SequenceEqual(other.UnconfirmedDeliveries);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as AtLeastOnceDeliverySnapshot);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = CurrentDeliveryId.GetHashCode();
                hashCode = (hashCode * 397) ^ (UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"AtLeastOnceDeliverySnapshot<currentDeliveryId: {CurrentDeliveryId}, unconfirmedDeliveries: {UnconfirmedDeliveries.Length}>";
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
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="unconfirmedDeliveries"/> array is undefined.
        /// </exception>
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

        /// <inheritdoc/>
        public bool Equals(UnconfirmedWarning other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(UnconfirmedDeliveries, other.UnconfirmedDeliveries);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as UnconfirmedWarning);

        /// <inheritdoc/>
        public override int GetHashCode() => UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0;

        /// <inheritdoc/>
        public override string ToString() => $"UnconfirmedWarning<unconfirmedDeliveries: {UnconfirmedDeliveries.Length}>";
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

        /// <inheritdoc/>
        public bool Equals(UnconfirmedDelivery other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(DeliveryId, other.DeliveryId)
                   && Equals(Destination, other.Destination)
                   && Equals(Message, other.Message);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as UnconfirmedDelivery);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DeliveryId.GetHashCode();
                hashCode = (hashCode * 397) ^ (Destination != null ? Destination.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"UnconfirmedDelivery<deliveryId: {DeliveryId}, dest: {Destination}, message: {Message}>";
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

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected MaxUnconfirmedMessagesExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    #endregion

    /// <summary>
    /// TBD
    /// </summary>
    public class AtLeastOnceDeliverySemantic
    {
        [Serializable]
        public sealed class Delivery : IEquatable<Delivery>
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

            public override bool Equals(object obj) => Equals(obj as Delivery);

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

            public override string ToString() => $"Delivery<dest: {Destination}, attempt: {Attempt}, timestamp: {Timestamp}, message: {Message}";
        }

        [Serializable]
        public sealed class RedeliveryTick : INotInfluenceReceiveTimeout, IDeadLetterSuppression
        {
            /// <summary>
            /// The singleton instance of the redelivery tick
            /// </summary>
            public static RedeliveryTick Instance { get; } = new RedeliveryTick();

            private RedeliveryTick() { }

            public override bool Equals(object obj) => obj is RedeliveryTick;
            public override int GetHashCode() => nameof(RedeliveryTick).GetHashCode();
        }

        #region actor methods

        private readonly IActorContext _context;
        private long _deliverySequenceNr;
        private ICancelable _redeliverScheduleCancelable;
        private ImmutableSortedDictionary<long, Delivery> _unconfirmed = ImmutableSortedDictionary<long, Delivery>.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="AtLeastOnceDeliverySemantic"/> class.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="settings">TBD</param>
        public AtLeastOnceDeliverySemantic(IActorContext context, PersistenceSettings.AtLeastOnceDeliverySettings settings)
        {
            _context = context;
            _deliverySequenceNr = 0;

            RedeliverInterval = settings.RedeliverInterval;
            RedeliveryBurstLimit = settings.RedeliveryBurstLimit;
            WarnAfterNumberOfUnconfirmedAttempts = settings.WarnAfterNumberOfUnconfirmedAttempts;
            MaxUnconfirmedMessages = settings.MaxUnconfirmedMessages;
        }

        /// <summary>
        /// Interval between redelivery attempts.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.redeliver-interval'
        /// configuration key.
        /// </summary>
        public TimeSpan RedeliverInterval { get; }

        /// <summary>
        /// Maximum number of unconfirmed messages that will be sent at each redelivery burst
        /// (burst frequency is half of the redelivery interval).
        /// If there's a lot of unconfirmed messages (e.g. if the destination is not available for a long time),
        /// this helps prevent an overwhelming amount of messages to be sent at once.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.redelivery-burst-limit'
        /// configuration key.
        /// </summary>
        public int RedeliveryBurstLimit { get; }

        /// <summary>
        /// After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        /// <see cref="ActorBase.Self" />. The count is reset after restart.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.warn-after-number-of-unconfirmed-attempts'
        /// configuration key.
        /// </summary>
        public int WarnAfterNumberOfUnconfirmedAttempts { get; }

        /// <summary>
        /// Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory.
        /// if this number is exceeded, <see cref="AtLeastOnceDeliverySemantic.Deliver" /> will not accept more
        /// messages and it will throw <see cref="MaxUnconfirmedMessagesExceededException" />.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.max-unconfirmed-messages'
        /// configuration key.
        /// </summary>
        public int MaxUnconfirmedMessages { get; }

        /// <summary>
        ///     Number of messages, that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount => _unconfirmed.Count;

        private void StartRedeliverTask()
        {
            if (_redeliverScheduleCancelable != null) return;
            var delay = new TimeSpan(RedeliverInterval.Ticks / 2);
            _redeliverScheduleCancelable = _context.System.Scheduler.ScheduleTellRepeatedlyCancelable(delay, RedeliverInterval, _context.Self,
                RedeliveryTick.Instance, _context.Self);
        }

        private long NextDeliverySequenceNr()
        {
            return (++_deliverySequenceNr);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="destination">TBD</param>
        /// <param name="deliveryMessageMapper">TBD</param>
        /// <param name="isRecovering">TBD</param>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        /// This exception is thrown when the actor exceeds the <see cref="MaxUnconfirmedMessages"/> count.
        /// </exception>
        public void Deliver(ActorPath destination, Func<long, object> deliveryMessageMapper, bool isRecovering)
        {
            if (_unconfirmed.Count >= MaxUnconfirmedMessages)
            {
                throw new MaxUnconfirmedMessagesExceededException(
                    $"{_context.Self} has too many unconfirmed messages. Maximum allowed is {MaxUnconfirmedMessages}");
            }

            long deliveryId = NextDeliverySequenceNr();
            DateTime now = isRecovering ? DateTime.UtcNow - RedeliverInterval : DateTime.UtcNow;
            var delivery = new Delivery(destination, deliveryMessageMapper(deliveryId), now, 0);

            if (isRecovering)
            {
                _unconfirmed = _unconfirmed.SetItem(deliveryId, delivery);
            }
            else
            {
                Send(deliveryId, delivery, now);
            }
        }

        /// <summary>
        /// Call this method when a message has been confirmed by the destination,
        /// or to abort re-sending.
        /// </summary>
        /// <param name="deliveryId">TBD</param>
        /// <returns>True the first time the <paramref name="deliveryId"/> is confirmed, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            var before = _unconfirmed;
            _unconfirmed = _unconfirmed.Remove(deliveryId);
            if (_unconfirmed.IsEmpty) Cancel();
            return _unconfirmed.Count < before.Count;
        }

        private void RedeliverOverdue()
        {
            DateTime now = DateTime.UtcNow;
            DateTime deadline = now - RedeliverInterval;
            var warnings = new List<UnconfirmedDelivery>();

            foreach (
                var entry in _unconfirmed.Where(e => e.Value.Timestamp <= deadline).Take(RedeliveryBurstLimit).ToArray()
                )
            {
                long deliveryId = entry.Key;
                Delivery unconfirmedDelivery = entry.Value;

                Send(deliveryId, unconfirmedDelivery, now);

                if (unconfirmedDelivery.Attempt == WarnAfterNumberOfUnconfirmedAttempts)
                {
                    warnings.Add(new UnconfirmedDelivery(deliveryId, unconfirmedDelivery.Destination,
                        unconfirmedDelivery.Message));
                }
            }

            if (warnings.Count != 0)
            {
                _context.Self.Tell(new UnconfirmedWarning(warnings.ToArray()));
            }
        }

        private void Send(long deliveryId, Delivery delivery, DateTime timestamp)
        {
            _context.ActorSelection(delivery.Destination).Tell(delivery.Message);
            _unconfirmed = _unconfirmed.SetItem(deliveryId, new Delivery(delivery.Destination, delivery.Message, timestamp, delivery.Attempt + 1));
            StartRedeliverTask();
        }

        /// <summary>
        /// Full state of the <see cref="AtLeastOnceDeliverySemantic"/>. It can be saved with
        /// <see cref="Eventsourced.SaveSnapshot" />. During recovery the snapshot received in
        /// <see cref="SnapshotOffer"/> should be set with <see cref="SetDeliverySnapshot"/>.
        /// 
        /// The <see cref="AtLeastOnceDeliverySnapshot"/> contains the full delivery state,
        /// including unconfirmed messages. If you need a custom snapshot for other parts of the
        /// actor state you must also include the <see cref="AtLeastOnceDeliverySnapshot"/>.
        /// It is serialized using protobuf with the ordinary Akka serialization mechanism.
        /// It is easiest to include the bytes of the <see cref="AtLeastOnceDeliverySnapshot"/>
        /// as a blob in your custom snapshot.
        /// </summary>
        /// <returns>TBD</returns>
        public AtLeastOnceDeliverySnapshot GetDeliverySnapshot()
        {
            UnconfirmedDelivery[] unconfirmedDeliveries = _unconfirmed
                .Select(e => new UnconfirmedDelivery(e.Key, e.Value.Destination, e.Value.Message))
                .ToArray();

            return new AtLeastOnceDeliverySnapshot(_deliverySequenceNr, unconfirmedDeliveries);
        }

        /// <summary>
        /// If snapshot from <see cref="GetDeliverySnapshot" /> was saved it will be received during recovery
        /// phase in a <see cref="SnapshotOffer" /> message and should be set with this method.
        /// </summary>
        /// <param name="snapshot">TBD</param>
        public void SetDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            _deliverySequenceNr = snapshot.CurrentDeliveryId;
            DateTime now = DateTime.UtcNow;
            _unconfirmed =
                snapshot.UnconfirmedDeliveries.Select(
                    u => new KeyValuePair<long, Delivery>(u.DeliveryId, new Delivery(u.Destination, u.Message, now, 0)))
                    .ToImmutableSortedDictionary();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Cancel()
        {
            // need a null check here, in case actor is terminated before StartRedeliverTask() is called
            _redeliverScheduleCancelable?.Cancel();
            _redeliverScheduleCancelable = null;
        }


        /// <summary>
        /// TBD
        /// </summary>
        public void OnReplaySuccess()
        {
            if (_unconfirmed.IsEmpty) return;
            RedeliverOverdue();
            StartRedeliverTask();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public bool AroundReceive(Receive receive, object message)
        {
            if (message is RedeliveryTick)
            {
                RedeliverOverdue();
                return true;
            }
            return false;
        }
        #endregion
    }
}
