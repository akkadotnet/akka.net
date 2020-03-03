//-----------------------------------------------------------------------
// <copyright file="BlockingCollectionTestActorQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// This class represents an implementation of <see cref="ITestActorQueue{T}"/>
    /// that uses a <see cref="BlockingQueue{T}"/> as its backing store.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type of item to store.</typeparam>
    public class BlockingCollectionTestActorQueue<T> : ITestActorQueue<T>
    {
        private readonly BlockingQueue<T> _queue;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockingCollectionTestActorQueue{T}"/> class.
        /// </summary>
        /// <param name="queue">The queue to use as the backing store.</param>
        public BlockingCollectionTestActorQueue(BlockingQueue<T> queue)
        {
            _queue = queue;
        }

        /// <summary>
        /// Adds the specified item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
        }

        /// <summary>
        /// Return an <see cref="List{T}"/> for the items inside the collection.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> for the <see cref="BlockingCollectionTestActorQueue{T}"/> items</returns>
        public List<T> ToList()
        {
            return _queue.ToList();
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
            while(_queue.TryTake(out item))
            {
                yield return item;
            }
        }
    }
}
