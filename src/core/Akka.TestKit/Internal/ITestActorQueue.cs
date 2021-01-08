//-----------------------------------------------------------------------
// <copyright file="ITestActorQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface ITestActorQueueProducer<in T>
    {
        /// <summary>Adds the specified item to the queue.</summary>
        /// <param name="item">The item.</param>
        void Enqueue(T item);
    }

    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface ITestActorQueue<T> : ITestActorQueueProducer<T>
    {
        /// <summary>
        /// Copies all the items from the <see cref="ITestActorQueue{T}"/> instance into a new <see cref="List{T}"/>
        /// </summary>
        /// <returns>TBD</returns>
        List<T> ToList();

        /// <summary>
        /// Get all messages.
        /// </summary>
        /// <returns>TBD</returns>
        IEnumerable<T> GetAll();
    }
}
