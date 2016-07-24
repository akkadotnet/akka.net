//-----------------------------------------------------------------------
// <copyright file="InternalExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;

namespace Akka.Persistence
{
    internal static class InternalExtensions
    {
        /// <summary>
        /// Enqueues provided <paramref name="message"/> at the beginning of the internal actor cell mailbox message queue.
        /// Requires current actor to use unbounded deque-based message queue. It will fail otherwise.
        /// </summary>
        public static void EnqueueMessageFirst(this IActorContext context, object message)
        {
            var cell = (ActorCell)context;
            var mailbox = cell.Mailbox;
            var queue = (IUnboundedDequeBasedMessageQueueSemantics)mailbox.MessageQueue;
            queue.EnqueueFirst(new Envelope(message, context.Sender));
        }
    }
}

