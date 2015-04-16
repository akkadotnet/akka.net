//-----------------------------------------------------------------------


using System;
using Akka.Actor;
using System.Collections.Concurrent;
// <copyright file="UnboundedMailboxQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    /// <summary> An unbounded mailbox message queue. </summary>
    public class UnboundedMessageQueue : IMessageQueue, IUnboundedMessageQueueSemantics
    {
	    private static readonly bool IsRunningOnMono = Type.GetType("Mono.Runtime") != null;

	    private readonly IProducerConsumerCollection<Envelope> _queue = IsRunningOnMono
			? (IProducerConsumerCollection<Envelope>)new Util.MonoConcurrentQueue<Envelope>()
		    : new ConcurrentQueue<Envelope>();

        public void Enqueue(Envelope envelope)
        {
			_queue.TryAdd(envelope);
        }

        public bool HasMessages
        {
            get { return _queue.Count > 0; }
        }

        public int Count
        {
            get { return _queue.Count; }
        }

        public bool TryDequeue(out Envelope envelope)
        {
            return _queue.TryTake(out envelope);
        }
    }
}

