//-----------------------------------------------------------------------
// <copyright file="Queue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams
{
    /// <summary>
    /// This interface allows to have the queue as a data source for some stream.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface ISourceQueue<in T>
    {
        /// <summary>
        /// Method offers next element to a stream and returns task that:
        /// <para>- competes with <see cref="QueueOfferResult.Enqueued"/> if element
        /// is consumed by a stream</para>
        /// <para>- competes with <see cref="QueueOfferResult.Dropped"/> when stream
        /// dropped offered element</para>
        /// <para>- competes with <see cref="QueueOfferResult.QueueClosed"/> when
        /// stream is completed while task is active</para>
        /// <para>- competes with <see cref="QueueOfferResult.Failure"/> when failure
        /// to enqueue element from upstream</para>
        /// <para>- fails if stream is completed or you cannot call offer in this moment
        /// because of implementation rules (like for backpressure mode and full buffer
        /// you need to wait for last offer call task completion.</para>
        /// </summary>
        /// <param name="element">element to send to a stream</param>
        /// <returns>TBD</returns>
        Task<IQueueOfferResult> OfferAsync(T element);

        /// <summary>
        /// Method returns <see cref="Task"/> that will be completed if the stream completes,
        /// or will be failed when the stage faces an internal failure.
        /// </summary>
        /// <returns>TBD</returns>
        Task WatchCompletionAsync();
    }

    /// <summary>
    /// This interface adds completion support to <see cref="ISourceQueue{T}"/>
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface ISourceQueueWithComplete<in T> : ISourceQueue<T>
    {
        /// <summary>
        /// Complete the stream normally. Use <see cref="ISourceQueue{T}.WatchCompletionAsync"/> to be notified of this operation’s success.
        /// </summary>
        void Complete();

        /// <summary>
        /// Complete the stream with a failure. Use <see cref="ISourceQueue{T}.WatchCompletionAsync"/> to be notified of this operation’s success.
        /// </summary>
        /// <param name="ex">TBD</param>
        void Fail(Exception ex);

        /// <summary>
        /// Method returns <see cref="Task"/> that will be completed if the stream completes,
        /// or will be failed when the stage faces an internal failure or the the <see cref="Fail"/> method is invoked.
        /// </summary>
        /// <returns>Task</returns>
        new Task WatchCompletionAsync();
    }

    /// <summary>
    /// Trait allows to have the queue as a sink for some stream.
    /// "SinkQueue" pulls data from stream with backpressure mechanism.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface ISinkQueue<T>
    {
        /// <summary>
        /// Method pulls elements from stream and returns task that:
        /// <para>- fails if stream is finished</para>
        /// <para>- completes with None in case if stream is completed after we got task</para>
        /// <para>- completes with `Some(element)` in case next element is available from stream.</para>
        /// </summary>
        /// <returns>TBD</returns>
        Task<Option<T>> PullAsync();
    }
}
