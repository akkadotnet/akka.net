//-----------------------------------------------------------------------
// <copyright file="SinkholeSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <typeparam name="TIn">TBD</typeparam>
    [InternalApi]
    public sealed class SinkholeSubscriber<TIn> : ISubscriber<TIn>
    {
        private readonly TaskCompletionSource<NotUsed> _whenCompleted;
        private bool _running;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="whenCompleted">TBD</param>
        public SinkholeSubscriber(TaskCompletionSource<NotUsed> whenCompleted)
        {
            _whenCompleted = whenCompleted;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            _whenCompleted.TrySetException(cause);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void OnComplete() => _whenCompleted.TrySetResult(NotUsed.Instance);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(TIn element) => ReactiveStreamsCompliance.RequireNonNullElement(element);
    }
}
