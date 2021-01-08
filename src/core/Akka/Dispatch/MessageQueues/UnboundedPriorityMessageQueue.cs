//-----------------------------------------------------------------------
// <copyright file="UnboundedPriorityMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Util;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> 
    /// Base class for a message queue that uses a priority generator for messages 
    /// </summary>
    public class UnboundedPriorityMessageQueue : BlockingMessageQueue, IUnboundedDequeBasedMessageQueueSemantics
    {
        private readonly ListPriorityQueue _prioQueue;
        // doesn't need to be threadsafe - only called from within actor
        private readonly Stack<Envelope> _prependBuffer = new Stack<Envelope>();

        /// <summary>
        /// DEPRECATED. Use <see cref="UnboundedPriorityMessageQueue(Func{object,int}, int)"/> instead.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the priority queue.</param>
        [Obsolete("Use UnboundedPriorityMessageQueue(Func<object, int> priorityGenerator, int initialCapacity) instead. [1.1.3]")]
        public UnboundedPriorityMessageQueue(int initialCapacity) : this(ListPriorityQueue.DefaultPriorityCalculator, initialCapacity)
        {

        }

        /// <summary>
        /// Creates a new unbounded priority message queue.
        /// </summary>
        /// <param name="priorityGenerator">The calculator function for determining the priority of inbound messages.</param>
        /// <param name="initialCapacity">The initial capacity of the queue.</param>
        public UnboundedPriorityMessageQueue(Func<object, int> priorityGenerator, int initialCapacity)
        {
            _prioQueue = new ListPriorityQueue(initialCapacity, priorityGenerator);
        }

        /// <summary>
        /// Unsafe method for computing the underlying message count. 
        /// </summary>
        /// <remarks>
        /// Called from within a synchronization mechanism.
        /// </remarks>
        protected override int LockedCount
        {
            get { return _prioQueue.Count(); }
        }

        /// <summary>
        /// Unsafe method for enqueuing a new message to the queue.
        /// </summary>
        /// <param name="envelope">The message to enqueue.</param>
        /// <remarks>
        /// Called from within a synchronization mechanism.
        /// </remarks>
        protected override void LockedEnqueue(Envelope envelope)
        {
            _prioQueue.Enqueue(envelope);
        }

        /// <summary>
        /// Unsafe method for attempting to dequeue a message.
        /// </summary>
        /// <param name="envelope">The message that might be dequeued.</param>
        /// <returns><c>true</c> if a message was available to be dequeued, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// Called from within a synchronization mechanism.
        /// </remarks>
        protected override bool LockedTryDequeue(out Envelope envelope)
        {
            if (_prependBuffer.Count > 0)
            {
                envelope = _prependBuffer.Pop();
                return true;
            }

            if (_prioQueue.Count() > 0)
            {
                envelope = _prioQueue.Dequeue();
                return true;
            }
            envelope = default (Envelope);
            return false;
        }

        public void EnqueueFirst(Envelope envelope)
        {
            _prependBuffer.Push(envelope);
        }
    }
}
