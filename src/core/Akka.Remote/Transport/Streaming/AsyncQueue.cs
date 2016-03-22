using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Remote.Transport.Streaming
{
    //TODO This queue can be optimized by making it single reader
    // and using a custom awaiter instead of TaskCompletionSource
    internal sealed class AsyncQueue<T> : IDisposable
    {
        private class Waiter
        {
            private readonly TaskCompletionSource<T> _completion;

            public Task<T> Task
            {
                get { return _completion.Task; }
            }

            public Waiter()
            {
                _completion = new TaskCompletionSource<T>();
            }

            public void SetResult(T item)
            {
                _completion.TrySetResult(item);
            }

            public void SetCanceled()
            {
                _completion.TrySetCanceled();
            }
        }

        private readonly Queue<T> _queue;
        private readonly Queue<Waiter> _waiters;

        public bool IsDisposed { get; private set; }

        public int Count
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }

        public AsyncQueue()
        {
            _queue = new Queue<T>();
            _waiters = new Queue<Waiter>();
        }

        public bool Enqueue(T item)
        {
            Waiter completedWaiter = null;

            lock (_queue)
            {
                if (IsDisposed)
                    return true;

                if (_waiters.Count > 0)
                {
                    completedWaiter = _waiters.Dequeue();
                }
                else
                {
                    _queue.Enqueue(item);
                }
            }

            if (completedWaiter != null)
                ThreadPool.QueueUserWorkItem(_ => completedWaiter.SetResult(item));

            return true;
        }

        public Task<T> Dequeue()
        {
            Waiter waiter = new Waiter();

            lock (_queue)
            {
                if (IsDisposed)
                {
                    waiter.SetCanceled();
                }
                else if (_queue.Count > 0)
                {
                    T value = _queue.Dequeue();
                    waiter.SetResult(value);
                }
                else
                {
                    _waiters.Enqueue(waiter);
                }
            }

            return waiter.Task;
        }

        public void Dispose()
        {
            List<Waiter> waiters;
            lock (_queue)
            {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                waiters = _waiters.ToList();
            }

            Task.Run(() =>
            {
                foreach (Waiter waiter in waiters)
                    waiter.SetCanceled();
            });
        }
    }
}