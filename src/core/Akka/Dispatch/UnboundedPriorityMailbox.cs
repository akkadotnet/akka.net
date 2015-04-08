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
