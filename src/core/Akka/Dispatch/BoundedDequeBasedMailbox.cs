//-----------------------------------------------------------------------
// <copyright file="BoundedDequeBasedMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Mailbox with support for EnqueueFirst
    /// </summary>
    public class BoundedDequeBasedMailbox : Mailbox<BoundedMessageQueue, BoundedDequeMessageQueue>, IDequeBasedMailbox
    {
        protected override BoundedMessageQueue CreateSystemMessagesQueue()
        {
            return new BoundedMessageQueue();
        }

        protected override BoundedDequeMessageQueue CreateUserMessagesQueue()
        {
            return new BoundedDequeMessageQueue();
        }

        public void EnqueueFirst(Actor.Envelope envelope)
        {
            UserMessages.EnqueueFirst(envelope);
        }
    }
}

