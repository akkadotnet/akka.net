//-----------------------------------------------------------------------
// <copyright file="ISemantics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Dispatch
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISemantics
    {
    }

    /// <summary> 
    /// Semantics for message queues that support multiple consumers 
    /// </summary>
    public interface IMultipleConsumerSemantics : ISemantics
    {
    }

    /// <summary> 
    /// Semantics for message queues that have an unbounded size 
    /// </summary>
    public interface IUnboundedMessageQueueSemantics : ISemantics
    {
    }

    /// <summary> 
    /// Semantics for message queues that have a bounded size 
    /// </summary>
    public interface IBoundedMessageQueueSemantics : ISemantics
    {
        /// <summary> 
        /// The enqueue time to wait until message is dropped to deadletters if the message queue is full 
        /// </summary>
        TimeSpan PushTimeOut { get; set; }
    }

    /// <summary> 
    /// Semantics for message queues that are blocking 
    /// </summary>
    public interface IBlockingMessageQueueSemantics : IMultipleConsumerSemantics
    {
        /// <summary> 
        /// The time to wait on a lock before throwing an timeout exception. 
        /// </summary>
        TimeSpan BlockTimeOut { get; set; }
    }

    /// <summary>
    /// Semantics for message queues that are Double-Ended.
    /// </summary>
    public interface IDequeBasedMessageQueueSemantics : ISemantics
    {
        void EnqueueFirst(Envelope envelope);
    }

    /// <summary>
    /// Semantics for message queues that are Double-Ended and unbounded
    /// </summary>
    public interface IUnboundedDequeBasedMessageQueueSemantics : IDequeBasedMessageQueueSemantics,
        IUnboundedMessageQueueSemantics
    {
    }

    /// <summary>
    /// Semantics for message queues that are Double-Ended and bounded
    /// </summary>
    public interface IBoundedDequeBasedMessageQueueSemantics : IDequeBasedMessageQueueSemantics,
        IUnboundedMessageQueueSemantics //TODO: make this Bounded once we have BoundedMessageQueues
    {
    }
}

