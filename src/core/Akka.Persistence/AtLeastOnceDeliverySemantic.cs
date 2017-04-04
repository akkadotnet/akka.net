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
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// TBD
    /// </summary>
    public class AtLeastOnceDeliverySemantic
    {
        private readonly IActorContext _context;
        private long _deliverySequenceNr;
        private ICancelable _redeliverScheduleCancelable;
        private readonly PersistenceSettings.AtLeastOnceDeliverySettings _settings;
        private ImmutableSortedDictionary<long, Delivery> _unconfirmed = ImmutableSortedDictionary<long, Delivery>.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="AtLeastOnceDeliverySemantic"/> class.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="settings">TBD</param>
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
        public virtual TimeSpan RedeliverInterval => _settings.RedeliverInterval;

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
        public virtual int RedeliveryBurstLimit => _settings.RedeliveryBurstLimit;

        /// <summary>
        /// After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        /// <see cref="ActorBase.Self" />. The count is reset after restart.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.warn-after-number-of-unconfirmed-attempts'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public virtual int WarnAfterNumberOfUnconfirmedAttempts => _settings.WarnAfterNumberOfUnconfirmedAttempts;

        /// <summary>
        /// Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory.
        /// if this number is exceeded, <see cref="AtLeastOnceDeliverySemantic.Deliver" /> will not accept more
        /// messages and it will throw <see cref="MaxUnconfirmedMessagesExceededException" />.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.max-unconfirmed-messages'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public virtual int MaxUnconfirmedMessages => _settings.MaxUnconfirmedMessages;

        /// <summary>
        ///     Number of messages, that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount => _unconfirmed.Count;

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
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void OnReplaySuccess()
        {
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
    }
}