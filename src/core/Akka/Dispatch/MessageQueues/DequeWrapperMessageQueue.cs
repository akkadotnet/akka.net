//-----------------------------------------------------------------------
// <copyright file="DequeWrapperMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>
    /// Message queue for supporting <see cref="IDequeBasedMessageQueueSemantics"/> within <see cref="Mailbox"/> instances.
    /// 
    /// Uses a <see cref="Stack{Envelope}"/> internally - each individual <see cref="EnqueueFirst"/>
    /// </summary>
    public class DequeWrapperMessageQueue : IMessageQueue, IDequeBasedMessageQueueSemantics
    {
        private readonly Stack<Envelope> _prependBuffer = new Stack<Envelope>();
        private readonly IMessageQueue _messageQueue;
        /// <summary>
        /// Takes another <see cref="IMessageQueue"/> as an argument - wraps <paramref name="messageQueue"/>
        /// in order to provide it with prepend (<see cref="EnqueueFirst"/>) semantics.
        /// </summary>
        /// <param name="messageQueue"></param>
        public DequeWrapperMessageQueue(IMessageQueue messageQueue)
        {
            _messageQueue = messageQueue;
        }

        /// <summary>
        /// Returns true if there are any messages inside the queue.
        /// </summary>
        public bool HasMessages
        {
            get { return Count > 0; }
        }

        /// <summary>
        /// Returns the number of messages in both the internal message queue
        /// and the prepend buffer.
        /// </summary>
        public int Count
        {
            get { return _messageQueue.Count + _prependBuffer.Count; }
        }

        /// <summary>
        /// Enqueue a message to the back of the <see cref="IMessageQueue"/>
        /// </summary>
        /// <param name="envelope"></param>
        public void Enqueue(Envelope envelope)
        {
            _messageQueue.Enqueue(envelope);
        }

        /// <summary>
        /// Attempt to dequeue a message from the front of the prepend buffer.
        /// 
        /// If the prepend buffer is empty, dequeue a message from the normal
        /// <see cref="IMessageQueue"/> wrapped but this wrapper.
        /// </summary>
        /// <param name="envelope">The message to return, if any</param>
        /// <returns><c>true</c> if a message was available, <c>false</c> otherwise.</returns>
        public bool TryDequeue(out Envelope envelope)
        {
            if (_prependBuffer.Count > 0)
            {
                envelope = _prependBuffer.Pop();
                return true;
            }

            return _messageQueue.TryDequeue(out envelope);
        }

        /// <summary>
        /// Add a message to the front of the queue via the prepend buffer.
        /// </summary>
        /// <param name="envelope">The message we wish to append to the front of the queue.</param>
        public void EnqueueFirst(Envelope envelope)
        {
            _prependBuffer.Push(envelope);
        }
    }
}

