//-----------------------------------------------------------------------
// <copyright file="BoundedMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public BoundedMessageQueue(Config config)
            : this(config.GetInt("mailbox-capacity"), config.GetTimeSpan("mailbox-push-timeout-time"))
        {
        }

        public BoundedMessageQueue(int boundedCapacity, TimeSpan pushTimeOut)
        {
            PushTimeOut = pushTimeOut;
            if (boundedCapacity < 0)
            {
                throw new ArgumentException("The capacity for BoundedMessageQueue can not be negative");
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

        public bool HasMessages => _queue.Count > 0;

        public int Count => _queue.Count;

        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            if (!_queue.TryAdd(envelope, PushTimeOut)) // dump messages that can't be delivered in-time into DeadLetters
            {
                receiver.AsInstanceOf<IInternalActorRef>().Provider.DeadLetters.Tell(new DeadLetter(envelope.Message, envelope.Sender, receiver), envelope.Sender);
            }
        }

        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryTake(out envelope);
        }

        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
            Envelope msg;
            while (TryDequeue(out msg))
            {
                deadletters.Enqueue(owner, msg);
            }
        }

        public TimeSpan PushTimeOut { get; set; }
    }
}

