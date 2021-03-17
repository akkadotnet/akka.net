using System;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Remote.Artery.Utils.Concurrent
{
    // Best fit approximation of how java CountDownLatch works.
    internal class CountDownLatch :IDisposable
    {
        private int _count;
        private readonly CancellationTokenSource _cts;
        private readonly TaskCompletionSource<object> _tcs;

        public CountDownLatch(int count)
        {
            if (count < 1)
                throw new ArgumentException("count < 1", nameof(count));

            _count = count;
            _cts = new CancellationTokenSource();
            _tcs = new TaskCompletionSource<object>(
                TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously);

            _cts.Token.Register(() =>
            {
                _tcs.SetCanceled();
            });
        }

        public Task Await()
        {
            return _tcs.Task;
        }

        public Task Await(TimeSpan timeout)
        {
            _cts.CancelAfter(timeout);
            return _tcs.Task;
        }

        public void CountDown()
        {
            Interlocked.Decrement(ref _count);
            if(_count == 0)
                _tcs.SetResult(null);
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
