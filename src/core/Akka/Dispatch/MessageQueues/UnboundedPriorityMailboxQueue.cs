//-----------------------------------------------------------------------
// <copyright file="UnboundedPriorityMailboxQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> 
    /// Base class message queue that uses a priority generator for messages 
    /// </summary>
    public class UnboundedPriorityMessageQueue : BlockingMessageQueue
    {
        private readonly ListPriorityQueue _prioQueue;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialCapacity">TBD</param>
        public UnboundedPriorityMessageQueue(int initialCapacity)
        {
            _prioQueue = new ListPriorityQueue(initialCapacity);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="priorityGenerator">TBD</param>
        /// <param name="initialCapacity">TBD</param>
        public UnboundedPriorityMessageQueue(Func<object, int> priorityGenerator, int initialCapacity) : this(initialCapacity)
        {
            _prioQueue.SetPriorityCalculator(priorityGenerator);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="priorityGenerator">TBD</param>
        /// <returns>TBD</returns>
        internal void SetPriorityGenerator(Func<object, int> priorityGenerator)
        {
            _prioQueue.SetPriorityCalculator(priorityGenerator);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override int LockedCount
        {
            get { return _prioQueue.Count(); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        protected override void LockedEnqueue(Envelope envelope)
        {
            _prioQueue.Enqueue(envelope);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        /// <returns>TBD</returns>
        protected override bool LockedTryDequeue(out Envelope envelope)
        {
            if (_prioQueue.Count() > 0)
            {
                envelope = _prioQueue.Dequeue();
                return true;
            }
            envelope = default (Envelope);
            return false;
        }
    }
}

