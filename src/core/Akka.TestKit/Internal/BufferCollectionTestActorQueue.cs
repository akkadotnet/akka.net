//-----------------------------------------------------------------------
// <copyright file="BlockingCollectionTestActorQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// This class represents an implementation of <see cref="ITestActorQueue{T}"/>
    /// that uses a <see cref="BufferBlock{T}"/> as its backing store.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type of item to store.</typeparam>
    public class BufferCollectionTestActorQueue<T> : ITestActorQueue<T>
    {
        private readonly BufferBlock<T> _queue;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferCollectionTestActorQueue{T}"/> class.
        /// </summary>
        /// <param name="queue">The queue to use as the backing store.</param>
        public BufferCollectionTestActorQueue(BufferBlock<T> queue)
        {
            _queue = queue;
        }

        /// <summary>
        /// Adds the specified item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public async ValueTask Enqueue(T item)
        {
            await _queue.SendAsync(item);
        }

        /// <summary>
        /// Return an <see cref="List{T}"/> for the items inside the collection.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> for the <see cref="BufferCollectionTestActorQueue{T}"/> items</returns>
        public List<T> ToList()
        {
            _queue.TryReceiveAll(out var list);
            return list.ToList();
        }
        
        /// <summary>
        /// <para>
        /// Retrieves all items from the queue.
        /// </para>
        /// <note>
        /// This will remove all items from the queue.
        /// </note>
        /// </summary>
        /// <returns>An enumeration of all items removed from the queue.</returns>
        public IEnumerable<T> GetAll()
        {
            T item;
            while(_queue.TryReceive(out item))
            {
                yield return item;
            }
        }
    }
}
