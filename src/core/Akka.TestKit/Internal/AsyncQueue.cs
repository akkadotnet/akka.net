// //-----------------------------------------------------------------------
// // <copyright file="AsyncQueue.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx.Synchronous;

namespace Akka.TestKit.Internal
{
    public class AsyncQueue<T>: ITestQueue<T> where T: class
    {
        private readonly AsyncPeekableCollection<T> _collection = new AsyncPeekableCollection<T>(new QueueCollection());

        public int Count => _collection.Count;
        
        public void Enqueue(T item) => EnqueueAsync(item).AsTask().WaitAndUnwrapException();

        public ValueTask EnqueueAsync(T item) => new ValueTask(_collection.AddAsync(item)); 

        public bool TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            var task = TryEnqueueAsync(item, millisecondsTimeout, cancellationToken);
            task.AsTask().Wait(cancellationToken);
            return task.Result;
        }

        public async ValueTask<bool> TryEnqueueAsync(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(millisecondsTimeout);
                try
                {
                    await _collection.AddAsync(item, cts.Token);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool TryTake(out T item, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                item = default;
                return false;
            }
            
            try
            {
                // TryRead returns immediately
                return _collection.TryTake(out item);
            }
            catch
            {
                item = default;
                return false;
            }
        }

        public bool TryTake(out T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            try
            {
                var task = TryTakeAsync(millisecondsTimeout, cancellationToken).AsTask();
                task.Wait(cancellationToken);
                item = task.Result.item;
                return task.Result.success;
            }
            catch
            {
                item = default;
                return false;
            }
        }

        public async ValueTask<(bool success, T item)> TryTakeAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _collection.TakeAsync(cancellationToken);
                return (true, result);
            }
            catch
            {
                return (false, default);
            }
        }

        public async ValueTask<(bool success, T item)> TryTakeAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(millisecondsTimeout);
                return await TryTakeAsync(cts.Token);
            }
        }

        public T Take(CancellationToken cancellationToken)
        {
            if(!_collection.TryTake(out var item))
                throw new InvalidOperationException("Failed to dequeue item from the queue.");
            return item;
        }

        public ValueTask<T> TakeAsync(CancellationToken cancellationToken) 
            => new ValueTask<T>(_collection.TakeAsync(cancellationToken));

        public bool TryPeek(out T item) => _collection.TryPeek(out item);

        public bool TryPeek(out T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            try
            {
                var task = TryPeekAsync(millisecondsTimeout, cancellationToken).AsTask();
                task.Wait(cancellationToken);
                item = task.Result.item;
                return task.Result.success;
            }
            catch
            {
                item = default;
                return false;
            }
        }

        public async ValueTask<(bool success, T item)> TryPeekAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _collection.PeekAsync(cancellationToken);
                return (true, result);
            }
            catch
            {
                return (false, default);
            }
        }

        public async ValueTask<(bool success, T item)> TryPeekAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(millisecondsTimeout);
                return await TryPeekAsync(cts.Token);
            }
        }

        public T Peek(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("Peek operation canceled");
                
            if(!_collection.TryPeek(out var item))
                throw new InvalidOperationException("Failed to peek item from the queue.");
            return item;
        }

        public ValueTask<T> PeekAsync(CancellationToken cancellationToken)
            => new ValueTask<T>(_collection.PeekAsync(cancellationToken));
        
        public List<T> ToList()
        {
            throw new System.NotImplementedException();
        }
        
        private class QueueCollection : IPeekableProducerConsumerCollection<T>
        {
            private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

            public int Count { 
                get
                {
                    return _queue.Count;
                }
            }

            public bool TryAdd(T item)
            {
                _queue.Enqueue(item);
                return true;
            }

            public bool TryTake(out T item)
            {
                return _queue.TryDequeue(out item);
            }

            public bool TryPeek(out T item)
            {
                return _queue.TryPeek(out item);
            }

            public void CopyTo(T[] array, int index)
            {
                _queue.CopyTo(array, index);
            }


            public void CopyTo(Array array, int index)
            {
                ((ICollection)_queue).CopyTo(array, index);
            }

            public T[] ToArray()
            {
                return _queue.ToArray();
            }


            public IEnumerator<T> GetEnumerator()
            {
                return new List<T>(_queue).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }


            public bool IsSynchronized => true;

            public object SyncRoot => new object();
        }        
    }
    
}