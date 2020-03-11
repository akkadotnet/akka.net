//-----------------------------------------------------------------------
// <copyright file="LoggerMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;

namespace Akka.Event
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class LoggerMailboxType : MailboxType, IProducesMessageQueue<LoggerMailbox>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <param name="config">TBD</param>
        public LoggerMailboxType(Settings settings, Config config) : base(settings, config)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="owner">TBD</param>
        /// <param name="system">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="owner"/> or <paramref name="system"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            if(owner != null && system != null) return new LoggerMailbox(owner, system);
            throw new ArgumentNullException(nameof(owner), "no mailbox owner or system given");
        }
    }

    /// <summary>
    /// Mailbox type used by loggers
    /// </summary>
    public class LoggerMailbox : Mailbox, ILoggerMessageQueueSemantics, IUnboundedMessageQueueSemantics, IMessageQueue
    {
        private readonly IActorRef _owner;
        private readonly ActorSystem _system;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="owner">TBD</param>
        /// <param name="system">TBD</param>
        public LoggerMailbox(IActorRef owner, ActorSystem system) : base(new UnboundedMessageQueue())
        {
            _owner = owner;
            _system = system;
        }

        /* Explicit IMessageQueue implementation; needed for MessageType support. */

        bool IMessageQueue.HasMessages => MessageQueue.HasMessages;

        int IMessageQueue.Count => MessageQueue.Count;

        void IMessageQueue.Enqueue(IActorRef receiver, Envelope envelope)
        {
            MessageQueue.Enqueue(receiver, envelope);
        }

        bool IMessageQueue.TryDequeue(out Envelope envelope)
        {
            return MessageQueue.TryDequeue(out envelope);
        }

        void IMessageQueue.CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            if (HasMessages)
            {
                Envelope envelope;

                // Drain all remaining messages to the StandardOutLogger.
                // CleanUp is called after switching out the mailbox, which is why
                // this kind of look works without a limit.
                while (TryDequeue(out envelope))
                {
                    // Logging.StandardOutLogger is a MinimalActorRef, i.e. not a "real" actor
                    Logging.StandardOutLogger.Tell(envelope.Message, envelope.Sender);
                }
            }
            MessageQueue.CleanUp(owner, deadletters);
        }
    }
}
