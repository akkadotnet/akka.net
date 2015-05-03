﻿//-----------------------------------------------------------------------
// <copyright file="BlockingCollectionTestActorQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class BlockingCollectionTestActorQueue<T> : ITestActorQueue<T>
    {
        private readonly BlockingQueue<T> _queue;

        public BlockingCollectionTestActorQueue(BlockingQueue<T> queue)
        {
            _queue = queue;
        }

        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
        }

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

