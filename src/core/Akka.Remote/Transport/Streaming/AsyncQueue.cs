using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Akka.Dispatch;

namespace Akka.Remote.Transport.Streaming
{
    /// <summary>
    /// Asynchronous multi-producer/single-consumer queue.
    /// <remarks>
    /// This classed is optimized for usage in StreamAssociationHandle, not intended for reuse.
    /// </remarks>
    /// </summary>
    internal sealed class AsyncQueue<T> : IDisposable
    {
        internal interface IFutureItem : INotifyCompletion
        {
            T Value { get; }
            bool IsCanceled { get; }
            bool IsCompleted { get; }

            IFutureItem GetAwaiter();
            void GetResult();
        }

        //TODO Try the ValueTask trick
        //https://github.com/dotnet/corefx/blob/master/src/System.Threading.Tasks.Extensions/src/System/Threading/Tasks/ValueTask.cs

        internal class FutureItem : IFutureItem
        {
            private Action _continuation;
            private T _value;
            private volatile bool _isCanceled;
            private volatile bool _isCompleted;

            public bool IsCompleted => _isCompleted;

            public bool IsCanceled => _isCanceled;

            public T Value
            {
                get
                {
                    lock (this)
                    {
                        if (!_isCompleted)
                            throw new InvalidOperationException("Operation is not completed");

                        if (_isCanceled)
                            throw new OperationCanceledException();

                        return _value;
                    }
                }
            }

            public void SetItem(T item, MessageDispatcher dispatcher = null)
            {
                Action continuation;
                lock (this)
                {
                    if (_isCompleted)
                        return;

                    _value = item;
                    _isCompleted = true;
                    continuation = _continuation;
                    _continuation = null;
                }

                TryInvoke(continuation, dispatcher);
            }

            public void SetCanceled(MessageDispatcher dispatcher = null)
            {
                Action continuation;
                lock (this)
                {
                    if (_isCompleted)
                        return;

                    _isCanceled = true;
                    _isCompleted = true;
                    continuation = _continuation;
                    _continuation = null;
                }

                TryInvoke(continuation, dispatcher);
            }

            private static void TryInvoke(Action continuation, MessageDispatcher dispatcher = null)
            {
                if (continuation == null)
                    return;

                if (dispatcher != null)
                {
                    dispatcher.Schedule(continuation);
                    return;
                }

                continuation();
            }

            public IFutureItem GetAwaiter()
            {
                return this;
            }

            public void GetResult()
            {
                lock (this)
                {
                    if (!_isCompleted)
                        throw new InvalidOperationException("Operation is not completed");
                }
            }

            public void OnCompleted(Action continuation)
            {
                bool alreadyCompleted = false;
                lock (this)
                {
                    _continuation = continuation;

                    if (_isCompleted)
                        alreadyCompleted = true;
                }

                if (alreadyCompleted)
                    continuation();
            }
        }

        // We use this scheduler when completing the FutureItem to prevent executing arbitrary code synchronously on the 
        // calling thread.
        private readonly MessageDispatcher _dispatcher;

        private readonly Queue<T> _queue;
        private FutureItem _pendingDequeue;

        private bool _addingCompleted;
        private bool _isDisposed;

        public AsyncQueue(MessageDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _queue = new Queue<T>();
        }

        /// <summary>
        /// Add an item to the queue. Can be called concurrently.
        /// </summary>
        /// <returns>True if the item was enqueued, otherwise False.</returns>
        public bool Enqueue(T item)
        {
            FutureItem completedWaiter = null;

            lock (_queue)
            {
                if (_addingCompleted)
                    return false;

                if (_pendingDequeue != null)
                {
                    completedWaiter = _pendingDequeue;
                    _pendingDequeue = null;
                }
                else
                {
                    _queue.Enqueue(item);
                }
            }

            completedWaiter?.SetItem(item, _dispatcher);

            return true;
        }

        /// <summary>
        /// Dequeue an item from the queue. The queue is single consumer, DequeueAsync must not be called again until the previous dequeue operation have completed.
        /// Warning: The returned IFutureItem must not be awaited more than once.
        /// </summary>
        /// <returns>A future of the dequeued item.</returns>
        public IFutureItem DequeueAsync()
        {
            FutureItem futureItem = new FutureItem();

            lock (_queue)
            {
                if (_pendingDequeue != null)
                    throw new InvalidOperationException("Dequeue operation is already in progress. This queue is single consumer.");

                if (_isDisposed)
                {
                    futureItem.SetCanceled();
                }
                else if (_queue.Count > 0)
                {
                    T value = _queue.Dequeue();
                    futureItem.SetItem(value);
                }
                else if (_addingCompleted)
                {
                    futureItem.SetCanceled();
                }
                else
                {
                    _pendingDequeue = futureItem;
                }
            }

            return futureItem;
        }

        public void CompleteAdding()
        {
            FutureItem pendingDequeue;

            lock (_queue)
            {
                _addingCompleted = true;
                pendingDequeue = _pendingDequeue;
            }

            pendingDequeue?.SetCanceled(_dispatcher);
        }

        public void Dispose()
        {
            FutureItem pendingDequeue;
            lock (_queue)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                _addingCompleted = true;

                pendingDequeue = _pendingDequeue;
            }

            pendingDequeue?.SetCanceled(_dispatcher);
        }
    }
}