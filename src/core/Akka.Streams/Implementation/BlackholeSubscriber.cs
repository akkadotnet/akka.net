using System;
using System.Threading.Tasks;

namespace Akka.Streams.Implementation
{
    public class BlackholeSubscriber<T> : ISubscriber<T>
    {
        private readonly int _highWatermark;
        private readonly TaskCompletionSource<object> _onCompletion;
        private readonly int _lowWatermark;
        private long _requested = 0;
        private ISubscription _subscription = null;

        public BlackholeSubscriber(int highWatermark, TaskCompletionSource<object> onCompletion)
        {
            _highWatermark = highWatermark;
            _onCompletion = onCompletion;
            _lowWatermark = Math.Max(1, highWatermark / 2);
        }

        public void OnNext(T element)
        {
            OnNext((object)element);
        }

        public void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription", "OnSubscriber expects subscription to be defined");
            if (_subscription != null) subscription.Cancel();
            else
            {
                _subscription = subscription;
                RequestMore();
            }
        }

        public void OnNext(object element)
        {
            if (element == null) throw new ArgumentNullException("element", "OnNext expects element to be defined");
            _requested--;
            RequestMore();
        }

        public void OnError(Exception cause)
        {
            if (cause == null) throw new ArgumentNullException("cause", "OnError expects error cause to be defined");
            _onCompletion.TrySetException(cause);
        }

        public void OnComplete()
        {
            _onCompletion.TrySetResult(null);
        }

        private void RequestMore()
        {
            if (_requested < _lowWatermark)
            {
                var amount = _highWatermark - _requested;
                _requested += amount;
                _subscription.Request(amount);
            }
        }
    }
}