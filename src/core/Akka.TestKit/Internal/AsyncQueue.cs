//-----------------------------------------------------------------------
// <copyright file="AsyncQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.TestKit.Internal
{
    public class AsyncQueue<T>: ITestQueue<T> where T: class
    {
        private readonly ConcurrentQueue<T> _collection = new();

        public int Count => _collection.Count;

        public void Enqueue(T item) => _collection.Enqueue(item);

        public ValueTask EnqueueAsync(T item)
        {
            _collection.Enqueue(item);
            return new ValueTask();
        }

        public bool TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            try
            {
                _collection.Enqueue(item);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public ValueTask<bool> TryEnqueueAsync(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            try
            {
                _collection.Enqueue(item);
                return new ValueTask<bool>(true);
            }
            catch
            {
                return new ValueTask<bool>(false);
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
                return _collection.TryDequeue(out item);
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

        public ValueTask<(bool success, T item)> TryTakeAsync(CancellationToken cancellationToken)
        {
            try
            {
                _collection.TryDequeue(out var result);
                return new ValueTask<(bool success, T item)>((true, result));
            }
            catch
            {
                return new ValueTask<(bool success, T item)>((false, default));
            }
        }

        public async ValueTask<(bool success, T item)> TryTakeAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return await TryTakeAsync(cts.Token);
        }

        public T Take(CancellationToken cancellationToken)
        {
            if(!_collection.TryDequeue(out var item))
                throw new InvalidOperationException("Failed to dequeue item from the queue.");
            return item;
        }

        public ValueTask<T> TakeAsync(CancellationToken cancellationToken) => new(Take(cancellationToken)); 

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

        public ValueTask<(bool success, T item)> TryPeekAsync(CancellationToken cancellationToken)
        {
            try
            {
                _collection.TryPeek(out var result);
                return new ValueTask<(bool success, T item)>((true, result));
            }
            catch
            {
                return new ValueTask<(bool success, T item)>((false, default));
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

        public ValueTask<T> PeekAsync(CancellationToken cancellationToken) => new(Peek(cancellationToken));
        
        public List<T> ToList()
        {
            return _collection.ToList();
        }
    }
}
