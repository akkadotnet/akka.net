//-----------------------------------------------------------------------
// <copyright file="UnboundedDequeBasedMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Mailbox with support for EnqueueFirst. Used primarily for <see cref="IStash"/> support.
    /// </summary>
    public class UnboundedDequeBasedMailbox : Mailbox<UnboundedMessageQueue, UnboundedDequeMessageQueue>, IDequeBasedMailbox
    {
        /// <summary>
        /// Intended for system messages, creates an <see cref="UnboundedMessageQueue"/> to be used 
        /// inside the <see cref="Mailbox"/>.
        /// </summary>
        protected override UnboundedMessageQueue CreateSystemMessagesQueue()
        {
            return new UnboundedMessageQueue();
        }

        /// <summary>
        /// Used for user-defined messages within a <see cref="Mailbox"/>; creates a new <see cref="UnboundedDequeMessageQueue"/>
        /// which means that any actor with a <see cref="IStash"/> can unstash messages to the front of the queue.
        /// </summary>
        /// <returns></returns>
        protected override UnboundedDequeMessageQueue CreateUserMessagesQueue()
        {
            return new UnboundedDequeMessageQueue();
        }

        public void EnqueueFirst(Actor.Envelope envelope)
        {
            UserMessages.EnqueueFirst(envelope);
        }
    }
}

