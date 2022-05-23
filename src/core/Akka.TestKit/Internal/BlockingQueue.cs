//-----------------------------------------------------------------------
// <copyright file="BlockingQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// This class represents a queue with the same characteristics of a <see cref="BlockingCollection{T}"/>.
    /// The queue can enqueue items at either the front (FIFO) or the end (LIFO) of the collection.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type of item to store.</typeparam>
    public class BlockingQueue<T> : ITestQueue<T>
    {
        private readonly BlockingCollection<Positioned> _collection = new BlockingCollection<Positioned>(new QueueWithAddFirst());

        public int Count { get { return _collection.Count; } }

        public void Enqueue(T item)
        {
            if (!_collection.TryAdd(new Positioned(item)))
                throw new InvalidOperationException("Failed to enqueue item into the queue.");
        }

        public async ValueTask EnqueueAsync(T item)
        {
            Enqueue(item);
        }

        [Obsolete("This method will be removed from the public API in the future")] 
        public void AddFirst(T item)
        {
            if(!_collection.TryAdd(new Positioned(item, first:true)))
                throw new InvalidOperationException("Failed to enqueue item into the head of the queue.");
        }

        public bool TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            return _collection.TryAdd(new Positioned(item), millisecondsTimeout, cancellationToken);
        }

        public async ValueTask<bool> TryEnqueueAsync(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            return TryEnqueue(item, millisecondsTimeout, cancellationToken);
        }

        public bool TryTake(out T item, CancellationToken cancellationToken = default)
        {
            if(_collection.TryTake(out var p, 0, cancellationToken))
            {
                item = p.Value;
                return true;
            }
            item = default;
            return false;
        }

        public async ValueTask<(bool success, T item)> TryTakeAsync(CancellationToken cancellationToken)
        {
            var result = TryTake(out var item);
            return (result, item);
        }

        public bool TryTake(out T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if(_collection.TryTake(out var p, millisecondsTimeout, cancellationToken))
            {
                item = p.Value;
                return true;
            }
            item = default;
            return false;
        }

        public async ValueTask<(bool success, T item)> TryTakeAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            var result = TryTake(out var item, millisecondsTimeout, cancellationToken);
            return (result, item);
        }

        public T Take(CancellationToken cancellationToken)
        {
            var p = _collection.Take(cancellationToken);
            return p.Value;
        }

        public async ValueTask<T> TakeAsync(CancellationToken cancellationToken)
        {
            return _collection.Take(cancellationToken).Value;
        }

        #region Peek methods

        public bool TryPeek(out T item)
        {
            if(_collection.TryTake(out var p))
            {
                item = p.Value;
                AddFirst(item);
                return true;
            }
            item = default;
            return false;
        }

        public async ValueTask<(bool success, T item)> TryPeekAsync(CancellationToken cancellationToken)
        {
            if(_collection.TryTake(out var p))
            {
                var item = p.Value;
                AddFirst(item);
                return (true, item);
            }
            return (false, default);
        }

        public bool TryPeek(out T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if(_collection.TryTake(out var p, millisecondsTimeout, cancellationToken))
            {
                item = p.Value;
                AddFirst(item);
                return true;
            }
            item = default;
            return false;
        }

        public async ValueTask<(bool success, T item)> TryPeekAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if(_collection.TryTake(out var p, millisecondsTimeout, cancellationToken))
            {
                var item = p.Value;
                AddFirst(item);
                return (true, item);
            }
            return (false, default);
        }
        
        public T Peek(CancellationToken cancellationToken)
        {
            var p = _collection.Take(cancellationToken);
            AddFirst(p.Value);
            return p.Value;
        }

        public async ValueTask<T> PeekAsync(CancellationToken cancellationToken)
        {
            var val = _collection.Take(cancellationToken).Value;
            AddFirst(val);
            return val;
        }
        #endregion
        
        public List<T> ToList()
        {
            var positionArray = _collection.ToArray();
            return positionArray.Select(positioned => positioned.Value).ToList();
        }


        private class Positioned
        {
            private readonly T _value;
            private readonly bool _first;

            public Positioned(T value, bool first = false)
            {
                _value = value;
                _first = first;
            }

            public T Value { get { return _value; } }
            public bool First { get { return _first; } }
        }

        private class QueueWithAddFirst : IProducerConsumerCollection<Positioned>
        {
            private readonly LinkedList<Positioned> _list = new LinkedList<Positioned>();

            public int Count { 
                get
                {
                    lock (SyncRoot)
                    {
                        return _list.Count;
                    }
                }
            }

            public bool TryAdd(Positioned item)
            {
                lock (SyncRoot)
                {
                    if(item.First)
                        _list.AddFirst(item);
                    else
                        _list.AddLast(item);
                    return true;
                }
            }

            public bool TryTake(out Positioned item)
            {
                lock(SyncRoot)
                {
                    if(_list.Count == 0)
                    {
                        item = null;
                        return false;
                    }

                    item = _list.First.Value;
                    _list.RemoveFirst();
                    return true;
                }
            }

            public void CopyTo(Positioned[] array, int index)
            {
                lock(SyncRoot)
                {
                    _list.CopyTo(array, index);
                }
            }


            public void CopyTo(Array array, int index)
            {
                lock(SyncRoot)
                {
                    ((ICollection)_list).CopyTo(array, index);
                }
            }

            public Positioned[] ToArray()
            {
                lock(SyncRoot)
                {
                    return _list.ToArray();
                }
            }


            public IEnumerator<Positioned> GetEnumerator()
            {
                lock(SyncRoot)
                {
                    //We must create a copy
                    return new List<Positioned>(_list).GetEnumerator();
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public object SyncRoot { get; } = new object();

            public bool IsSynchronized => true;
        }
    }
}
