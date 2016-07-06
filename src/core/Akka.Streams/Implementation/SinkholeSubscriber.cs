//-----------------------------------------------------------------------
// <copyright file="SinkholeSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class SinkholeSubscriber<TIn> : ISubscriber<TIn>
    {
        private readonly TaskCompletionSource<NotUsed> _whenCompleted;
        private bool _running;

        public SinkholeSubscriber(TaskCompletionSource<NotUsed> whenCompleted)
        {
            _whenCompleted = whenCompleted;
        }

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

        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            _whenCompleted.TrySetException(cause);
        }

        public void OnComplete() => _whenCompleted.TrySetResult(NotUsed.Instance);

        public void OnNext(TIn element) => ReactiveStreamsCompliance.RequireNonNullElement(element);
    }
}
