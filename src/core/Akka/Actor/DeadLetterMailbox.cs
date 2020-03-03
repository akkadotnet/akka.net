//-----------------------------------------------------------------------
// <copyright file="DeadLetterMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="deadLetters">TBD</param>
        public DeadLetterMessageQueue(IActorRef deadLetters)
        {
            _deadLetters = deadLetters;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasMessages => false;
        /// <summary>
        /// TBD
        /// </summary>
        public int Count => 0;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receiver">TBD</param>
        /// <param name="envelope">TBD</param>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            if (envelope.Message is DeadLetter)
            {
                // actor subscribing to DeadLetter. Drop it.
                return;
            }

            _deadLetters.Tell(new DeadLetter(envelope.Message, envelope.Sender, receiver), envelope.Sender);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        /// <returns>TBD</returns>
        public bool TryDequeue(out Envelope envelope)
        {
            envelope = new Envelope(new NoMessage(), ActorRefs.NoSender);
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="owner">TBD</param>
        /// <param name="deadletters">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="deadLetters">TBD</param>
        public DeadLetterMailbox(IActorRef deadLetters) : base(new DeadLetterMessageQueue(deadLetters))
        {
            _deadLetters = deadLetters;
            BecomeClosed(); // always closed
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal override bool HasSystemMessages => false;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="newContents">TBD</param>
        /// <returns>TBD</returns>
        internal override EarliestFirstSystemMessageList SystemDrain(LatestFirstSystemMessageList newContents)
        {
            return SystemMessageList.ENil;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        internal override void SystemEnqueue(IActorRef receiver, SystemMessage message)
        {
            _deadLetters.Tell(new DeadLetter(message, receiver, receiver));
        }
    }
}

