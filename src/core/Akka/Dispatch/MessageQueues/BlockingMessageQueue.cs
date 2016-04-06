//-----------------------------------------------------------------------
// <copyright file="BlockingMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        protected abstract int LockedCount { get; }

        public TimeSpan BlockTimeOut
        {
            get { return _blockTimeOut; }
            set { _blockTimeOut = value; }
        }

        public void Enqueue(Envelope envelope)
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

        public bool HasMessages
        {
            get { return Count > 0; }
        }

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

        protected abstract void LockedEnqueue(Envelope envelope);

        protected abstract bool LockedTryDequeue(out Envelope envelope);
    }
}

