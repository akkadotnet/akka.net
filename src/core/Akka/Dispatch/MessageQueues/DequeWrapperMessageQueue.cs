//-----------------------------------------------------------------------
// <copyright file="DequeWrapperMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        // doesn't need to be threadsafe - only called from within actor
        private readonly Stack<Envelope> _prependBuffer = new Stack<Envelope>();

        /// <summary>
        /// The underlying <see cref="IMessageQueue"/>.
        /// </summary>
        protected readonly IMessageQueue MessageQueue;

        /// <summary>
        /// Takes another <see cref="IMessageQueue"/> as an argument - wraps <paramref name="messageQueue"/>
        /// in order to provide it with prepend (<see cref="EnqueueFirst"/>) semantics.
        /// </summary>
        /// <param name="messageQueue">The underlying message queue wrapped by this one.</param>
        public DequeWrapperMessageQueue(IMessageQueue messageQueue)
        {
            MessageQueue = messageQueue;
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
            get { return MessageQueue.Count + _prependBuffer.Count; }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            MessageQueue.Enqueue(receiver, envelope);
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

            return MessageQueue.TryDequeue(out envelope);
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

