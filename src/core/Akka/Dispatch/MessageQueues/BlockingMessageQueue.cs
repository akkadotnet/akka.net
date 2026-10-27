//-----------------------------------------------------------------------
// <copyright file="BlockingMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> 
    /// Base class for blocking message queues. Allows non thread safe data structures to be used as message queues. 
    /// </summary>
    public abstract class BlockingMessageQueue : IMessageQueue, IBlockingMessageQueueSemantics
    {
        private readonly object _lock = new();
        private TimeSpan _blockTimeOut = TimeSpan.FromSeconds(1);
        /// <summary>
        /// Gets the number of entries in the underlying queue while its synchronization lock is held.
        /// </summary>
        protected abstract int LockedCount { get; }

        /// <summary>
        /// Gets or sets the lock timeout reported by the blocking-queue semantics contract.
        /// </summary>
        public TimeSpan BlockTimeOut
        {
            get { return _blockTimeOut; }
            set { _blockTimeOut = value; }
        }

        /// <summary>
        /// Gets whether this queue contains at least one message.
        /// </summary>
        public bool HasMessages
        {
            get { return Count > 0; }
        }

        /// <summary>
        /// Gets the number of queued messages while holding the synchronization lock.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return LockedCount;
                }
            }
        }

        /// <summary>
        /// Adds an envelope to the underlying queue while holding the synchronization lock.
        /// </summary>
        /// <param name="receiver">The intended recipient; this base implementation does not use this parameter.</param>
        /// <param name="envelope">The message envelope to enqueue.</param>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            lock (_lock)
            {
                LockedEnqueue(envelope);
            }
        }

        /// <summary>
        /// Attempts to remove an envelope from the underlying queue while holding the synchronization lock.
        /// </summary>
        /// <param name="envelope">When this method returns <c>true</c>, contains the removed envelope; otherwise, the default envelope.</param>
        /// <returns><c>true</c> if an envelope was removed; otherwise, <c>false</c>.</returns>
        public bool TryDequeue(out Envelope envelope)
        {
            lock (_lock)
            {
                return LockedTryDequeue(out envelope);
            }
        }

        /// <summary>
        /// Dequeues messages into the supplied dead-letter queue until a dequeue reports that the queue is empty.
        /// </summary>
        /// <param name="owner">The actor that owns this message queue and is used as the recipient during transfer.</param>
        /// <param name="deadletters">The queue that receives dequeued envelopes. Stop producers first if all queued messages must be transferred, because a concurrent enqueue may occur after an empty dequeue.</param>
        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            while (TryDequeue(out var msg)) // lock gets acquired inside the TryDequeue method
            {
                deadletters.Enqueue(owner, msg);
            }
        }

        /// <summary>
        /// Enqueues an envelope into the subclass's underlying collection. The caller holds the synchronization lock.
        /// </summary>
        /// <param name="envelope">The envelope to enqueue.</param>
        protected abstract void LockedEnqueue(Envelope envelope);

        /// <summary>
        /// Attempts to dequeue an envelope from the subclass's underlying collection. The caller holds the synchronization lock.
        /// </summary>
        /// <param name="envelope">When this method returns <c>true</c>, contains the removed envelope; otherwise, the default envelope.</param>
        /// <returns><c>true</c> if an envelope was available; otherwise, <c>false</c>.</returns>
        protected abstract bool LockedTryDequeue(out Envelope envelope);
    }
}
