using System;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using System.Threading;
using Akka.Actor;

namespace Akka.Streams.Implementation
{
    public class SubscriptionTimeoutException : Exception
    {
        public SubscriptionTimeoutException(string message) : base(message)
        {
        }

        public SubscriptionTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected SubscriptionTimeoutException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// A subscriber who calls <see cref="ISubscription.Cancel"/> directly from <see cref="OnSubscribe"/> and ignores all other callbacks.
    /// </summary>
    public sealed class CancelingSubscriber<T> : ISubscriber<T>
    {
        public static readonly CancelingSubscriber<T> Instance = new CancelingSubscriber<T>();
        private CancelingSubscriber() { }

        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            subscription.Cancel();
        }

        public void OnNext(T element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
        }

        void ISubscriber.OnNext(object element)
        {
            OnNext((T)element);
        }

        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
        }

        public void OnComplete() { }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Subscription timeout which does not start any scheduled events and always returns `true`.
    /// This specialized implementation is to be used for "noop" timeout mode.
    /// </summary>
    public sealed class NoopSubscriptionTimeout : ICancelable
    {
        public static readonly NoopSubscriptionTimeout Instance = new NoopSubscriptionTimeout();
        private NoopSubscriptionTimeout() { }

        public void Cancel() { }

        public bool IsCancellationRequested => true;

        public CancellationToken Token => CancellationToken.None;

        public void CancelAfter(TimeSpan delay) { }

        public void CancelAfter(int millisecondsDelay) { }

        public void Cancel(bool throwOnFirstException) { }
    }

    /// <summary>
    /// INTERNAL API
    /// Provides support methods to create Publishers and Subscribers which time-out gracefully,
    /// and are cancelled subscribing an <see cref="CancellingSubscriber{T}"/> to the publisher, or by calling `onError` on the timed-out subscriber.
    /// 
    /// See `akka.stream.materializer.subscription-timeout` for configuration options.
    /// </summary>
    public interface IStreamSubscriptionTimeoutSupport
    {
        /// <summary>
        /// Default settings for subscription timeouts.
        /// </summary>
        StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings { get; }

        /// <summary>
        /// Schedules a Subscription timeout.
        /// The actor will receive the message created by the provided block if the timeout triggers.
        /// </summary>
        ICancelable ScheduleSubscriptionTimeout(IActorRef actorRef, object message);

        /// <summary>
        /// Called by the actor when a subscription has timed out. Expects the actual <see cref="IPublisher"/> or <see cref="IProcessor{T1,T2}"/> target.
        /// </summary>
        void SubscriptionTimedOut(IPublisher target);

        /// <summary>
        /// Callback that should ensure that the target is canceled with the given cause.
        /// </summary>
        void HandleSubscriptionTimeout(IPublisher target, Exception cause);
    }
}