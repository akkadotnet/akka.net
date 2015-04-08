//-----------------------------------------------------------------------
// <copyright file="UnboundedMailboxQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
#if MONO
using TQueue = Akka.Util.MonoConcurrentQueue<Akka.Actor.Envelope>;
#else
using TQueue = System.Collections.Concurrent.ConcurrentQueue<Akka.Actor.Envelope>;

#endif

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> An unbounded mailbox message queue. </summary>
    public class UnboundedMessageQueue : IMessageQueue, IUnboundedMessageQueueSemantics
    {
        private readonly TQueue _queue = new TQueue();

        public void Enqueue(Envelope envelope)
        {
            _queue.Enqueue(envelope);
        }

        public bool HasMessages
        {
            get { return _queue.Count > 0; }
        }

        public int Count
        {
            get { return _queue.Count; }
        }

        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryDequeue(out envelope);
        }
    }
}

