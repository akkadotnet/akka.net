//-----------------------------------------------------------------------
// <copyright file="BlockingCollectionTestActorQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class BlockingCollectionTestActorQueue<T> : ITestActorQueue<T>
    {
        private readonly BlockingQueue<T> _queue;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="queue">TBD</param>
        public BlockingCollectionTestActorQueue(BlockingQueue<T> queue)
        {
            _queue = queue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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