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
using System.Threading.Channels;
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
    public class ChannelQueue<T>
    {
        private readonly Channel<Positioned> _channel = Channel.CreateBounded<Positioned>(2000);


        /// <summary>
        /// The number of items that are currently in the queue.
        /// </summary>
        public int Count { get { return _channel.Reader.Count; } }

        /// <summary>
        /// Adds the specified item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public async ValueTask Enqueue(T item)
        {
            var writer = _channel.Writer;
            await writer.WriteAsync(new Positioned(item));

            //if (!writer. _channel.TryAdd(new Positioned(item)))
            // 
                //throw new InvalidOperationException("Failed to enqueue item into the queue.");
        }

        /// <summary>
        /// Adds the specified item to the front of the queue. 
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public async ValueTask AddFirst(T item)
        {
            var writer = _channel.Writer;
            await writer.WriteAsync(new Positioned(item, first: true));

            //if (!_channel.TryAdd(new Positioned(item, first:true)))
                //throw new InvalidOperationException("Failed to enqueue item into the head of the queue.");
        }

        /// <summary>
        /// Tries to add the specified item to the end of the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the add to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the add completed within the specified timeout; otherwise, <c>false</c>.</returns>
        public async ValueTask<bool> TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            var result = _channel.Writer.TryWrite(new Positioned(item));
            return await Task.FromResult(result);
            //return _channel.TryAdd(new Positioned(item), millisecondsTimeout, cancellationToken);
        }

        /// <summary>
        /// Tries to remove the specified item from the queue.
        /// </summary>
        /// <returns><c>(true, T)</c> if the item was removed; otherwise, <c>(false, default)</c>.</returns>
        public async ValueTask<(bool Success, T Item)> TryTake()
        {
            var reader = _channel.Reader;   
            if(reader.TryRead(out var p))
            {
                return await Task.FromResult((true, p.Value));
            }
            return await Task.FromResult((false, default(T)));
        }

        /// <summary>
        /// Tries to remove the specified item from the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the remove to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the remove completed within the specified timeout; otherwise, <c>false</c>.</returns>
        public async ValueTask<(bool Success, T Item)> TryTake(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            var reader = _channel.Reader;
            var p = await reader.ReadAsync(cancellationToken); 
            if(p != null)
                return (true, p.Value);

            return (false, default(T)); 
        }

        /// <summary>
        /// Removes an item from the collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">
        /// This exception is thrown when the operation is canceled.
        /// </exception>
        /// <returns>The item removed from the collection.</returns>
        public async ValueTask<T> Take(CancellationToken cancellationToken)
        {
            var reader = _channel.Reader;
            var p = await reader.ReadAsync(cancellationToken);
            return p.Value;
        }

        /// <summary>
        /// Copies the items from the <see cref="ChannelQueue{T}"/> instance into a new <see cref="List{T}"/>.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> containing copies of the elements of the collection</returns>
        public List<T> ToList()
        {
            var positionArray = _channel.Reader.ToArray();
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
