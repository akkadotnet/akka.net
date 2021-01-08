//-----------------------------------------------------------------------
// <copyright file="UnboundedMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using TQueue = System.Collections.Concurrent.ConcurrentQueue<Akka.Actor.Envelope>;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> An unbounded mailbox message queue. </summary>
    public class UnboundedMessageQueue : IMessageQueue, IUnboundedMessageQueueSemantics
    {
        private readonly TQueue _queue = new TQueue();

        /// <inheritdoc cref="IMessageQueue"/>
        public bool HasMessages
        {
            get { return !_queue.IsEmpty; }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public int Count
        {
            get { return _queue.Count; }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            _queue.Enqueue(envelope);
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryDequeue(out envelope);
        }

        /// <inheritdoc cref="IMessageQueue"/>
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
