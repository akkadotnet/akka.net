//-----------------------------------------------------------------------
// <copyright file="QueueOfferResult.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

    public sealed class QueueOfferResult
    {

        public sealed class Enqueued : IQueueOfferResult
        {
            public static readonly Enqueued Instance = new Enqueued();

            private Enqueued()
            {
            }
        }

        public sealed class Dropped : IQueueOfferResult
        {
            public static readonly Dropped Instance = new Dropped();

            private Dropped()
            {
            }
        }

        public sealed class Failure : IQueueOfferResult
        {
            public Exception Cause { get; }

            public Failure(Exception cause)
            {
                Cause = cause;
            }
        }

        public sealed class QueueClosed : IQueueOfferResult
        {
            public static readonly QueueClosed Instance = new QueueClosed();

            private QueueClosed()
            {
            }
        }
    }
}