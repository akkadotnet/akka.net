//-----------------------------------------------------------------------
// <copyright file="SinkholeSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Annotations;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">The type of elements accepted and discarded by this subscriber.</typeparam>
    [InternalApi]
    public sealed class SinkholeSubscriber<TIn> : ISubscriber<TIn>
    {
        private readonly TaskCompletionSource<NotUsed> _whenCompleted;
        private bool _running;

        /// <summary>
        /// Creates a subscriber that drains its subscription and completes a task when the publisher terminates.
        /// </summary>
        /// <param name="whenCompleted">The task completion source completed when the subscription terminates.</param>
        public SinkholeSubscriber(TaskCompletionSource<NotUsed> whenCompleted)
        {
            _whenCompleted = whenCompleted;
        }

        /// <summary>
        /// Requests all elements from the first subscription and cancels any subsequent subscription.
        /// </summary>
        /// <param name="subscription">The subscription supplied by the publisher.</param>
        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
            if (_running)
                subscription.Cancel();
            else
            {
                _running = true;
                subscription.Request(long.MaxValue);
            }
        }

        /// <summary>
        /// Completes the task with the publisher's failure.
        /// </summary>
        /// <param name="cause">The failure reported by the publisher.</param>
        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            _whenCompleted.TrySetException(cause);
        }

        /// <summary>
        /// Completes the task when the publisher completes normally.
        /// </summary>
        public void OnComplete() => _whenCompleted.TrySetResult(NotUsed.Instance);

        /// <summary>
        /// Validates and discards an element delivered by the publisher.
        /// </summary>
        /// <param name="element">The element delivered by the publisher.</param>
        public void OnNext(TIn element) => ReactiveStreamsCompliance.RequireNonNullElement(element);
    }
}
