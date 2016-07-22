//-----------------------------------------------------------------------
// <copyright file="UnboundedMailboxQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public bool HasMessages
        {
#if MONO
            get { return _queue.Count > 0; }
#else
            get { return !_queue.IsEmpty; }
#endif
        }

        public int Count
        {
            get { return _queue.Count; }
        }

        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            _queue.Enqueue(envelope);
        }

        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryDequeue(out envelope);
        }

        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            Envelope msg;
            while (TryDequeue(out msg))
            {
                deadletters.Enqueue(owner, msg);
            }
        }
    }
}