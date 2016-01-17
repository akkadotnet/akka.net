//-----------------------------------------------------------------------
// <copyright file="UnboundedDequeMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Dispatch.MessageQueues
{
    public class UnboundedDequeMessageQueue : DequeWrapperMessageQueue, IUnboundedDequeBasedMessageQueueSemantics
    {
        public UnboundedDequeMessageQueue() : base(new UnboundedMessageQueue())
        {
        }
    }
    public class BoundedDequeMessageQueue : DequeWrapperMessageQueue, IBoundedDequeBasedMessageQueueSemantics
    {
        public BoundedDequeMessageQueue()
            : base(new BoundedMessageQueue())
        {
        }

        public TimeSpan PushTimeOut { get; set; }
    }
}

