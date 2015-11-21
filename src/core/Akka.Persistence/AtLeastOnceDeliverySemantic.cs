//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDelivery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{

    #region Messages

    /// <summary>
    ///     Snapshot of a current <see cref="AtLeastOnceDeliveryActor" /> state. Can be retrieved with
    ///     <see cref="AtLeastOnceDeliverySemantic.GetDeliverySnapshot" /> and saved with
    ///     <see cref="Eventsourced.SaveSnapshot" />.
    ///     During recovery the snapshot received in <see cref="SnapshotOffer" /> should be sent with
    ///     <see cref="AtLeastOnceDeliverySemantic.SetDeliverySnapshot" />.
    /// </summary>
    public sealed class AtLeastOnceDeliverySnapshot : IMessage, IEquatable<AtLeastOnceDeliverySnapshot>
    {
        public readonly long DeliveryId;
        public readonly UnconfirmedDelivery[] UnconfirmedDeliveries;

        public AtLeastOnceDeliverySnapshot(long deliveryId, UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            if (unconfirmedDeliveries == null)
                throw new ArgumentNullException("unconfirmedDeliveries",
                    "AtLeastOnceDeliverySnapshot expects not null array of unconfirmed deliveries");

            DeliveryId = deliveryId;
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public bool Equals(AtLeastOnceDeliverySnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(DeliveryId, other.DeliveryId)
                   && Equals(UnconfirmedDeliveries, UnconfirmedDeliveries);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AtLeastOnceDeliverySnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (DeliveryId.GetHashCode()*397) ^
                       (UnconfirmedDeliveries != null ? UnconfirmedDeliveries.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return string.Format("AtLeastOnceDeliverySnapshot<deliveryId: {0}, unconfirmedDeliveries: {1}>", DeliveryId,
                UnconfirmedDeliveries.Length);
        }
    }

    /// <summary>
    ///     <see cref="UnconfirmedDelivery" /> contains details about unconfirmed messages.
    ///     It's included inside <see cref="UnconfirmedWarning" /> and <see cref="AtLeastOnceDeliverySnapshot" />.
    /// </summary>
    [Serializable]
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
    ///     <see cref="UnconfirmedWarning" /> message should be sent after
    ///     <see cref="AtLeastOnceDeliverySemantic.UnconfirmedDeliveryAttemptsToWarn" /> limit will be reached.
    /// </summary>
    [Serializable]
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


    [Serializable]
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
            return string.Format("Delivery<dest: {0}, attempt: {1}, timestamp: {2}, message: {3}", Destination, Attempt,
                Timestamp, Message);
        }
    }

    #endregion

    internal class AtLeastOnceDeliverySemantic
    {
        /// <summary>
        ///     This exception is thrown when the <see cref="MaxUnconfirmedMessages" /> threshold has been exceeded.
        /// </summary>
        public class MaxUnconfirmedMessagesExceededException : AkkaException
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException" /> class.
            /// </summary>
            /// <param name="message">The message that describes the error.</param>
            /// <param name="cause">The exception that is the cause of the current exception.</param>
            public MaxUnconfirmedMessagesExceededException(string message, Exception cause = null)
                : base(message, cause)
            {
            }

            /// <summary>
            ///     Initializes a new instance of the <see cref="MaxUnconfirmedMessagesExceededException" /> class.
            /// </summary>
            /// <param name="info">
            ///     The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being
            ///     thrown.
            /// </param>
            /// <param name="context">
            ///     The <see cref="StreamingContext" /> that contains contextual information about the source or
            ///     destination.
            /// </param>
            protected MaxUnconfirmedMessagesExceededException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }

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
        private ConcurrentDictionary<long, Delivery> _unconfirmed = new ConcurrentDictionary<long, Delivery>();


        public AtLeastOnceDeliverySemantic(IActorContext context, PersistenceSettings.AtLeastOnceDeliverySettings settings)
        {
            _context = context;
            _settings = settings;
        }


        /// <summary>
        ///     Interval between redelivery attempts.
        /// </summary>
        public virtual TimeSpan RedeliverInterval
        {
            get { return DefaultRedeliverInterval; }
        }

        protected TimeSpan DefaultRedeliverInterval
        {
            get { return _settings.RedeliverInterval; }
        }

        /// <summary>
        ///     Maximum number of unconfirmed messages that will be sent at each redelivery burst. This is to help to
        ///     prevent overflowing amount of messages to be sent at once, for eg. when destination cannot be reached for a long
        ///     time.
        /// </summary>
        public virtual int RedeliveryBurstLimit
        {
            get { return DefaultRedeliveryBurstLimit; }
        }

        protected int DefaultRedeliveryBurstLimit
        {
            get { return _settings.RedeliveryBurstLimit; }
        }

        /// <summary>
        ///     After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        ///     <see cref="ActorBase.Self" />.
        ///     The count is reset after restart.
        /// </summary>
        public virtual int UnconfirmedDeliveryAttemptsToWarn
        {
            get { return DefaultUnconfirmedDeliveryAttemptsToWarn; }
        }

        protected int DefaultUnconfirmedDeliveryAttemptsToWarn
        {
            get { return _settings.UnconfirmedAttemptsToWarn; }
        }

        /// <summary>
        ///     Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory. When this
        ///     number is exceed, <see cref="Deliver" /> will throw <see cref="MaxUnconfirmedMessagesExceededException" />
        ///     instead of accepting messages.
        /// </summary>
        public virtual int MaxUnconfirmedMessages
        {
            get { return DefaultMaxUnconfirmedMessages; }
        }

        protected int DefaultMaxUnconfirmedMessages
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

        /// <summary>
        ///     Invoked after actor has been created and all of it's fields have been initialized.
        /// </summary>
        public void Init()
        {
            _redeliverScheduleCancelable = ScheduleRedelivery();
        }

        /// <summary>
        ///     Send the message created with <paramref name="deliveryMessageMapper" /> function to the
        ///     <paramref name="destination" />
        ///     actor. It will retry sending the message until the delivery is confirmed with <see cref="ConfirmDelivery" />.
        ///     Correlation between these two methods is performed by delivery id - parameter of
        ///     <paramref name="deliveryMessageMapper" />.
        ///     Usually it's passed inside the message to the destination, which replies with the message having the same id.
        ///     During recovery this method won't send out any message, but it will be sent later until corresponding
        ///     <see cref="ConfirmDelivery" /> method will be invoked.
        /// </summary>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        ///     Thrown when <see cref="UnconfirmedCount" /> is greater than or equal to <see cref="MaxUnconfirmedMessages" />.
        /// </exception>
        public void Deliver(ActorPath destination, Func<long, object> deliveryMessageMapper, bool isRecovering = false)
        {
            if (UnconfirmedCount >= MaxUnconfirmedMessages)
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
                _unconfirmed.AddOrUpdate(deliveryId, delivery, (id, d) => delivery);
            }
            else
            {
                Send(deliveryId, delivery, now);
            }
        }

        /// <summary>
        ///     Call this method to confirm that message with <paramref name="deliveryId" /> has been sent
        ///     or to cancel redelivery attempts.
        /// </summary>
        /// <returns>True if delivery was confirmed first time, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            Delivery delivery;
            return _unconfirmed.TryRemove(deliveryId, out delivery);
        }

        /// <summary>
        ///     Returns full state of the current delivery actor. Could be saved using <see cref="Eventsourced.SaveSnapshot" />
        ///     method.
        ///     During recovery a snapshot received in <see cref="SnapshotOffer" /> should be set with
        ///     <see cref="SetDeliverySnapshot" />.
        /// </summary>
        public AtLeastOnceDeliverySnapshot GetDeliverySnapshot()
        {
            UnconfirmedDelivery[] unconfirmedDeliveries = _unconfirmed
                .Select(e => new UnconfirmedDelivery(e.Key, e.Value.Destination, e.Value.Message))
                .ToArray();

            return new AtLeastOnceDeliverySnapshot(_deliverySequenceNr, unconfirmedDeliveries);
        }

        /// <summary>
        ///     If snapshot from <see cref="GetDeliverySnapshot" /> was saved, it will be received during recovery phase in a
        ///     <see cref="SnapshotOffer" /> message and should be set with this method.
        /// </summary>
        /// <param name="snapshot"></param>
        public void SetDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            _deliverySequenceNr = snapshot.DeliveryId;
            DateTime now = DateTime.UtcNow;
            IEnumerable<KeyValuePair<long, Delivery>> unconfirmedDeliveries = snapshot.UnconfirmedDeliveries
                .Select(
                    u => new KeyValuePair<long, Delivery>(u.DeliveryId, new Delivery(u.Destination, u.Message, now, 0)));

            _unconfirmed = new ConcurrentDictionary<long, Delivery>(unconfirmedDeliveries);
        }

        public void Cancel()
        {
            _redeliverScheduleCancelable.Cancel();
        }


        public void OnReplaySuccess()
        {
            RedeliverOverdue();
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

        private void Send(long deliveryId, Delivery delivery, DateTime timestamp)
        {
            ActorSelection destination = _context.ActorSelection(delivery.Destination);
            destination.Tell(delivery.Message);

            var dcopy = new Delivery(delivery.Destination, delivery.Message, timestamp, delivery.Attempt + 1);
            _unconfirmed.AddOrUpdate(deliveryId, dcopy, (id, d) => dcopy);
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

                if (unconfirmedDelivery.Attempt == UnconfirmedDeliveryAttemptsToWarn)
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

        private long NextDeliverySequenceNr()
        {
            return (++_deliverySequenceNr);
        }

        private ICancelable ScheduleRedelivery()
        {
            var interval = new TimeSpan(RedeliverInterval.Ticks/2);
            return _context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, interval, _context.Self,
                RedeliveryTick.Instance, _context.Self);
        }

        #endregion
    }
}