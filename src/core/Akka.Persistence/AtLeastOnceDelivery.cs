//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDelivery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence
{

    /// <summary>
    /// Persistent actor type that sends messages with at-least-once delivery semantics to destinations.
    /// It takes care of re-sending messages when they haven't been confirmed withing expected timeout.
    /// Use the <see cref="AtLeastOnceDeliverySemantic.Deliver" /> method to send a message to a destination. Call the
    /// <see cref="AtLeastOnceDeliverySemantic.ConfirmDelivery" />
    /// method when destination has replied with a confirmation message.
    /// 
    /// At-least-once delivery implies that the original message send order is not always retained
    /// and the destination may receive duplicate messages due to possible resends.
    /// 
    /// The interval between redelivery attempts can be defined with <see cref="AtLeastOnceDeliverySemantic.RedeliverInterval" />.
    /// After a number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to <see cref="ActorBase.Self" />.
    /// The re-sending will continue, but you may choose <see cref="AtLeastOnceDeliverySemantic.ConfirmDelivery" /> to cancel re-sending.
    /// 
    /// This actor type has a state consisting of unconfirmed messages and a sequence number. It does not store this state
    /// itself. You must persist events corresponding to the <see cref="AtLeastOnceDeliverySemantic.Deliver"/> and
    /// <see cref="AtLeastOnceDeliverySemantic.ConfirmDelivery"/> invocations from your <see cref="PersistentActor"/> so that
    /// the state can be restored by calling the same methods during the recovery phase of the <see cref="PersistentActor"/>.
    /// Sometimes these events can be derived from other business level events, and sometimes you must create separate events.
    /// During recovery calls to <see cref="AtLeastOnceDeliverySemantic.Deliver"/> will not send out the message, but it will be sent
    /// later if no matching <see cref="AtLeastOnceDeliverySemantic.ConfirmDelivery"/> was performed.
    /// 
    /// Support for snapshot is provided by <see cref="AtLeastOnceDeliverySemantic.GetDeliverySnapshot"/> and
    /// <see cref="AtLeastOnceDeliverySemantic.SetDeliverySnapshot"/>. The <see cref="AtLeastOnceDeliverySnapshot"/> contains
    /// the full delivery state, including unconfirmed messages. If you need a custom snapshot for other parts of the
    /// actor state you must also include the <see cref="AtLeastOnceDeliverySnapshot"/>. It is serialized using protobuf
    /// with the ordinary Akka serialization mechanism. It is easiest to include the bytes of the
    /// <see cref="AtLeastOnceDeliverySnapshot"/> as a blob in your custom snapshot.
    /// </summary>
    public abstract class AtLeastOnceDeliveryActor : PersistentActor
    {
        private readonly AtLeastOnceDeliverySemantic _atLeastOnceDeliverySemantic;

        /// <summary>
        /// Initializes a new instance of the <see cref="AtLeastOnceDeliveryActor"/> class.
        /// </summary>
        protected AtLeastOnceDeliveryActor()
        {
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, Extension.Settings.AtLeastOnceDelivery);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AtLeastOnceDeliveryActor"/> class.
        /// </summary>
        /// <param name="settings">TBD</param>
        protected AtLeastOnceDeliveryActor(PersistenceSettings.AtLeastOnceDeliverySettings settings)
        {
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, settings);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AtLeastOnceDeliveryActor"/> class.
        /// </summary>
        /// <param name="overrideSettings">A lambda to tweak the default AtLeastOnceDelivery settings.</param>
        protected AtLeastOnceDeliveryActor(Func<PersistenceSettings.AtLeastOnceDeliverySettings, PersistenceSettings.AtLeastOnceDeliverySettings> overrideSettings)
        {
            var settings = overrideSettings(Extension.Settings.AtLeastOnceDelivery);
            _atLeastOnceDeliverySemantic = new AtLeastOnceDeliverySemantic(Context, settings);
        }

        /// <summary>
        /// Interval between redelivery attempts.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.redeliver-interval'
        /// configuration key. Custom value may be provided via the
        /// <see cref="AtLeastOnceDeliveryActor(PersistenceSettings.AtLeastOnceDeliverySettings)"/> constructor.
        /// </summary>
        public TimeSpan RedeliverInterval => _atLeastOnceDeliverySemantic.RedeliverInterval;

        /// <summary>
        /// Maximum number of unconfirmed messages that will be sent at each redelivery burst
        /// (burst frequency is half of the redelivery interval).
        /// If there's a lot of unconfirmed messages (e.g. if the destination is not available for a long time),
        /// this helps prevent an overwhelming amount of messages to be sent at once.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.redelivery-burst-limit'
        /// configuration key. Custom value may be provided via the
        /// <see cref="AtLeastOnceDeliveryActor(PersistenceSettings.AtLeastOnceDeliverySettings)"/> constructor.
        /// </summary>
        public int RedeliveryBurstLimit => _atLeastOnceDeliverySemantic.RedeliveryBurstLimit;

        /// <summary>
        /// After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
        /// <see cref="ActorBase.Self" />. The count is reset after restart.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.warn-after-number-of-unconfirmed-attempts'
        /// configuration key. Custom value may be provided via the
        /// <see cref="AtLeastOnceDeliveryActor(PersistenceSettings.AtLeastOnceDeliverySettings)"/> constructor.
        /// </summary>
        public int WarnAfterNumberOfUnconfirmedAttempts => _atLeastOnceDeliverySemantic.WarnAfterNumberOfUnconfirmedAttempts;

        /// <summary>
        /// Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory.
        /// if this number is exceeded, <see cref="AtLeastOnceDeliverySemantic.Deliver" /> will not accept more
        /// messages and it will throw <see cref="MaxUnconfirmedMessagesExceededException" />.
        /// 
        /// The default value can be configure with the 'akka.persistence.at-least-once-delivery.max-unconfirmed-messages'
        /// configuration key. Custom value may be provided via the
        /// <see cref="AtLeastOnceDeliveryActor(PersistenceSettings.AtLeastOnceDeliverySettings)"/> constructor.
        /// </summary>
        public int MaxUnconfirmedMessages => _atLeastOnceDeliverySemantic.MaxUnconfirmedMessages;

        /// <summary>
        /// Number of messages that have not been confirmed yet.
        /// </summary>
        public int UnconfirmedCount => _atLeastOnceDeliverySemantic.UnconfirmedCount;

        /// <inheritdoc/>
        public override void AroundPreRestart(Exception cause, object message)
        {
            _atLeastOnceDeliverySemantic.Cancel();
            base.AroundPreRestart(cause, message);
        }

        /// <inheritdoc/>
        public override void AroundPostStop()
        {
            _atLeastOnceDeliverySemantic.Cancel();
            base.AroundPostStop();
        }

        /// <inheritdoc/>
        protected override void OnReplaySuccess()
        {
            _atLeastOnceDeliverySemantic.OnReplaySuccess();
            base.OnReplaySuccess();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
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
        /// <param name="destination">TBD</param>
        /// <param name="deliveryMessageMapper">TBD</param>
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
        /// <param name="destination">TBD</param>
        /// <param name="deliveryMessageMapper">TBD</param>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        /// Thrown when <see cref="UnconfirmedCount" /> is greater than or equal to <see cref="MaxUnconfirmedMessages" />.
        /// </exception>
        /// <exception cref="NotSupportedException">TBD</exception>
        public void Deliver(ActorSelection destination, Func<long, object> deliveryMessageMapper)
        {
            var isWildcardSelection = destination.PathString.Contains("*");
            if (isWildcardSelection)
                throw new NotSupportedException(
                    "Delivering to wildcard actor selections is not supported by AtLeastOnceDelivery. " +
                    "Introduce an mediator Actor which this AtLeastOnceDelivery Actor will deliver the messages to," +
                    "and will handle the logic of fan-out and collecting individual confirmations, until it can signal confirmation back to this Actor.");
            Deliver(ActorPath.Parse($"{destination.Anchor.Path}/{destination.PathString}"), deliveryMessageMapper);
        }

        /// <summary>
        /// Call this method when a message has been confirmed by the destination,
        /// or to abort re-sending.
        /// </summary>
        /// <param name="deliveryId">TBD</param>
        /// <returns>True the first time the <paramref name="deliveryId"/> is confirmed, false for duplicate confirmations.</returns>
        public bool ConfirmDelivery(long deliveryId)
        {
            return _atLeastOnceDeliverySemantic.ConfirmDelivery(deliveryId);
        }

        /// <summary>
        /// Full state of the <see cref="AtLeastOnceDeliveryActor"/>. It can be saved with
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
            return _atLeastOnceDeliverySemantic.GetDeliverySnapshot();
        }

        /// <summary>
        /// If snapshot from <see cref="GetDeliverySnapshot" /> was saved it will be received during recovery
        /// phase in a <see cref="SnapshotOffer" /> message and should be set with this method.
        /// </summary>
        /// <param name="snapshot">TBD</param>
        public void SetDeliverySnapshot(AtLeastOnceDeliverySnapshot snapshot)
        {
            _atLeastOnceDeliverySemantic.SetDeliverySnapshot(snapshot);
        }
    }
}
