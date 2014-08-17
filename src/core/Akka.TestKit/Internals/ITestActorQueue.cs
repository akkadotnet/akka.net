using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Contexts;
using Akka.Event;

namespace Akka.TestKit.Internals
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