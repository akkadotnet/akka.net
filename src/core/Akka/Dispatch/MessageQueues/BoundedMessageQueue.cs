using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>An Bounded mailbox message queue.</summary>
    public class BoundedMessageQueue : MessageQueue, IBoundedMessageQueueSemantics
    {
        private readonly BlockingCollection<Envelope> _queue;

        public BoundedMessageQueue()
        {
            _queue = new BlockingCollection<Envelope>();
        }

        public BoundedMessageQueue(Settings settings, Config config) 
            : this(config.GetInt("mailbox-capacity"), config.GetTimeSpan("mailbox-push-timeout-time"))
        {
        }

        public BoundedMessageQueue(int boundedCapacity, TimeSpan pushTimeOut)
        {
            if (boundedCapacity < 0)
            {
                throw new ArgumentException("The capacity for BoundedMessageQueue can not be negative");
            }
            else if (boundedCapacity == 0)
            {
                _queue = new BlockingCollection<Envelope>();
            }
            else
            {
                _queue = new BlockingCollection<Envelope>(boundedCapacity);
            }
        }

        public void Enqueue(Envelope envelope)
        {
            if (PushTimeOut.Milliseconds >= 0)
            {
                _queue.TryAdd(envelope, PushTimeOut);
            }
            else
            {
                _queue.Add(envelope);
            }
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