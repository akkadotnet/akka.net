//-----------------------------------------------------------------------
// <copyright file="ITestActorQueue.cs" company="Akka.NET Project">
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
    public interface ITestActorQueueProducer<in T>
    {
        /// <summary>Adds the specified item to the queue.</summary>
        /// <param name="item">The item.</param>
        void Enqueue(T item);
    }

    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public interface ITestActorQueue<T> : ITestActorQueueProducer<T>
    {
        /// <summary>
        /// Get all messages.
        /// </summary>
        /// <returns></returns>
        IEnumerable<T> GetAll();
    }
}

