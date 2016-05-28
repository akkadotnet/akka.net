//-----------------------------------------------------------------------
// <copyright file="Queue.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Streams.Util;

namespace Akka.Streams
{
    /// <summary>
    /// This interface allows to have the queue as a data source for some stream.
    /// </summary>
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
        Task<IQueueOfferResult> OfferAsync(T element);

        /// <summary>
        /// Method returns task that completes when stream is completed and fails
        /// when stream failed.
        /// </summary>
        Task WatchCompletionAsync();
    }

    /// <summary>
    /// Trait allows to have the queue as a sink for some stream.
    /// "SinkQueue" pulls data from stream with backpressure mechanism.
    /// </summary>
    public interface ISinkQueue<T>
    {
        /// <summary>
        /// Method pulls elements from stream and returns task that:
        /// <para>- fails if stream is finished</para>
        /// <para>- completes with None in case if stream is completed after we got task</para>
        /// <para>- completes with `Some(element)` in case next element is available from stream.</para>
        /// </summary>
        Task<Option<T>> PullAsync();
    }
}