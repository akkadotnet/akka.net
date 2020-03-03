//-----------------------------------------------------------------------
// <copyright file="StreamSubscriptionTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Threading;
using Akka.Actor;
using Akka.Annotations;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    public class SubscriptionTimeoutException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionTimeoutException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public SubscriptionTimeoutException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionTimeoutException"/> class.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public SubscriptionTimeoutException(string message, Exception innerException) : base(message, innerException)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected SubscriptionTimeoutException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// A subscriber who calls <see cref="ISubscription.Cancel"/> directly from <see cref="OnSubscribe"/> and ignores all other callbacks.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class CancelingSubscriber<T> : ISubscriber<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly CancelingSubscriber<T> Instance = new CancelingSubscriber<T>();
        private CancelingSubscriber() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            subscription.Cancel();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(T element) => ReactiveStreamsCompliance.RequireNonNullElement(element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public void OnError(Exception cause) => ReactiveStreamsCompliance.RequireNonNullException(cause);

        /// <summary>
        /// TBD
        /// </summary>
        public void OnComplete() { }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Subscription timeout which does not start any scheduled events and always returns `true`.
    /// This specialized implementation is to be used for "noop" timeout mode.
    /// </summary>
    [InternalApi]
    public sealed class NoopSubscriptionTimeout : ICancelable
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly NoopSubscriptionTimeout Instance = new NoopSubscriptionTimeout();
        private NoopSubscriptionTimeout() { }

        /// <summary>
        /// TBD
        /// </summary>
        public void Cancel() { }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsCancellationRequested => true;

        /// <summary>
        /// TBD
        /// </summary>
        public CancellationToken Token => CancellationToken.None;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        public void CancelAfter(TimeSpan delay) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="millisecondsDelay">TBD</param>
        public void CancelAfter(int millisecondsDelay) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="throwOnFirstException">TBD</param>
        public void Cancel(bool throwOnFirstException) { }
    }

    /// <summary>
    /// INTERNAL API
    /// Provides support methods to create Publishers and Subscribers which time-out gracefully,
    /// and are cancelled subscribing an <see cref="CancellingSubscriber{T}"/> to the publisher, or by calling onError on the timed-out subscriber.
    /// 
    /// See "akka.stream.materializer.subscription-timeout" for configuration options.
    /// </summary>
    internal interface IStreamSubscriptionTimeoutSupport
    {
        /// <summary>
        /// Default settings for subscription timeouts.
        /// </summary>
        StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings { get; }

        /// <summary>
        /// Schedules a Subscription timeout.
        /// The actor will receive the message created by the provided block if the timeout triggers.
        /// </summary>
        /// <param name="actorRef">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        ICancelable ScheduleSubscriptionTimeout(IActorRef actorRef, object message);

        /// <summary>
        /// Called by the actor when a subscription has timed out. Expects the actual <see cref="IUntypedPublisher"/> or <see cref="IProcessor{T1,T2}"/> target.
        /// </summary>
        /// <param name="target">TBD</param>
        void SubscriptionTimedOut(IUntypedPublisher target);

        /// <summary>
        /// Callback that should ensure that the target is canceled with the given cause.
        /// </summary>
        /// <param name="target">TBD</param>
        /// <param name="cause">TBD</param>
        void HandleSubscriptionTimeout(IUntypedPublisher target, Exception cause);
    }
}
