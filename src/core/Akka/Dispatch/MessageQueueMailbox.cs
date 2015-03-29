using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    public abstract class MessageQueueMailbox : Mailbox
    {
        public abstract MessageQueue MessageQueue { get; }
    }
}
