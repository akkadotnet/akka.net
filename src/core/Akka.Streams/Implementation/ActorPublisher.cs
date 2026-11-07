//-----------------------------------------------------------------------
// <copyright file="ActorPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Marker message that wakes an actor after a subscriber has been queued for registration.
    /// </summary>
    [Serializable]
    internal sealed class SubscribePending
    {
        /// <summary>
        /// Gets the shared wake-up message.
        /// </summary>
        public static readonly SubscribePending Instance = new();
        private SubscribePending() { }
    }

    /// <summary>
    /// Carries a subscriber's request count to the actor that implements the publisher.
    /// </summary>
    [Serializable]
    internal sealed class RequestMore : IDeadLetterSuppression
    {
        /// <summary>
        /// Gets the subscription whose demand changed.
        /// </summary>
        public readonly IActorSubscription Subscription;
        /// <summary>
        /// Gets the number of elements requested by the subscriber.
        /// </summary>
        public readonly long Demand;

        /// <summary>
        /// Creates a request message for the implementing actor.
        /// </summary>
        /// <param name="subscription">The subscription that issued the request.</param>
        /// <param name="demand">The requested element count.</param>
        public RequestMore(IActorSubscription subscription, long demand)
        {
            Subscription = subscription;
            Demand = demand;
        }
    }

    /// <summary>
    /// Carries a subscriber cancellation to the actor that implements the publisher.
    /// </summary>
    [Serializable]
    internal sealed class Cancel : IDeadLetterSuppression
    {
        /// <summary>
        /// Gets the canceled subscription.
        /// </summary>
        public readonly IActorSubscription Subscription;

        /// <summary>
        /// Creates a cancellation message for the implementing actor.
        /// </summary>
        /// <param name="subscription">The subscription that was canceled.</param>
        public Cancel(IActorSubscription subscription)
        {
            Subscription = subscription;
        }
    }

    /// <summary>
    /// Supplies the actor publisher implementation to its Reactive Streams wrapper.
    /// </summary>
    [Serializable]
    internal sealed class ExposedPublisher : IDeadLetterSuppression
    {
        /// <summary>
        /// Gets the actor publisher implementation being exposed.
        /// </summary>
        public readonly IActorPublisher Publisher;

        /// <summary>
        /// Creates a message that exposes the actor publisher implementation.
        /// </summary>
        /// <param name="publisher">The publisher implementation to expose.</param>
        public ExposedPublisher(IActorPublisher publisher)
        {
            Publisher = publisher;
        }
    }

    /// <summary>
    /// Exception used when a publisher is shut down with a normal shutdown reason.
    /// </summary>
    [Serializable]
    public class NormalShutdownException : IllegalStateException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NormalShutdownException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public NormalShutdownException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="NormalShutdownException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected NormalShutdownException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Contract for an actor-backed publisher that can shut down and drain pending subscribers.
    /// </summary>
    public interface IActorPublisher : IUntypedPublisher
    {
        /// <summary>
        /// Shuts down the publisher and completes or fails pending subscribers according to the reason, unless a specification-violation reason suppresses terminal signals.
        /// </summary>
        /// <param name="reason">The failure to signal, or <see langword="null"/> to complete pending subscribers successfully. A specification-violation reason suppresses terminal signals.</param>
        void Shutdown(Exception reason);
        /// <summary>
        /// Removes and returns the subscribers currently awaiting registration.
        /// </summary>
        /// <returns>The subscribers pending at the time of the call.</returns>
        IEnumerable<IUntypedSubscriber> TakePendingSubscribers();
    }

    /// <summary>
    /// Provides the standard reason used when a publisher is shut down normally.
    /// </summary>
    public static class ActorPublisher
    {
        /// <summary>
        /// Gets the message used by <see cref="NormalShutdownReason"/>.
        /// </summary>
        public const string NormalShutdownReasonMessage = "Cannot subscribe to shut-down Publisher";
        /// <summary>
        /// Gets the shared exception used to reject subscriptions after normal shutdown.
        /// </summary>
        public static readonly NormalShutdownException NormalShutdownReason = new(NormalShutdownReasonMessage);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// When you instantiate this class, or its subclasses, you MUST send an ExposedPublisher message to the wrapped
    /// ActorRef! If you don't need to subclass, prefer the apply() method on the companion object which takes care of this.
    /// </summary>
    /// <typeparam name="TOut">The type of elements emitted by the publisher.</typeparam>
    [InternalApi]
    public class ActorPublisher<TOut> : IActorPublisher, IPublisher<TOut>
    {
        /// <summary>
        /// Gets the actor that implements this publisher.
        /// </summary>
        protected readonly IActorRef Impl;

        // The subscriber of an subscription attempt is first placed in this list of pending subscribers.
        // The actor will call takePendingSubscribers to remove it from the list when it has received the
        // SubscribePending message. The AtomicReference is set to null by the shutdown method, which is
        // called by the actor from postStop. Pending (unregistered) subscription attempts are denied by
        // the shutdown method. Subscription attempts after shutdown can be denied immediately.
        private readonly AtomicReference<ImmutableList<ISubscriber<TOut>>> _pendingSubscribers =
            new(ImmutableList<ISubscriber<TOut>>.Empty);

        private volatile Exception _shutdownReason;

        /// <summary>
        /// Gets the message sent to the implementation actor to drain queued subscription attempts.
        /// </summary>
        protected virtual object WakeUpMessage => SubscribePending.Instance;

        /// <summary>
        /// Creates a publisher wrapper around its implementing actor.
        /// </summary>
        /// <param name="impl">The actor that handles subscription, request, and cancellation messages.</param>
        public ActorPublisher(IActorRef impl)
        {
            Impl = impl;
        }

        /// <summary>
        /// Queues a subscriber for actor-side registration, or immediately signals completion, failure, or no signal according to the shutdown reason.
        /// </summary>
        /// <param name="subscriber">The subscriber to register.</param>
        /// <exception cref="ArgumentNullException">The subscriber is null.</exception>
        public void Subscribe(ISubscriber<TOut> subscriber)
        {
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));
            while (true)
            {
                var current = _pendingSubscribers.Value;
                if (current == null)
                {
                    ReportSubscribeFailure(subscriber);
                    break;
                }

                if (_pendingSubscribers.CompareAndSet(current, current.Add(subscriber)))
                {
                    Impl.Tell(WakeUpMessage);
                    break;
                }
            }
        }

        void IUntypedPublisher.Subscribe(IUntypedSubscriber subscriber) => Subscribe(UntypedSubscriber.ToTyped<TOut>(subscriber));

        /// <summary>
        /// Atomically removes the subscribers currently waiting for actor-side registration.
        /// </summary>
        /// <returns>The subscribers removed from the pending queue.</returns>
        public IEnumerable<ISubscriber<TOut>> TakePendingSubscribers()
        {
            var pending = _pendingSubscribers.GetAndSet(ImmutableList<ISubscriber<TOut>>.Empty);
            return pending ?? ImmutableList<ISubscriber<TOut>>.Empty;
        }

        IEnumerable<IUntypedSubscriber> IActorPublisher.TakePendingSubscribers() => TakePendingSubscribers().Select(UntypedSubscriber.FromTyped);

        /// <summary>
        /// Shuts down this publisher and completes or fails pending subscribers according to the reason, unless a specification-violation reason suppresses terminal signals.
        /// </summary>
        /// <param name="reason">The failure sent to pending subscribers, or <see langword="null"/> to complete them successfully. A specification-violation reason suppresses terminal signals.</param>
        public void Shutdown(Exception reason)
        {
            _shutdownReason = reason;
            var pending = _pendingSubscribers.GetAndSet(null);
            if (pending != null)
            {
                foreach (var subscriber in pending.Reverse())
                    ReportSubscribeFailure(subscriber);
            }
        }

        private void ReportSubscribeFailure(ISubscriber<TOut> subscriber)
        {
            try
            {
                if (_shutdownReason == null)
                {
                    ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                    ReactiveStreamsCompliance.TryOnComplete(subscriber);
                }
                else if (_shutdownReason is ISpecViolation)
                {
                    // ok, not allowed to call OnError
                }
                else
                {
                    ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                    ReactiveStreamsCompliance.TryOnError(subscriber, _shutdownReason);
                }
            }
            catch (Exception exception)
                when (exception is ISpecViolation)
            {
            }
        }
    }

    /// <summary>
    /// Subscription type that receives requests and cancellation through actor messages.
    /// </summary>
    public interface IActorSubscription : ISubscription
    {
    }

    /// <summary>
    /// Creates actor subscriptions for typed or untyped subscribers.
    /// </summary>
    public static class ActorSubscription
    {
        /// <summary>
        /// Creates an actor subscription for an untyped subscriber using its element type.
        /// </summary>
        /// <param name="implementor">The actor that handles requests and cancellation.</param>
        /// <param name="subscriber">The untyped subscriber attached to the subscription.</param>
        /// <returns>An actor subscription with the subscriber's element type.</returns>
        internal static IActorSubscription Create(IActorRef implementor, IUntypedSubscriber subscriber)
        {
            if (subscriber is UntypedSubscriber untyped)
                return untyped.CreateActorSubscription(implementor);

            // an IUntypedSubscriber implemented outside Akka.Streams (#8731)
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw RuntimeGenerics.NotSupported(subscriber);

            var subscribedType = RuntimeGenerics.FirstGenericArgument(subscriber);
            return (IActorSubscription)RuntimeGenerics.Instantiate(typeof(ActorSubscription<>), subscribedType, implementor, UntypedSubscriber.ToTyped(subscriber));
        }

        /// <summary>
        /// Creates an actor subscription for a typed subscriber.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="implementor">The actor that handles requests and cancellation.</param>
        /// <param name="subscriber">The subscriber attached to the subscription.</param>
        /// <returns>The actor subscription.</returns>
        public static IActorSubscription Create<T>(IActorRef implementor, ISubscriber<T> subscriber)
            => new ActorSubscription<T>(implementor, subscriber);
    }

    /// <summary>
    /// Actor-backed Reactive Streams subscription for elements of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The subscriber's element type.</typeparam>
    public class ActorSubscription<T> : IActorSubscription
    {
        /// <summary>
        /// Gets the actor that receives subscription messages.
        /// </summary>
        public readonly IActorRef Implementor;
        /// <summary>
        /// Gets the subscriber associated with this subscription.
        /// </summary>
        public readonly ISubscriber<T> Subscriber;

        /// <summary>
        /// Creates an actor subscription for the supplied subscriber.
        /// </summary>
        /// <param name="implementor">The actor that handles requests and cancellation.</param>
        /// <param name="subscriber">The subscriber attached to the subscription.</param>
        public ActorSubscription(IActorRef implementor, ISubscriber<T> subscriber)
        {
            Implementor = implementor;
            Subscriber = subscriber;
        }

        /// <summary>
        /// Sends a request message to the implementing actor.
        /// </summary>
        /// <param name="n">The requested element count.</param>
        public void Request(long n) => Implementor.Tell(new RequestMore(this, n));

        /// <summary>
        /// Sends a cancellation message to the implementing actor.
        /// </summary>
        public void Cancel() => Implementor.Tell(new Cancel(this));
    }

    /// <summary>
    /// Actor subscription that also tracks active state, outstanding demand, and the subscriber's buffer cursor.
    /// </summary>
    /// <typeparam name="TIn">The element type delivered to the subscriber.</typeparam>
    public class ActorSubscriptionWithCursor<TIn> : ActorSubscription<TIn>, ISubscriptionWithCursor<TIn>
    {
        /// <summary>
        /// Creates an active subscription with its reader cursor and demand initialized to zero.
        /// </summary>
        /// <param name="implementor">The actor that handles requests and cancellation.</param>
        /// <param name="subscriber">The subscriber receiving elements.</param>
        public ActorSubscriptionWithCursor(IActorRef implementor, ISubscriber<TIn> subscriber) : base(implementor, subscriber)
        {
            IsActive = true;
            TotalDemand = 0;
            Cursor = 0;
        }

        ISubscriber<TIn> ISubscriptionWithCursor<TIn>.Subscriber => Subscriber;

        /// <summary>
        /// Delivers an untyped element after casting it to the subscriber's element type.
        /// </summary>
        /// <param name="element">The element to cast and deliver.</param>
        public void Dispatch(object element) => ReactiveStreamsCompliance.TryOnNext(Subscriber, (TIn)element);

        bool ISubscriptionWithCursor<TIn>.IsActive
        {
            get { return IsActive; }
            set { IsActive = value; }
        }

        /// <summary>
        /// Gets whether this subscription remains active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets the read position of this subscriber in the shared buffer.
        /// </summary>
        public long Cursor { get; private set; }

        long ISubscriptionWithCursor<TIn>.TotalDemand
        {
            get { return TotalDemand; }
            set { TotalDemand = value; }
        }

        /// <summary>
        /// Gets the number of requested elements not yet delivered.
        /// </summary>
        public long TotalDemand { get; private set; }

        /// <summary>
        /// Delivers a typed element to the subscriber.
        /// </summary>
        /// <param name="element">The element to deliver.</param>
        public void Dispatch(TIn element) => ReactiveStreamsCompliance.TryOnNext(Subscriber, element);

        long ICursor.Cursor
        {
            get { return Cursor; }
            set { Cursor = value; }
        }
    }
}
