//-----------------------------------------------------------------------
// <copyright file="BoundedMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary>An Bounded mailbox message queue.</summary>
    public class BoundedMessageQueue : IMessageQueue, IBoundedMessageQueueSemantics
    {
        private readonly BlockingCollection<Envelope> _queue;

        // TODO: do we need to check for null or empty config here?
        /// <summary>
        /// Creates a new bounded message queue.
        /// </summary>
        /// <param name="config">The configuration for this mailbox.</param>
        public BoundedMessageQueue(Config config)
            : this(config.GetInt("mailbox-capacity", 0), config.GetTimeSpan("mailbox-push-timeout-time", null))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundedMessageQueue"/> class.
        /// </summary>
        /// <param name="boundedCapacity">TBD</param>
        /// <param name="pushTimeOut">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="boundedCapacity"/> is negative.
        /// </exception>
        public BoundedMessageQueue(int boundedCapacity, TimeSpan pushTimeOut)
        {
            PushTimeOut = pushTimeOut;
            if (boundedCapacity < 0)
            {
                throw new ArgumentException("The capacity for BoundedMessageQueue can not be negative", nameof(boundedCapacity));
            }
            else if (boundedCapacity == 0)
            {
                _queue = new BlockingCollection<Envelope>();
            }
            else
            {
                _queue = new BlockingCollection<Envelope>(boundedCapacity);
            }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public bool HasMessages => _queue.Count > 0;

        /// <inheritdoc cref="IMessageQueue"/>
        public int Count => _queue.Count;

        /// <inheritdoc cref="IMessageQueue"/>
        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            if (!_queue.TryAdd(envelope, PushTimeOut)) // dump messages that can't be delivered in-time into DeadLetters
            {
                receiver.AsInstanceOf<IInternalActorRef>().Provider.DeadLetters.Tell(new DeadLetter(envelope.Message, envelope.Sender, receiver), envelope.Sender);
            }
        }

        /// <inheritdoc cref="IMessageQueue"/>
        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryTake(out envelope);
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
        /// The push timeout for this bounded queue.
        /// </summary>
        public TimeSpan PushTimeOut { get; set; }
    }
}

