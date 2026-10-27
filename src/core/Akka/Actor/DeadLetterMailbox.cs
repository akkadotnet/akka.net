//-----------------------------------------------------------------------
// <copyright file="DeadLetterMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Annotations;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Message queue implementation used to funnel messages to <see cref="DeadLetterActorRef"/>
    /// </summary>
    internal sealed class DeadLetterMessageQueue : IMessageQueue
    {
        private readonly IActorRef _deadLetters;

        /// <summary>
        /// Creates a queue that immediately forwards enqueued messages to the dead-letters reference.
        /// </summary>
        /// <param name="deadLetters">The reference that receives the resulting dead-letter messages.</param>
        public DeadLetterMessageQueue(IActorRef deadLetters)
        {
            _deadLetters = deadLetters;
        }

        /// <summary>
        /// Always <c>false</c>, because this queue forwards messages as they are enqueued.
        /// </summary>
        public bool HasMessages => false;
        /// <summary>
        /// Always zero, because this queue does not retain user messages.
        /// </summary>
        public int Count => 0;
        /// <summary>
        /// Forwards the envelope as a dead letter unless it is already a dead-letter event.
        /// </summary>
        /// <param name="receiver">The actor reference that would receive the message.</param>
        /// <param name="envelope">The message and sender to forward.</param>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            if (envelope.Message is AllDeadLetters)
            {
                /*  We're receiving a DeadLetter sent to us by someone else (which is not normal - usually only happens
                 *  if we were explicitly subscribed to DeadLetters on the EventStream).
                 *   
                 *  Have to terminate here in order to prevent a stack overflow.
                 */ 
                return;
            }

            _deadLetters.Tell(new DeadLetter(envelope.Message, envelope.Sender, receiver), envelope.Sender);
        }

        /// <summary>
        /// This queue never stores messages, so dequeue always fails and returns a sentinel envelope.
        /// </summary>
        /// <param name="envelope">Receives a sentinel envelope because no message is available.</param>
        /// <returns>Always <c>false</c>.</returns>
        public bool TryDequeue(out Envelope envelope)
        {
            envelope = new Envelope(new NoMessage(), ActorRefs.NoSender);
            return false;
        }

        /// <summary>
        /// Does nothing because messages are forwarded immediately and none remain to drain.
        /// </summary>
        /// <param name="owner">The actor that owned this queue.</param>
        /// <param name="deadletters">The queue that would receive remaining messages.</param>
        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            // do nothing
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Mailbox for dead letters.
    /// </summary>
    [InternalApi]
    public sealed class DeadLetterMailbox : Mailbox
    {
        private readonly IActorRef _deadLetters;

        /// <summary>
        /// Creates a mailbox that forwards enqueued messages to the supplied dead-letters reference and remains closed.
        /// </summary>
        /// <param name="deadLetters">The reference that receives user and system messages as dead letters.</param>
        public DeadLetterMailbox(IActorRef deadLetters) : base(new DeadLetterMessageQueue(deadLetters))
        {
            _deadLetters = deadLetters;
            BecomeClosed(); // always closed
        }

        /// <summary>
        /// Always <c>false</c>; this mailbox does not queue system messages.
        /// </summary>
        internal override bool HasSystemMessages => false;
        /// <summary>
        /// Draining this mailbox always returns an empty system-message list.
        /// </summary>
        /// <param name="newContents">The replacement system-message list, which this mailbox does not use.</param>
        /// <returns>The empty system-message list.</returns>
        internal override EarliestFirstSystemMessageList SystemDrain(LatestFirstSystemMessageList newContents)
        {
            return SystemMessageList.ENil;
        }

        /// <summary>
        /// Forwards the system message to the dead-letters reference as a dead-letter event.
        /// </summary>
        /// <param name="receiver">The actor reference that would receive the system message.</param>
        /// <param name="message">The system message to publish as a dead letter.</param>
        internal override void SystemEnqueue(IActorRef receiver, SystemMessage message)
        {
            _deadLetters.Tell(new DeadLetter(message, receiver, receiver));
        }
    }
}
