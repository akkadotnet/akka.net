using System;
using Akka.Actor;

namespace Akka.Dispatch
{
    /// <summary>
    /// 
    /// </summary>
    public interface Semantics
    {
    }

    /// <summary> 
    /// Semantics for message queues that support multiple consumers 
    /// </summary>
    public interface MultipleConsumerSemantics : Semantics
    {
    }

    /// <summary> 
    /// Semantics for message queues that have an unbounded size 
    /// </summary>
    public interface UnboundedMessageQueueSemantics : Semantics
    {
    }

    /// <summary> 
    /// Semantics for message queues that have a bounded size 
    /// </summary>
    public interface IBoundedMessageQueueSemantics : Semantics
    {
        /// <summary> 
        /// The enqueue time to wait until message is dropped to deadletters if the message queue is full 
        /// </summary>
        TimeSpan PushTimeOut { get; set; }
    }

    /// <summary> 
    /// Semantics for message queues that are blocking 
    /// </summary>
    public interface IBlockingMessageQueueSemantics : MultipleConsumerSemantics
    {
        /// <summary> 
        /// The time to wait on a lock before throwing an timeout exception. 
        /// </summary>
        TimeSpan BlockTimeOut { get; set; }
    }

    /// <summary>
    /// Semantics for message queues that are Double-Ended.
    /// </summary>
    public interface DequeBasedMessageQueueSemantics : Semantics
    {
        void EnqueueFirst(Envelope envelope);
    }

    /// <summary>
    /// Semantics for message queues that are Double-Ended and unbounded
    /// </summary>
    public interface UnboundedDequeBasedMessageQueueSemantics : DequeBasedMessageQueueSemantics,
        UnboundedMessageQueueSemantics
    {
    }

    /// <summary>
    /// Semantics for message queues that are Double-Ended and bounded
    /// </summary>
    public interface IBoundedDequeBasedMessageQueueSemantics : DequeBasedMessageQueueSemantics,
        UnboundedMessageQueueSemantics //TODO: make this Bounded once we have BoundedMessageQueues
    {
    }
}