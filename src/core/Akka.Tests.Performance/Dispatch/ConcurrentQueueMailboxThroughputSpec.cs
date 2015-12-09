using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{
    public class ConcurrentQueueMailboxThroughputSpec : MailboxThroughputSpecBase
    {
        protected override Mailbox CreateMailbox()
        {
            return new ConcurrentQueueMailbox();
        }
    }
}