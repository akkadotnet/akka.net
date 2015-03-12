using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Mailbox with support for EnqueueFirst
    /// </summary>
    public class BoundedDequeBasedMailbox : Mailbox<BoundedMessageQueue, BoundedDequeMessageQueue>, IDequeBasedMailbox
    {
        protected override BoundedMessageQueue CreateSystemMessagesQueue()
        {
            return new BoundedMessageQueue();
        }

        protected override BoundedDequeMessageQueue CreateUserMessagesQueue()
        {
            return new BoundedDequeMessageQueue();
        }

        public void EnqueueFirst(Actor.Envelope envelope)
        {
            UserMessages.EnqueueFirst(envelope);
        }
    }
}