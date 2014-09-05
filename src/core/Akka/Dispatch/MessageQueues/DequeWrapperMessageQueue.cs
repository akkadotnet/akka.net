﻿using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    public class DequeWrapperMessageQueue : MessageQueue, DequeBasedMessageQueueSemantics
    {
        private readonly Stack<Envelope> _prependBuffer = new Stack<Envelope>();
        private readonly MessageQueue _messageQueue;
        public DequeWrapperMessageQueue(MessageQueue messageQueue)
        {
            _messageQueue = messageQueue;
        }

        public bool HasMessages
        {
            get { return Count > 0; }
        }

        public int Count
        {
            get { return _messageQueue.Count + _prependBuffer.Count; }
        }

        public void Enqueue(Envelope envelope)
        {
            _messageQueue.Enqueue(envelope);
        }

        public bool TryDequeue(out Envelope envelope)
        {
            if (_prependBuffer.Count > 0)
            {
                envelope = _prependBuffer.Pop();
                return true;
            }

            return _messageQueue.TryDequeue(out envelope);
        }

        public void EnqueueFirst(Envelope envelope)
        {
            _prependBuffer.Push(envelope);
        }
    }
}
