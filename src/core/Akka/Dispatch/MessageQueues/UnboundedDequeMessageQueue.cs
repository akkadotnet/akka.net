//-----------------------------------------------------------------------
// <copyright file="UnboundedDequeMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>
    /// An unbounded double-ended queue. Used in combination with <see cref="IStash"/>.
    /// </summary>
    public class UnboundedDequeMessageQueue : DequeWrapperMessageQueue, IUnboundedDequeBasedMessageQueueSemantics
    {
        /// <summary>
        /// TBD
        /// </summary>
        public UnboundedDequeMessageQueue() : base(new UnboundedMessageQueue())
        {
        }
    }

    /// <summary>
    /// A bounded double-ended queue. Used in combination with <see cref="IStash"/>.
    /// </summary>
    public class BoundedDequeMessageQueue : DequeWrapperMessageQueue, IBoundedDequeBasedMessageQueueSemantics
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="boundedCapacity">TBD</param>
        /// <param name="pushTimeOut">TBD</param>
        public BoundedDequeMessageQueue(int boundedCapacity, TimeSpan pushTimeOut)
            : base(new BoundedMessageQueue(boundedCapacity, pushTimeOut))
        {
            PushTimeOut = pushTimeOut;
        }

        /// <summary>
        /// Gets the underlying <see cref="BoundedMessageQueue.PushTimeOut"/> 
        /// </summary>
        /// <remarks>
        /// This method is never called, but had to be implemented to support the <see cref="IBoundedDequeBasedMessageQueueSemantics"/> interface.
        /// </remarks>
        public TimeSpan PushTimeOut { get; }
    }
}

