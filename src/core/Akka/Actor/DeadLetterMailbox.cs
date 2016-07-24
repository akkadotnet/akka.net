//-----------------------------------------------------------------------
// <copyright file="DeadLetterMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

        public DeadLetterMessageQueue(IActorRef deadLetters)
        {
            _deadLetters = deadLetters;
        }

        public bool HasMessages => false;
        public int Count => 0;
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            if (envelope.Message is DeadLetter)
            {
                // actor subscribing to DeadLetter. Drop it.
                return;
            }

            _deadLetters.Tell(new DeadLetter(envelope.Message, envelope.Sender, receiver), envelope.Sender);
        }

        public bool TryDequeue(out Envelope envelope)
        {
            envelope = new Envelope(new NoMessage(), ActorRefs.NoSender);
            return false;
        }

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
    public sealed class DeadLetterMailbox : Mailbox
    {
        private readonly IActorRef _deadLetters;

        public DeadLetterMailbox(IActorRef deadLetters) : base(new DeadLetterMessageQueue(deadLetters))
        {
            _deadLetters = deadLetters;
            BecomeClosed(); // always closed
        }

        internal override bool HasSystemMessages => false;
        internal override EarliestFirstSystemMessageList SystemDrain(LatestFirstSystemMessageList newContents)
        {
            return SystemMessageList.ENil;
        }

        internal override void SystemEnqueue(IActorRef receiver, SystemMessage message)
        {
            _deadLetters.Tell(new DeadLetter(message, receiver, receiver));
        }
    }
}

