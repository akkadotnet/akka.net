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

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// This class represents a queue with the same characteristics of a <see cref="BlockingCollection{T}"/>.
    /// The queue can enqueue items at either the front (FIFO) or the end (LIFO) of the collection.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type of item to store.</typeparam>
    public class BlockingQueue<T>
    {
        private readonly BlockingCollection<Positioned> _collection = new BlockingCollection<Positioned>(new QueueWithAddFirst());

        /// <summary>
        /// The number of items that are currently in the queue.
        /// </summary>
        public int Count { get { return _collection.Count; } }

        /// <summary>
        /// Adds the specified item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public void Enqueue(T item)
        {
            if (!_collection.TryAdd(new Positioned(item)))
                throw new InvalidOperationException("Failed to enqueue item into the queue.");
        }

        /// <summary>
        /// Adds the specified item to the front of the queue. 
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public void AddFirst(T item)
        {
            if(!_collection.TryAdd(new Positioned(item, first:true)))
                throw new InvalidOperationException("Failed to enqueue item into the head of the queue.");
        }

        /// <summary>
        /// Tries to add the specified item to the end of the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the add to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the add completed within the specified timeout; otherwise, <c>false</c>.</returns>
        public bool TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            return _collection.TryAdd(new Positioned(item), millisecondsTimeout, cancellationToken);
        }

        /// <summary>
        /// Tries to remove the specified item from the queue.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <returns><c>true</c> if the item was removed; otherwise, <c>false</c>.</returns>
        public bool TryTake(out T item)
        {
            if(_collection.TryTake(out var p))
            {
                item = p.Value;
                return true;
            }
            item = default;
            return false;
        }

        /// <summary>
        /// Tries to remove the specified item from the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the remove to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the remove completed within the specified timeout; otherwise, <c>false</c>.</returns>
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

        /// <summary>
        /// Removes an item from the collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">
        /// This exception is thrown when the operation is canceled.
        /// </exception>
        /// <returns>The item removed from the collection.</returns>
        public T Take(CancellationToken cancellationToken)
        {
            var p = _collection.Take(cancellationToken);
            return p.Value;
        }

        /// <summary>
        /// Copies the items from the <see cref="BlockingQueue{T}"/> instance into a new <see cref="List{T}"/>.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> containing copies of the elements of the collection</returns>
        [Obsolete("This method will be removed in the future")] 
        public List<T> ToList()
        {
            var positionArray = _collection.ToArray();
            return positionArray.Select(positioned => positioned.Value).ToList();
        }

        /// <summary>
        /// Empties the items from the <see cref="BlockingQueue{T}"/> instance and returns a <see cref="List{T}"/>
        /// of items emptied from the collection.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> containing all the emptied elements of the collection</returns>
        public List<T> TakeAll(CancellationToken cancellationToken)
        {
            return _collection
                .GetConsumingEnumerable(cancellationToken)
                .Select(p => p.Value).ToList();
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
