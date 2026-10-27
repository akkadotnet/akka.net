//-----------------------------------------------------------------------
// <copyright file="QueueOfferResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    /// <summary>
    /// Used as return type for async callbacks to streams
    /// </summary>
    public interface IQueueOfferResult
    {
    }

    /// <summary>
    /// Result types returned by offers to a stream queue.
    /// </summary>
    public sealed class QueueOfferResult
    {
        /// <summary>
        /// Result indicating that the offer was accepted, either by buffering the element or sending it directly downstream.
        /// </summary>
        public sealed class Enqueued : IQueueOfferResult
        {
            /// <summary>
            /// Singleton instance of the enqueued result.
            /// </summary>
            public static readonly Enqueued Instance = new();

            private Enqueued()
            {
            }
        }

        /// <summary>
        /// Result indicating that the offered element was dropped.
        /// </summary>
        public sealed class Dropped : IQueueOfferResult
        {
            /// <summary>
            /// Singleton instance of the dropped result.
            /// </summary>
            public static readonly Dropped Instance = new();

            private Dropped()
            {
            }
        }

        /// <summary>
        /// Result indicating that offering the element failed.
        /// </summary>
        public sealed class Failure : IQueueOfferResult
        {
            /// <summary>
            /// The cause of the failure
            /// </summary>
            public Exception Cause { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="Failure"/> class.
            /// </summary>
            /// <param name="cause">The cause of the failure</param>
            public Failure(Exception cause)
            {
                Cause = cause;
            }
        }

        /// <summary>
        /// Result indicating that the queue is closed and did not accept the offer.
        /// </summary>
        public sealed class QueueClosed : IQueueOfferResult
        {
            /// <summary>
            /// Singleton instance of the closed-queue result.
            /// </summary>
            public static readonly QueueClosed Instance = new();

            private QueueClosed()
            {
            }
        }
    }
}
