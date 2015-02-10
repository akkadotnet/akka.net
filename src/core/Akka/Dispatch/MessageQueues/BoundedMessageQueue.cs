using System;
using System.Collections.Concurrent;
using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>An Bounded mailbox message queue.</summary>
    public class BoundedMessageQueue : MessageQueue, BoundedMessageQueueSemantics
    {
        private readonly BlockingCollection<Envelope> _queue;

        public BoundedMessageQueue()
        {
            _queue = new BlockingCollection<Envelope>();
        }

        public BoundedMessageQueue(int boundedCapacity)
        {
            _queue = new BlockingCollection<Envelope>(boundedCapacity);
        }

        public void Enqueue(Envelope envelope)
        {
            _queue.Add(envelope);
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
            return _queue.TryTake(out envelope);
        }

        public TimeSpan PushTimeOut { get; set; }
    }
}