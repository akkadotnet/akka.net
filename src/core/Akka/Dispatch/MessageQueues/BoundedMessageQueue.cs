using System;
using System.Collections.Concurrent;
using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>An Bounded mailbox message queue.</summary>
    public class BoundedMessageQueue : MessageQueue, BoundedMessageQueueSemantics
    {
        //TODO: Implement BoundedMessageQueue. Currently it's just a copy of UnboundedMessageQueue
        private readonly ConcurrentQueue<Envelope> _queue = new ConcurrentQueue<Envelope>();

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

        public TimeSpan PushTimeOut { get; set; }
    }
}