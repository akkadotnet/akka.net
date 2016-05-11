//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryReceiveActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    ///     Receive persistent actor type, that sends messages with at-least-once delivery semantics to it's destinations.
    /// </summary>
    public abstract class AtLeastOnceDeliveryReceiveActor : ReceivePersistentActor
    {
        private readonly AtLeastOnceDeliverySemantic _atLeastOnceDeliverySemantic;

        protected AtLeastOnceDeliveryReceiveActor()
        {
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, Extension.Settings.AtLeastOnceDelivery);

        }
        protected AtLeastOnceDeliveryReceiveActor(PersistenceSettings.AtLeastOnceDeliverySettings settings)
        {
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, settings);

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
            get { return _atLeastOnceDeliverySemantic.RedeliverInterval; ; }
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
        public int RedeliveryBurstLimit
        {
            get { return _atLeastOnceDeliverySemantic.RedeliveryBurstLimit; }
        }

        /// <summary>
        /// After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        /// <see cref="ActorBase.Self" />. The count is reset after restart.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.warn-after-number-of-unconfirmed-attempts'
        /// configuration key. This method can be overridden by implementation classes to return
        /// non-default values.
        /// </summary>
        public int WarnAfterNumberOfUnconfirmedAttempts
        {
            get { return _atLeastOnceDeliverySemantic.WarnAfterNumberOfUnconfirmedAttempts; }
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
        public int MaxUnconfirmedMessages
        {
            get { return _atLeastOnceDeliverySemantic.MaxUnconfirmedMessages; }
        }

        /// <summary>
        /// Number of messages that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount
        {
            get { return _atLeastOnceDeliverySemantic.UnconfirmedCount; }
        }

        public override void AroundPreRestart(Exception cause, object message)
        {
            _atLeastOnceDeliverySemantic.Cancel();
            base.AroundPreRestart(cause, message);
        }

        public override void AroundPostStop()
        {
            _atLeastOnceDeliverySemantic.Cancel();
            base.AroundPostStop();
        }


        protected override void OnReplaySuccess()
        {
            _atLeastOnceDeliverySemantic.OnReplaySuccess();
            base.OnReplaySuccess();
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            return _atLeastOnceDeliverySemantic.AroundReceive(receive, message) || base.AroundReceive(receive, message);
        }

        /// <summary>
        /// Send the message created with <paramref name="deliveryMessageMapper" /> function to the
        /// <paramref name="destination" /> actor. It will retry sending the message until the delivery is
        /// confirmed with <see cref="ConfirmDelivery" />.
        /// Correlation between these two methods is performed by deliveryId that is provided as parameter
        /// to the <paramref name="deliveryMessageMapper"/> function. The deliveryId is typically passed in the message to
        /// the destination, which replies with a message containing the same 'deliveryId'.
        /// 
        /// The 'deliveryId' is a strictly monotonically increasing sequence number without gaps.
        /// The same sequence is used for all destinations of the actor, i.e. when sending
        /// to multiple destinations the destinations will see gaps in the sequence if no translation is performed.
        /// 
        /// During recovery this method will not send out the message, but it will be sent later if no matching 
        /// <see cref="ConfirmDelivery" /> was performed.
        /// </summary>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        /// Thrown when <see cref="UnconfirmedCount" /> is greater than or equal to <see cref="MaxUnconfirmedMessages" />.
        /// </exception>
        public void Deliver(ActorPath destination, Func<long, object> deliveryMessageMapper)
        {
            _atLeastOnceDeliverySemantic.Deliver(destination, deliveryMessageMapper, IsRecovering);
        }

        /// <summary>
        /// Send the message created with <paramref name="deliveryMessageMapper" /> function to the
        /// <paramref name="destination" /> actor. It will retry sending the message until the delivery is
        /// confirmed with <see cref="ConfirmDelivery" />.
        /// Correlation between these two methods is performed by deliveryId that is provided as parameter
        /// to the <paramref name="deliveryMessageMapper"/> function. The deliveryId is typically passed in the message to
        /// the destination, which replies with a message containing the same 'deliveryId'.
        /// 
        /// The 'deliveryId' is a strictly monotonically increasing sequence number without gaps.
        /// The same sequence is used for all destinations of the actor, i.e. when sending
        /// to multiple destinations the destinations will see gaps in the sequence if no translation is performed.
        /// 
        /// During recovery this method will not send out the message, but it will be sent later if no matching 
        /// <see cref="ConfirmDelivery" /> was performed.
        /// </summary>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        /// Thrown when <see cref="UnconfirmedCount" /> is greater than or equal to <see cref="MaxUnconfirmedMessages" />.
        /// </exception>
        public void Deliver(ActorSelection destination, Func<long, object> deliveryMessageMapper)
        {
            var isWildcardSelection = destination.PathString.Contains("*");
            if (isWildcardSelection)
                throw new NotSupportedException(
                    "Delivering to wildcard actor selections is not supported by AtLeastOnceDelivery. " +
                    "Introduce an mediator Actor which this AtLeastOnceDelivery Actor will deliver the messages to," +
                    "and will handle the logic of fan-out and collecting individual confirmations, until it can signal confirmation back to this Actor.");
            Deliver(ActorPath.Parse(destination.PathString), deliveryMessageMapper);
        }

        /// <summary>
        /// Call this method when a message has been confirmed by the destination,
        /// or to abort re-sending.
        /// </summary>
        /// <returns>True the first time the <paramref name="deliveryId"/> is confirmed, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            return _atLeastOnceDeliverySemantic.ConfirmDelivery(deliveryId);
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
            return _atLeastOnceDeliverySemantic.GetDeliverySnapshot();
        }

        /// <summary>
        /// If snapshot from <see cref="GetDeliverySnapshot" /> was saved it will be received during recovery
        /// phase in a <see cref="SnapshotOffer" /> message and should be set with this method.
        /// </summary>
        public void SetDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            _atLeastOnceDeliverySemantic.SetDeliverySnapshot(snapshot);
        }
    }
}