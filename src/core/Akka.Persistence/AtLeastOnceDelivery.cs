//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDelivery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;

namespace Akka.Persistence
{
    /// <summary>
    ///     Persistent actor type, that sends messages with at-least-once delivery semantics to it's destinations.
    ///     It takes care of re-sending messages when they haven't been confirmed withing expected timeout. The same
    ///     message may be send twice or more to the same destination as a result of possible resends.
    ///     Use a <see cref="AtLeastOnceDeliverySemantic.Deliver" /> method to send a message to a destination. Call the
    ///     <see cref="ConfirmDelivery" />
    ///     method once destination has replied with a confirmation message. The interval between redelivery attempts
    ///     can be defined with <see cref="RedeliverInterval" />. After a number of delivery attempts an
    ///     <see cref="UnconfirmedWarning" /> message will be sent to <see cref="ActorBase.Self" />. The resending will
    ///     continue,
    ///     but you may choose <see cref="AtLeastOnceDeliverySemantic.ConfirmDelivery" /> to cancel resending.
    ///     This actor type has state consisting of unconfirmed messages and a sequence number. It doesn't store it by
    ///     itself, so you must persist corresponding events so that state can be restored by calling the same
    ///     delivery-related methods during recovery phase of the persistent actor. During recovery calls to
    ///     <see cref="AtLeastOnceDeliverySemantic.Deliver" /> won't send out a message, but it will be sent later if no
    ///     matching <see cref="AtLeastOnceDeliverySemantic.ConfirmDelivery" /> call was performed.
    ///     Support for snapshot is provided by get and set delivery snapshot methods. These snapshots contains full
    ///     delivery state including unconfirmed messages. For custom snapshots remember to include those delivery ones.
    /// </summary>
    public abstract class AtLeastOnceDeliveryActor : PersistentActor, IInitializableActor
    {
        private readonly AtLeastOnceDeliverySemantic _atLeastOnceDeliverySemantic;

        protected AtLeastOnceDeliveryActor()
        {
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, Extension.Settings.AtLeastOnceDelivery);
        }


        protected AtLeastOnceDeliveryActor(PersistenceSettings.AtLeastOnceDeliverySettings settings)
        {
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, settings);
        }

        /// <summary>
        ///     Interval between redelivery attempts.
        /// </summary>
        public TimeSpan RedeliverInterval
        {
            get { return _atLeastOnceDeliverySemantic.RedeliverInterval; }
        }

        /// <summary>
        ///     Maximum number of unconfirmed messages that will be sent at each redelivery burst. This is to help to
        ///     prevent overflowing amount of messages to be sent at once, for eg. when destination cannot be reached for a long
        ///     time.
        /// </summary>
        public int RedeliveryBurstLimit
        {
            get { return _atLeastOnceDeliverySemantic.RedeliveryBurstLimit; }
        }

        /// <summary>
        ///     After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        ///     <see cref="ActorBase.Self" />.
        ///     The count is reset after restart.
        /// </summary>
        public int UnconfirmedDeliveryAttemptsToWarn
        {
            get { return _atLeastOnceDeliverySemantic.UnconfirmedDeliveryAttemptsToWarn; }
        }

        /// <summary>
        ///     Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory. When this
        ///     number is exceed, <see cref="Deliver" /> will throw
        ///     <see cref="AtLeastOnceDeliverySemantic.MaxUnconfirmedMessagesExceededException" />
        ///     instead of accepting messages.
        /// </summary>
        public int MaxUnconfirmedMessages
        {
            get { return _atLeastOnceDeliverySemantic.MaxUnconfirmedMessages; }
        }
        

        /// <summary>
        ///     Number of messages, that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount
        {
            get { return _atLeastOnceDeliverySemantic.UnconfirmedCount; }
        }

        public void Init()
        {
            _atLeastOnceDeliverySemantic.Init();
        }


        public override void AroundPostRestart(Exception cause, object message)
        {
            _atLeastOnceDeliverySemantic.Cancel();
            base.AroundPostRestart(cause, message);
        }

        public override void AroundPostStop()
        {
            _atLeastOnceDeliverySemantic.Cancel();
            base.AroundPostStop();
        }


        protected override void OnReplaySuccess()
        {
            _atLeastOnceDeliverySemantic.OnReplaySuccess();
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            return _atLeastOnceDeliverySemantic.AroundReceive(receive, message) || base.AroundReceive(receive, message);
        }

        /// <summary>
        ///     If snapshot from <see cref="GetDeliverySnapshot" /> was saved, it will be received during recovery phase in a
        ///     <see cref="SnapshotOffer" /> message and should be set with this method.
        /// </summary>
        /// <param name="snapshot"></param>
        public void SetDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            _atLeastOnceDeliverySemantic.SetDeliverySnapshot(snapshot);
        }

        /// <summary>
        ///     Call this method to confirm that message with <paramref name="deliveryId" /> has been sent
        ///     or to cancel redelivery attempts.
        /// </summary>
        /// <returns>True if delivery was confirmed first time, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            return _atLeastOnceDeliverySemantic.ConfirmDelivery(deliveryId);
        }


        /// <summary>
        ///     Returns full state of the current delivery actor. Could be saved using <see cref="Eventsourced.SaveSnapshot" />
        ///     method.
        ///     During recovery a snapshot received in <see cref="SnapshotOffer" /> should be set with
        ///     <see cref="SetDeliverySnapshot" />.
        /// </summary>
        public AtLeastOnceDeliverySnapshot GetDeliverySnapshot()
        {
            return _atLeastOnceDeliverySemantic.GetDeliverySnapshot();
        }

        /// <summary>
        ///     Send the message created with <paramref name="deliveryMessageMapper" /> function to the
        ///     <paramref name="destination" />
        ///     actor. It will retry sending the message until the delivery is confirmed with <see cref="ConfirmDelivery" />.
        ///     Correlation between these two methods is performed by delivery id - parameter of
        ///     <paramref name="deliveryMessageMapper" />.
        ///     Usually it's passed inside the message to the destination, which replies with the message having the same id.
        ///     During recovery this method won't send out any message, but it will be sent later if no matching
        ///     <see cref="ConfirmDelivery" /> call was performed.
        /// </summary>
        /// <exception cref="AtLeastOnceDeliverySemantic.MaxUnconfirmedMessagesExceededException">
        ///     Thrown when <see cref="UnconfirmedCount" /> is greater than or equal to <see cref="MaxUnconfirmedMessages" />.
        /// </exception>
        public void Deliver(ActorPath destination, Func<long, object> deliveryMessageMapper)
        {
            _atLeastOnceDeliverySemantic.Deliver(destination, deliveryMessageMapper, IsRecovering);
        }
    }
}