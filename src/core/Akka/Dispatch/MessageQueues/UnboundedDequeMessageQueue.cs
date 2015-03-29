﻿namespace Akka.Dispatch.MessageQueues
{
    public class UnboundedDequeMessageQueue : DequeWrapperMessageQueue, UnboundedDequeBasedMessageQueueSemantics
    {
        public UnboundedDequeMessageQueue() : base(new UnboundedMessageQueue())
        {
        }
    }
    public class BoundedDequeMessageQueue : DequeWrapperMessageQueue, BoundedDequeBasedMessageQueueSemantics
    {
        public BoundedDequeMessageQueue()
            : base(new BoundedMessageQueue())
        {
        }
    }
}
