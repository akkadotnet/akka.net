//-----------------------------------------------------------------------
// <copyright file="StreamSubscriptionTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Exception raised when a stream subscription does not arrive before its configured timeout.
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

        /// <summary>
        /// Initializes a new instance of the <see cref="SubscriptionTimeoutException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected SubscriptionTimeoutException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// A subscriber who calls <see cref="ISubscription.Cancel"/> directly from <see cref="OnSubscribe"/> and ignores all other callbacks.
    /// </summary>
    /// <typeparam name="T">The publisher's element type.</typeparam>
    public sealed class CancelingSubscriber<T> : ISubscriber<T>
    {
        /// <summary>
        /// Singleton subscriber that cancels as soon as it receives a subscription.
        /// </summary>
        public static readonly CancelingSubscriber<T> Instance = new();
        private CancelingSubscriber() { }

        /// <summary>
        /// Cancels the subscription immediately.
        /// </summary>
        /// <param name="subscription">The subscription to cancel.</param>
        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            subscription.Cancel();
        }

        /// <summary>
        /// Validates that an element is non-null; this subscriber does not consume elements.
        /// </summary>
        /// <param name="element">The element checked for Reactive Streams null compliance.</param>
        public void OnNext(T element) => ReactiveStreamsCompliance.RequireNonNullElement(element);

        /// <summary>
        /// Validates that a failure is non-null; this subscriber does not otherwise handle it.
        /// </summary>
        /// <param name="cause">The failure checked for Reactive Streams null compliance.</param>
        public void OnError(Exception cause) => ReactiveStreamsCompliance.RequireNonNullException(cause);

        /// <summary>
        /// Ignores successful completion after cancellation.
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
        /// Shared no-op timeout instance; it schedules no work and remains cancellation-requested.
        /// </summary>
        public static readonly NoopSubscriptionTimeout Instance = new();
        private NoopSubscriptionTimeout() { }

        /// <summary>
        /// Does nothing because this timeout never schedules work.
        /// </summary>
        public void Cancel() { }

        /// <summary>
        /// Reports cancellation as already requested.
        /// </summary>
        public bool IsCancellationRequested => true;

        /// <summary>
        /// Gets a non-cancelable token.
        /// </summary>
        public CancellationToken Token => CancellationToken.None;

        /// <summary>
        /// Does nothing; no timeout is scheduled.
        /// </summary>
        /// <param name="delay">The ignored delay.</param>
        public void CancelAfter(TimeSpan delay) { }

        /// <summary>
        /// Does nothing; no timeout is scheduled.
        /// </summary>
        /// <param name="millisecondsDelay">The ignored delay in milliseconds.</param>
        public void CancelAfter(int millisecondsDelay) { }

        /// <summary>
        /// Does nothing because this timeout never schedules work or cancellation callbacks.
        /// </summary>
        /// <param name="throwOnFirstException">Ignored; cancellation cannot raise callback exceptions.</param>
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
        /// <param name="actorRef">The actor that receives the timeout message.</param>
        /// <param name="message">The message sent to <paramref name="actorRef"/> if the timeout expires.</param>
        /// <returns>A cancellation handle for the scheduled timeout.</returns>
        ICancelable ScheduleSubscriptionTimeout(IActorRef actorRef, object message);

        /// <summary>
        /// Called by the actor when a subscription has timed out. Expects the actual <see cref="IUntypedPublisher"/> or <see cref="IProcessor{T1,T2}"/> target.
        /// </summary>
        /// <param name="target">The publisher or processor whose subscription timed out.</param>
        void SubscriptionTimedOut(IUntypedPublisher target);

        /// <summary>
        /// Callback that should ensure that the target is canceled with the given cause.
        /// </summary>
        /// <param name="target">The publisher or processor to cancel.</param>
        /// <param name="cause">The timeout failure to signal to the target.</param>
        void HandleSubscriptionTimeout(IUntypedPublisher target, Exception cause);
    }
}
