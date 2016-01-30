using System;
using System.Reactive.Streams;
using System.Threading.Tasks;

namespace Akka.Streams.Implementation
{

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SinkholeSubscriber<TIn> : ISubscriber<TIn>
    {
        private readonly TaskCompletionSource<Unit> _whenCompleted;
        private bool _running;

        public SinkholeSubscriber(TaskCompletionSource<Unit> whenCompleted)
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

        public void OnComplete() => _whenCompleted.TrySetResult(Unit.Instance);

        public void OnNext(TIn element) => ReactiveStreamsCompliance.RequireNonNullElement(element);

        void ISubscriber.OnNext(object element)
        {
            OnNext((TIn)element);
        }
    }
}
