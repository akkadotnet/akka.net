using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Mailbox with support for EnqueueFirst
    /// </summary>
    public class UnboundedDequeBasedMailbox : Mailbox<UnboundedMessageQueue, UnboundedDequeMessageQueue>, DequeBasedMailbox
    {
        protected override UnboundedMessageQueue CreateSystemMessagesQueue()
        {
            return new UnboundedMessageQueue();
        }

        protected override UnboundedDequeMessageQueue CreateUserMessagesQueue()
        {
            return new UnboundedDequeMessageQueue();
        }

        public void EnqueueFirst(Actor.Envelope envelope)
        {
            UserMessages.EnqueueFirst(envelope);
        }
    }
}
