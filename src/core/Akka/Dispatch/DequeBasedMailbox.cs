﻿//-----------------------------------------------------------------------
// <copyright file="DequeBasedMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used for <see cref="IMessageQueue"/> instances that support double-ended queues.
    /// </summary>
    public interface IDequeBasedMailbox
    {
        /// <summary>
        /// Enqueues an <see cref="Envelope"/> to the front of
        /// the <see cref="IMessageQueue"/>. Typically called during
        /// a <see cref="IStash.Unstash"/> or <see cref="IStash.UnstashAll()"/>operation.
        /// </summary>
        /// <param name="envelope">The message that will be prepended to the queue.</param>
        void EnqueueFirst(Envelope envelope);

        /// <summary>
        /// Posts a message to the back of the <see cref="IMessageQueue"/>
        /// </summary>
        /// <param name="receiver">The intended recipient of the message.</param>
        /// <param name="envelope">The message that will be appended to the queue.</param>
        void Post(IActorRef receiver, Envelope envelope);
    }
}

