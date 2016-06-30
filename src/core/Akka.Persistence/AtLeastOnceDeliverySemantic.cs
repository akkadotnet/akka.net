//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliverySemantic.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
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
        public readonly long CurrentDeliveryId;
        public readonly UnconfirmedDelivery[] UnconfirmedDeliveries;

        public AtLeastOnceDeliverySnapshot(long currentDeliveryId, UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            if (unconfirmedDeliveries == null)
                throw new ArgumentNullException("unconfirmedDeliveries",
                    "AtLeastOnceDeliverySnapshot expects not null array of unconfirmed deliveries");

            CurrentDeliveryId = currentDeliveryId;
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public bool Equals(AtLeastOnceDeliverySnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(CurrentDeliveryId, other.CurrentDeliveryId)
                   && UnconfirmedDeliveries.SequenceEqual(other.UnconfirmedDeliveries);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AtLeastOnceDeliverySnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = CurrentDeliveryId.GetHashCode();
                hashCode = (hashCode*397) ^ (UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format("AtLeastOnceDeliverySnapshot<currentDeliveryId: {0}, unconfirmedDeliveries: {1}>", CurrentDeliveryId,
                UnconfirmedDeliveries.Length);
        }
    }

    /// <summary>
    /// Message should be sent after <see cref="AtLeastOnceDeliverySemantic.WarnAfterNumberOfUnconfirmedAttempts" />
    /// limit will is reached.
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class UnconfirmedWarning : IEquatable<UnconfirmedWarning>
    {
        public readonly UnconfirmedDelivery[] UnconfirmedDeliveries;

        public UnconfirmedWarning(UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            if (unconfirmedDeliveries == null)
                throw new ArgumentNullException("unconfirmedDeliveries",
                    "UnconfirmedWarning expects not null array of unconfirmed deliveries");

            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public bool Equals(UnconfirmedWarning other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(UnconfirmedDeliveries, other.UnconfirmedDeliveries);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UnconfirmedWarning);
        }

        public override int GetHashCode()
        {
            return (UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return string.Format("UnconfirmedWarning<unconfirmedDeliveries: {0}>", UnconfirmedDeliveries.Length);
        }
    }

    /// <summary>
    /// Contains details about unconfirmed messages.
    /// It's included inside <see cref="UnconfirmedWarning" /> and <see cref="AtLeastOnceDeliverySnapshot" />.
    /// <see cref="AtLeastOnceDeliverySemantic.AfterNumberOfUnconfirmedAttempts" />
    /// </summary>
#if SERIALIZATION
    [Serializable]
#endif
    public sealed class UnconfirmedDelivery : IEquatable<UnconfirmedDelivery>
    {
        public readonly long DeliveryId;
        public readonly ActorPath Destination;
        public readonly object Message;

        public UnconfirmedDelivery(long deliveryId, ActorPath destination, object message)
        {
            DeliveryId = deliveryId;
            Destination = destination;
            Message = message;
        }

        public bool Equals(UnconfirmedDelivery other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(DeliveryId, other.DeliveryId)
                   && Equals(Destination, other.Destination)
                   && Equals(Message, other.Message);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UnconfirmedDelivery);
        }

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

        public override string ToString()
        {
            return string.Format("UnconfirmedDelivery<deliveryId: {0}, dest: {1}, message: {2}>", DeliveryId,
                Destination, Message);
        }
    }

    /// <summary>
    /// This exception is thrown when the <see cref="AtLeastOnceDeliverySemantic.MaxUnconfirmedMessages" /> threshold has been exceeded.
    /// </summary>
    public class MaxUnconfirmedMessagesExceededException : Exception
    {
        public MaxUnconfirmedMessagesExceededException()
        {
        }

        public MaxUnconfirmedMessagesExceededException(string message) : base(message)
        {
        }

        public MaxUnconfirmedMessagesExceededException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if SERIALIZATION
        protected MaxUnconfirmedMessagesExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    #endregion

    public class AtLeastOnceDeliverySemantic
    {

#if SERIALIZATION
        [Serializable]
#endif
        internal sealed class Delivery : IEquatable<Delivery>
        {
            public readonly int Attempt;
            public readonly ActorPath Destination;
            public readonly object Message;
            public readonly DateTime Timestamp;

            public Delivery(ActorPath destination, object message, DateTime timestamp, int attempt)
            {
                Destination = destination;
                Message = message;
                Timestamp = timestamp;
                Attempt = attempt;
            }

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
                    hashCode = (hashCode*397) ^ (Message != null ? Message.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ Timestamp.GetHashCode();
                    hashCode = (hashCode*397) ^ Attempt;
                    return hashCode;
                }
            }

            public override string ToString()
            {
                return string.Format("Delivery<dest: {0}, attempt: {1}, timestamp: {2}, message: {3}", Destination,
                    Attempt,
                    Timestamp, Message);
            }
        }

#if SERIALIZATION
        [Serializable]
#endif
        internal sealed class RedeliveryTick
        {
            public static readonly RedeliveryTick Instance = new RedeliveryTick();

            private RedeliveryTick()
            {
            }

            public override bool Equals(object obj)
            {
                return obj is RedeliveryTick;
            }
        }

        #region actor methods

        private readonly IActorContext _context;
        private long _deliverySequenceNr;
        private ICancelable _redeliverScheduleCancelable;
        private readonly PersistenceSettings.AtLeastOnceDeliverySettings _settings;
        private ImmutableSortedDictionary<long, Delivery> _unconfirmed = ImmutableSortedDictionary<long, Delivery>.Empty;


        public AtLeastOnceDeliverySemantic(IActorContext context, PersistenceSettings.AtLeastOnceDeliverySettings settings)
        {
            _context = context;
            _settings = settings;
            _deliverySequenceNr = 0;
        }

        /// <summary>
        /// Interval between redelivery attempts.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.redeliver-interval'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public virtual TimeSpan RedeliverInterval
        {
            get { return _settings.RedeliverInterval; }
        }

        /// <summary>
        /// Maximum number of unconfirmed messages that will be sent at each redelivery burst
        /// (burst frequency is half of the redelivery interval).
        /// If there's a lot of unconfirmed messages (e.g. if the destination is not available for a long time),
        /// this helps prevent an overwhelming amount of messages to be sent at once.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.redelivery-burst-limit'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public virtual int RedeliveryBurstLimit
        {
            get { return _settings.RedeliveryBurstLimit; }
        }

        /// <summary>
        /// After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        /// <see cref="ActorBase.Self" />. The count is reset after restart.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.warn-after-number-of-unconfirmed-attempts'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public virtual int WarnAfterNumberOfUnconfirmedAttempts
        {
            get { return _settings.WarnAfterNumberOfUnconfirmedAttempts; }
        }

        /// <summary>
        /// Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory.
        /// if this number is exceeded, <see cref="AtLeastOnceDeliverySemantic.Deliver" /> will not accept more
        /// messages and it will throw <see cref="MaxUnconfirmedMessagesExceededException" />.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.max-unconfirmed-messages'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public virtual int MaxUnconfirmedMessages
        {
            get { return _settings.MaxUnconfirmedMessages; }
        }

        /// <summary>
        ///     Number of messages, that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount
        {
            get { return _unconfirmed.Count; }
        }

        private void StartRedeliverTask()
        {
            var interval = new TimeSpan(RedeliverInterval.Ticks/2);
            _redeliverScheduleCancelable = _context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, interval, _context.Self,
                RedeliveryTick.Instance, _context.Self);
        }

        private long NextDeliverySequenceNr()
        {
            return (++_deliverySequenceNr);
        }

        public void Deliver(ActorPath destination, Func<long, object> deliveryMessageMapper, bool isRecovering)
        {
            if (_unconfirmed.Count >= MaxUnconfirmedMessages)
            {
                throw new MaxUnconfirmedMessagesExceededException(
                    string.Format("{0} has too many unconfirmed messages. Maximum allowed is {1}", _context.Self,
                        MaxUnconfirmedMessages));
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
        /// <returns>True the first time the <paramref name="deliveryId"/> is confirmed, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            var before = _unconfirmed;
            _unconfirmed = _unconfirmed.Remove(deliveryId);
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
            ActorSelection destination = _context.ActorSelection(delivery.Destination);
            destination.Tell(delivery.Message);

            _unconfirmed = _unconfirmed.SetItem(deliveryId,
                new Delivery(delivery.Destination, delivery.Message, timestamp, delivery.Attempt + 1));
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
        public void SetDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            _deliverySequenceNr = snapshot.CurrentDeliveryId;
            DateTime now = DateTime.UtcNow;
            _unconfirmed =
                snapshot.UnconfirmedDeliveries.Select(
                    u => new KeyValuePair<long, Delivery>(u.DeliveryId, new Delivery(u.Destination, u.Message, now, 0)))
                    .ToImmutableSortedDictionary();
        }

        public void Cancel()
        {
            // need a null check here, in case actor is terminated before StartRedeliverTask() is called
            _redeliverScheduleCancelable?.Cancel();
        }


        public void OnReplaySuccess()
        {
            RedeliverOverdue();
            StartRedeliverTask();
        }

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