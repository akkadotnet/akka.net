//-----------------------------------------------------------------------
// <copyright file="UnboundedPriorityMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    public abstract class UnboundedPriorityMailbox : Mailbox<UnboundedMessageQueue,UnboundedPriorityMessageQueue>
    {
        protected override UnboundedMessageQueue CreateSystemMessagesQueue()
        {
            return new UnboundedMessageQueue();
        }

        protected override UnboundedPriorityMessageQueue CreateUserMessagesQueue()
        {
            return new UnboundedPriorityMessageQueue(PriorityGenerator);
        }

        protected abstract int PriorityGenerator(object message);
    }
}

