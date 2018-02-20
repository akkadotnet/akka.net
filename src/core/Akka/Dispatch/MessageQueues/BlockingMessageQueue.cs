//-----------------------------------------------------------------------
// <copyright file="BlockingMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly object _lock = new object();
        private TimeSpan _blockTimeOut = TimeSpan.FromSeconds(1);
        /// <summary>
        /// TBD
        /// </summary>
        protected abstract int LockedCount { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan BlockTimeOut
        {
            get { return _blockTimeOut; }
            set { _blockTimeOut = value; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasMessages
        {
            get { return Count > 0; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Count
        {
            get
            {
                Monitor.TryEnter(_lock, BlockTimeOut);
                try
                {
                    return LockedCount;
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receiver">TBD</param>
        /// <param name="envelope">TBD</param>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            Monitor.TryEnter(_lock, BlockTimeOut);
            try
            {
                LockedEnqueue(envelope);
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        /// <returns>TBD</returns>
        public bool TryDequeue(out Envelope envelope)
        {
            Monitor.TryEnter(_lock, BlockTimeOut);
            try
            {
                return LockedTryDequeue(out envelope);
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="owner">TBD</param>
        /// <param name="deadletters">TBD</param>
        /// <returns>TBD</returns>
        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            Envelope msg;
            while (TryDequeue(out msg)) // lock gets acquired inside the TryDequeue method
            {
                deadletters.Enqueue(owner, msg);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        protected abstract void LockedEnqueue(Envelope envelope);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        /// <returns>TBD</returns>
        protected abstract bool LockedTryDequeue(out Envelope envelope);
    }
}

