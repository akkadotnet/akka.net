using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    #region Messages

    /// <summary>
    /// Snapshot of a current <see cref="GuaranteedDeliveryActor"/> state. Can be retrieved with
    /// <see cref="GuaranteedDeliveryActor.GetDeliverySnapshot"/> and saved with <see cref="Eventsourced.SaveSnapshot"/>.
    /// During recovery the snapshot received in <see cref="SnapshotOffer"/> should be sent with 
    /// <see cref="GuaranteedDeliveryActor.SetDeliverySnapshot"/>.
    /// </summary>
    [Serializable]
    public sealed class GuaranteedDeliverySnapshot : IMessage
    {
        public GuaranteedDeliverySnapshot(long deliveryId, UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            DeliveryId = deliveryId;
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public long DeliveryId { get; private set; }
        public UnconfirmedDelivery[] UnconfirmedDeliveries { get; private set; }
    }

    /// <summary>
    /// <see cref="UnconfirmedWarning"/> message should be sent after 
    /// <see cref="GuaranteedDeliveryActor.UnconfirmedDeliveryAttemptsToWarn"/> limit will be reached.
    /// </summary>
    public sealed class UnconfirmedWarning
    {
        public UnconfirmedWarning(UnconfirmedDelivery[] unconfirmedDeliveries)
        {
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public UnconfirmedDelivery[] UnconfirmedDeliveries { get; set; }
    }

    /// <summary>
    /// <see cref="UnconfirmedDelivery"/> contains details about unconfirmed messages.
    /// It's included inside <see cref="UnconfirmedWarning"/> and <see cref="GuaranteedDeliverySnapshot"/>.
    /// </summary>
    public sealed class UnconfirmedDelivery
    {
        public UnconfirmedDelivery(long deliveryId, ActorPath destination, object message)
        {
            DeliveryId = deliveryId;
            Destination = destination;
            Message = message;
        }

        public long DeliveryId { get; set; }
        public ActorPath Destination { get; set; }
        public object Message { get; set; }
    }

    internal sealed class Delivery
    {
        public Delivery(ActorPath destination, object message, DateTime timestamp, int attempt)
        {
            Destination = destination;
            Message = message;
            Timestamp = timestamp;
            Attempt = attempt;
        }

        public ActorPath Destination { get; private set; }
        public object Message { get; private set; }
        public DateTime Timestamp { get; private set; }
        public int Attempt { get; private set; }

        public Delivery IncrementedCopy()
        {
            return new Delivery(Destination, Message, Timestamp, Attempt + 1);
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

    #endregion

    /// <summary>
    /// An exception thrown, when <see cref="GuaranteedDeliveryActor.MaxUnconfirmedMessages"/> threshold has been exceeded.
    /// </summary>
    public class MaxUnconfirmedMessagesExceededException : AkkaException
    {
        public MaxUnconfirmedMessagesExceededException(string message, Exception cause = null)
            : base(message, cause)
        {
        }

        protected MaxUnconfirmedMessagesExceededException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Persistent actor type, that sends messages with at-least-once delivery semantics to it's destinations.
    /// It takes care of re-sending messages when they haven't been confirmed withing expected timeout. The same 
    /// message may be send twice or more to the same destination as a result of possible resends.
    /// 
    /// Use a <see cref="Deliver"/> method to send a message to a destination. Call the <see cref="ConfirmDelivery"/>
    /// method once destination has replied with a confirmation message. The interval between redelivery attempts
    /// can be defined with <see cref="RedeliverInterval"/>. After a number of delivery attempts an 
    /// <see cref="UnconfirmedWarning"/> message will be sent to <see cref="Self"/>. The resending will continue,
    /// but you may choose <see cref="ConfirmDelivery"/> to cancel resending.
    /// 
    /// This actor type has state consisting of unconfirmed messages and a sequence number. It doesn't store it by
    /// itself, so you must persist corresponding events so that state can be restored by calling the same 
    /// delivery-related methods during recovery phase of the persistent actor. During recovery calls to 
    /// <see cref="Deliver"/> won't send out a message, but it will be sent later if no <see cref="ConfirmDelivery"/>
    /// call was performed.
    /// 
    /// Support for snapshot is provided by get and set delivery snapshot methods. These snapshots contains full
    /// delivery state including unconfirmed messages. For custom snapshots remember to include those delivery ones.
    /// </summary>
    public abstract class GuaranteedDeliveryActor : PersistentActor, IInitializableActor
    {
        private ICancelable _redeliverScheduleCancelable;
        private long _deliverySequenceNr = 0L;
        private ConcurrentDictionary<long, Delivery> _unconfirmed = new ConcurrentDictionary<long, Delivery>();
        
        /// <summary>
        /// Invoked after actor has been created and all of it's fields have been initialized.
        /// </summary>
        public void Init()
        {
            _redeliverScheduleCancelable = ScheduleRedelivery();
        }


        /// <summary>
        /// Interval between redelivery attempts.
        /// </summary>
        public virtual TimeSpan RedeliverInterval { get { return DefaultRedeliverInterval; } }
        protected TimeSpan DefaultRedeliverInterval { get { return Extension.Settings.GuaranteedDelivery.RedeliverInterval; } }

        /// <summary>
        /// Maximum number of unconfirmed messages that will be sent at each redelivery burst. This is to help to 
        /// prevent overflowing amount of messages to be sent at once, for eg. when destination cannot be reached for a long time.
        /// </summary>
        public virtual int RedeliveryBurstLimit { get { return DefaultRedeliveryBurstLimit; } }
        protected int DefaultRedeliveryBurstLimit { get { return Extension.Settings.GuaranteedDelivery.RedeliveryBurstLimit; } }

        /// <summary>
        /// After this number of delivery attempts a <see cref="UnconfirmedWarning"/> message will be sent to <see cref="Self"/>.
        /// The count is reset after restart.
        /// </summary>
        public virtual int UnconfirmedDeliveryAttemptsToWarn { get { return DefaultUnconfirmedDeliveryAttemptsToWarn; } }
        protected int DefaultUnconfirmedDeliveryAttemptsToWarn { get { return Extension.Settings.GuaranteedDelivery.UnconfirmedAttemptsToWarn; } }

        /// <summary>
        /// Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory. When this 
        /// number is exceed, <see cref="Deliver"/> will throw <see cref="MaxUnconfirmedMessagesExceededException"/>
        /// instead of accepting messages.
        /// </summary>
        public virtual int MaxUnconfirmedMessages { get { return DefaultMaxUnconfirmedMessages; } }
        protected int DefaultMaxUnconfirmedMessages { get { return Extension.Settings.GuaranteedDelivery.MaxUnconfirmedMessages; } }

        /// <summary>
        /// Number of messages, that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount { get { return _unconfirmed.Count; } }

        /// <summary>
        /// Send the message created with <paramref name="deliveryMessageMapper"/> function to the <see cref="destination"/>
        /// actor. It will retry sending the message until the delivery is confirmed with <see cref="ConfirmDelivery"/>.
        /// Correlation between these two methods is performed by delivery id - parameter of <see cref="deliveryMessageMapper"/>.
        /// Usually it's passed inside the message to the destination, which replies with the message having the same id.
        /// 
        /// During recovery this method won't send out any message, but it will be sent later until corresponding 
        /// <see cref="ConfirmDelivery"/> method will be invoked.
        /// </summary>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        /// Thrown when <see cref="UnconfirmedCount"/> is greater than or equal to <see cref="MaxUnconfirmedMessages"/>.
        /// </exception>
        public void Deliver(ActorPath destination, Func<long, object> deliveryMessageMapper)
        {
            if (UnconfirmedCount >= MaxUnconfirmedMessages)
            {
                throw new MaxUnconfirmedMessagesExceededException(string.Format("{0} has too many unconfirmed messages. Maximum allowed is {1}", PersistenceId, MaxUnconfirmedMessages));
            }

            var deliveryId = NextDeliverySequenceNr();
            var now = IsRecovering ? DateTime.Now - RedeliverInterval : DateTime.Now;
            var delivery = new Delivery(destination, deliveryMessageMapper(deliveryId), now, attempt: 0);

            if (IsRecovering)
            {
                _unconfirmed.AddOrUpdate(deliveryId, delivery, (id, d) => delivery);
            }
            else
            {
                Send(deliveryId, delivery, now);
            }
        }

        /// <summary>
        /// Call this method to confirm that message with <paramref name="deliveryId"/> has been sent
        /// or to cancel redelivery attempts.
        /// </summary>
        /// <returns>True if delivery was confirmed first time, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            Delivery delivery;
            return _unconfirmed.TryRemove(deliveryId, out delivery);
        }

        /// <summary>
        /// Returns full state of the current delivery actor. Could be saved using <see cref="Eventsourced.SaveSnapshot"/> method.
        /// During recovery a snapshot received in <see cref="SnapshotOffer"/> should be set with <see cref="SetDeliverySnapshot"/>.
        /// </summary>
        public GuaranteedDeliverySnapshot GetDeliverySnapshot()
        {
            var unconfirmedDeliveries = _unconfirmed
                .Select(e => new UnconfirmedDelivery(e.Key, e.Value.Destination, e.Value.Message))
                .ToArray();

            return new GuaranteedDeliverySnapshot(_deliverySequenceNr, unconfirmedDeliveries);
        }

        /// <summary>
        /// If snapshot from <see cref="GetDeliverySnapshot"/> was saved, it will be received during recovery phase in a
        /// <see cref="SnapshotOffer"/> message and should be set with this method.
        /// </summary>
        /// <param name="snapshot"></param>
        public void SetDeliverySnapshot(GuaranteedDeliverySnapshot snapshot)
        {
            _deliverySequenceNr = snapshot.DeliveryId;
            var now = DateTime.Now;
            var unconfirmedDeliveries = snapshot.UnconfirmedDeliveries
                .Select(u => new KeyValuePair<long, Delivery>(u.DeliveryId, new Delivery(u.Destination, u.Message, now, 0)));

            _unconfirmed = new ConcurrentDictionary<long, Delivery>(unconfirmedDeliveries);
        }

        public override void AroundPostRestart(Exception cause, object message)
        {
            _redeliverScheduleCancelable.Cancel();
            base.AroundPostRestart(cause, message);
        }

        public override void AroundPostStop()
        {
            _redeliverScheduleCancelable.Cancel();
            base.AroundPostStop();
        }


        protected override void OnReplaySuccess()
        {
            RedeliverOverdue();
            base.OnReplaySuccess();
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            if (message is RedeliveryTick)
            {
                RedeliverOverdue();
                return true;
            }

            return base.AroundReceive(receive, message);
        }

        private void Send(long deliveryId, Delivery delivery, DateTime timestamp)
        {
            var destination = Context.ActorSelection(delivery.Destination);
            destination.Tell(delivery.Message);

            var dcopy = new Delivery(delivery.Destination, delivery.Message, timestamp, delivery.Attempt + 1);
            _unconfirmed.AddOrUpdate(deliveryId, dcopy, (id, d) => dcopy);
        }

        private void RedeliverOverdue()
        {
            var now = DateTime.Now;
            var deadline = now - RedeliverInterval;
            var warnings = new List<UnconfirmedDelivery>();

            foreach (var entry in _unconfirmed.Where(e => e.Value.Timestamp <= deadline).Take(RedeliveryBurstLimit).ToArray())
            {
                var deliveryId = entry.Key;
                var unconfirmedDelivery = entry.Value;

                Send(deliveryId, unconfirmedDelivery, now);

                if (unconfirmedDelivery.Attempt == UnconfirmedDeliveryAttemptsToWarn)
                {
                    warnings.Add(new UnconfirmedDelivery(deliveryId, unconfirmedDelivery.Destination, unconfirmedDelivery.Message));
                }
            }

            if (warnings.Count != 0)
            {
                Self.Tell(new UnconfirmedWarning(warnings.ToArray()));
            }
        }

        private long NextDeliverySequenceNr()
        {
            return (++_deliverySequenceNr);
        }

        private ICancelable ScheduleRedelivery()
        {
            var interval = new TimeSpan(RedeliverInterval.Ticks / 2);
            return Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, interval, Self, RedeliveryTick.Instance, Self);
        }
    }
}