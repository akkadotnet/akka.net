//-----------------------------------------------------------------------
// <copyright file="BlockingQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly BlockingCollection<Positioned> _collection = new BlockingCollection<Positioned>();

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
            _collection.TryAdd(new Positioned(item));
        }

        /// <summary>
        /// Adds the specified item to the front of the queue. 
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public void AddFirst(T item)
        {
            _collection.TryAdd(new Positioned(item, first:true));
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
            Positioned p;
            if(_collection.TryTake(out p))
            {
                item = p.Value;
                return true;
            }
            item = default(T);
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
            Positioned p;
            if(_collection.TryTake(out p,millisecondsTimeout,cancellationToken))
            {
                item = p.Value;
                return true;
            }
            item = default(T);
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
        public List<T> ToList()
        {
            var positionArray = _collection.ToArray();
            var items = new List<T>();
            foreach (var positioned in positionArray)
            {
                items.Add(positioned.Value);
            }
            return items;
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
            private readonly object _lock = new object();

            public int Count { get { return _list.Count; } }

            public bool TryAdd(Positioned item)
            {
                if(item.First)
                {
                    lock(_lock)
                    {
                        _list.AddFirst(item);
                    }
                }
                else
                {
                    lock(_lock)
                    {
                        _list.AddLast(item);
                    }
                }
                return true;
            }

            public bool TryTake(out Positioned item)
            {
                var result = false;
                if(_list.Count == 0)
                {
                    item = null;
                }
                else
                {
                    lock(_lock)
                    {
                        if(_list.Count == 0)
                        {
                            item = null;
                        }
                        else
                        {
                            item = _list.First.Value;
                            _list.RemoveFirst();
                            result = true;
                        }
                    }
                }
                return result;
            }


            public void CopyTo(Positioned[] array, int index)
            {
                lock(_lock)
                {
                    _list.CopyTo(array, index);
                }
            }


            public void CopyTo(Array array, int index)
            {
                lock(_lock)
                {
                    ((ICollection)_list).CopyTo(array, index);
                }
            }

            public Positioned[] ToArray()
            {
                Positioned[] array;
                lock(_lock)
                {
                    array = _list.ToArray();
                }
                return array;
            }


            public IEnumerator<Positioned> GetEnumerator()
            {
                //We must create a copy
                List<Positioned> copy;
                lock(_lock)
                {
                    copy = new List<Positioned>(_list);
                }
                return copy.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }


            public object SyncRoot { get { throw new NotImplementedException(); } }

            public bool IsSynchronized { get { return false; } }
        }
    }
}
