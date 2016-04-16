using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    [Serializable]
    public sealed class SubscribePending
    {
        public static readonly SubscribePending Instance = new SubscribePending();
        private SubscribePending() { }
    }

    [Serializable]
    public sealed class RequestMore
    {
        public readonly IActorSubscription Subscription;
        public readonly long Demand;

        public RequestMore(IActorSubscription subscription, long demand)
        {
            Subscription = subscription;
            Demand = demand;
        }
    }

    [Serializable]
    public sealed class Cancel
    {
        public readonly IActorSubscription Subscription;

        public Cancel(IActorSubscription subscription)
        {
            Subscription = subscription;
        }
    }

    [Serializable]
    public sealed class ExposedPublisher
    {
        public readonly IActorPublisher Publisher;

        public ExposedPublisher(IActorPublisher publisher)
        {
            Publisher = publisher;
        }
    }

    [Serializable]
    public class NormalShutdownException : IllegalStateException
    {
        public NormalShutdownException(string message) : base(message) { }
        protected NormalShutdownException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    public interface IActorPublisher : IPublisher
    {
        void Shutdown(Exception reason);
        IEnumerable<ISubscriber> TakePendingSubscribers();
    }

    internal static class ActorPublisher
    {
        public const string NormalShutdownReasonMessage = "Cannot subscribe to shut-down Publisher";
        public static readonly NormalShutdownException NormalShutdownReason = new NormalShutdownException(NormalShutdownReasonMessage);
    }

    /**
     * INTERNAL API
     *
     * When you instantiate this class, or its subclasses, you MUST send an ExposedPublisher message to the wrapped
     * ActorRef! If you don't need to subclass, prefer the apply() method on the companion object which takes care of this.
     */
    public class ActorPublisher<TOut> : IActorPublisher, IPublisher<TOut>
    {
        protected readonly IActorRef Impl;

        // The subscriber of an subscription attempt is first placed in this list of pending subscribers.
        // The actor will call takePendingSubscribers to remove it from the list when it has received the
        // SubscribePending message. The AtomicReference is set to null by the shutdown method, which is
        // called by the actor from postStop. Pending (unregistered) subscription attempts are denied by
        // the shutdown method. Subscription attempts after shutdown can be denied immediately.
        private readonly AtomicReference<ImmutableList<ISubscriber<TOut>>> _pendingSubscribers =
            new AtomicReference<ImmutableList<ISubscriber<TOut>>>(ImmutableList<ISubscriber<TOut>>.Empty);

        private volatile Exception _shutdownReason;
        
        protected virtual object WakeUpMessage => SubscribePending.Instance;

        public ActorPublisher(IActorRef impl)
        {
            Impl = impl;
        }

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

        void IPublisher.Subscribe(ISubscriber subscriber)
        {
            Subscribe((ISubscriber<TOut>)subscriber);
        }

        public IEnumerable<ISubscriber<TOut>> TakePendingSubscribers()
        {
            var pending = _pendingSubscribers.GetAndSet(ImmutableList<ISubscriber<TOut>>.Empty);
            return pending ?? ImmutableList<ISubscriber<TOut>>.Empty;
        }

        IEnumerable<ISubscriber> IActorPublisher.TakePendingSubscribers()
        {
            return TakePendingSubscribers();
        }

        public void Shutdown(Exception reason)
        {
            _shutdownReason = reason;
            var pending = _pendingSubscribers.GetAndSet(null);
            if (pending != null)
            {
                foreach (var subscriber in pending.Reverse())
                {
                    ReportSubscribeFailure(subscriber);
                }
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
            {
                if (!(exception is ISpecViolation)) throw;
            }
        }
    }

    public interface IActorSubscription : ISubscription
    {
    }

    public static class ActorSubscription
    {
        public static IActorSubscription Create(IActorRef implementor, ISubscriber subscriber)
        {
            var subscribedType = subscriber.GetType().GetSubscribedType();
            var subscriptionType = typeof(ActorSubscription<>).MakeGenericType(subscribedType);
            return (IActorSubscription) Activator.CreateInstance(subscriptionType, implementor, subscriber);
        }

        public static IActorSubscription Create<T>(IActorRef implementor, ISubscriber<T> subscriber)
        {
            return new ActorSubscription<T>(implementor, subscriber);
        }
    }

    public class ActorSubscription<T> : IActorSubscription
    {
        public readonly IActorRef Implementor;
        public readonly ISubscriber<T> Subscriber;

        public ActorSubscription(IActorRef implementor, ISubscriber<T> subscriber)
        {
            Implementor = implementor;
            Subscriber = subscriber;
        }

        public void Request(long n)
        {
            Implementor.Tell(new RequestMore(this, n));
        }

        public void Cancel()
        {
            Implementor.Tell(new Cancel(this));
        }
    }

    public class ActorSubscriptionWithCursor<TIn> : ActorSubscription<TIn>, ISubscriptionWithCursor<TIn>
    {
        public ActorSubscriptionWithCursor(IActorRef implementor, ISubscriber<TIn> subscriber) : base(implementor, subscriber)
        {
            IsActive = true;
            TotalDemand = 0;
            Cursor = 0;
        }

        ISubscriber<TIn> ISubscriptionWithCursor<TIn>.Subscriber => Subscriber;

        public void Dispatch(object element)
        {
            ReactiveStreamsCompliance.TryOnNext(Subscriber, (TIn)element);
        }

        bool ISubscriptionWithCursor<TIn>.IsActive
        {
            get { return IsActive; }
            set { IsActive = value; }
        }

        public bool IsActive { get; private set; }
        public int Cursor { get; private set; }
        long ISubscriptionWithCursor<TIn>.TotalDemand
        {
            get { return TotalDemand; }
            set { TotalDemand = value; }
        }

        public long TotalDemand { get; private set; }
        public void Dispatch(TIn element)
        {
            ReactiveStreamsCompliance.TryOnNext(Subscriber, element);
        }

        int ICursor.Cursor
        {
            get { return Cursor; }
            set { Cursor = value; }
        }
    }
}