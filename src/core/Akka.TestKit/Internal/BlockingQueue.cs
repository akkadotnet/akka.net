//-----------------------------------------------------------------------
// <copyright file="BlockingQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// This behaves exactly like a <see cref="BlockingCollection{T}"/> with a queue as the underlying storage
    /// except it adds the possibility to add an item first, making it a LIFO.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type of item to store</typeparam>
    public class BlockingQueue<T>
    {
        private readonly BlockingCollection<Positioned> _collection = new BlockingCollection<Positioned>();

        public int Count { get { return _collection.Count; } }

        public void Enqueue(T item)
        {
            _collection.TryAdd(new Positioned(item));
        }

        public void AddFirst(T item)
        {
            _collection.TryAdd(new Positioned(item, first:true));
        }

        public bool TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
           return  _collection.TryAdd(new Positioned(item),millisecondsTimeout, cancellationToken);
        }

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
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation..</param>
        /// <returns>The item removed from the collection.</returns>
        /// <exception cref="OperationCanceledException">Thrown if the operation was cancelled</exception>
        public T Take(CancellationToken cancellationToken)
        {
            var p = _collection.Take(cancellationToken);
            return p.Value;
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

