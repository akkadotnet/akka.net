//-----------------------------------------------------------------------
// <copyright file="QueueOfferResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// TBD
    /// </summary>
    public sealed class QueueOfferResult
    {
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Enqueued : IQueueOfferResult
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Enqueued Instance = new Enqueued();

            private Enqueued()
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Dropped : IQueueOfferResult
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Dropped Instance = new Dropped();

            private Dropped()
            {
            }
        }

        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        public sealed class QueueClosed : IQueueOfferResult
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly QueueClosed Instance = new QueueClosed();

            private QueueClosed()
            {
            }
        }
    }
}
