using System;
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

    /**
     * A subscriber who calls `cancel` directly from `onSubscribe` and ignores all other callbacks.
     */
    public sealed class CancelingSubscriber : ISubscriber
    {
        public static readonly CancelingSubscriber Instance = new CancelingSubscriber();

        private CancelingSubscriber()
        {
        }

        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            subscription.Cancel();
        }

        public void OnNext(object element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
        }

        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
        }

        public void OnComplete()
        {
        }
    }

    /**
     * INTERNAL API
     *
     * Subscription timeout which does not start any scheduled events and always returns `true`.
     * This specialized implementation is to be used for "noop" timeout mode.
     */
    public sealed class NoopSubscriptionTimeout : ICancelable
    {
        public static readonly NoopSubscriptionTimeout Instance = new NoopSubscriptionTimeout();
        private NoopSubscriptionTimeout() { }

        public void Cancel() { }

        public bool IsCancellationRequested
        {
            get { return true; }
        }

        public CancellationToken Token
        {
            get { throw new NotImplementedException(); }
        }

        public void CancelAfter(TimeSpan delay) { }

        public void CancelAfter(int millisecondsDelay) { }

        public void Cancel(bool throwOnFirstException) { }
    }

    /**
     * INTERNAL API
     * Provides support methods to create Publishers and Subscribers which time-out gracefully,
     * and are cancelled subscribing an `CancellingSubscriber` to the publisher, or by calling `onError` on the timed-out subscriber.
     *
     * See `akka.stream.materializer.subscription-timeout` for configuration options.
     */
    public interface IStreamSubscriptionTimeoutSupport
    {
        
    }
}