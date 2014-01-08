using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class Mailbox
    {
        public Action<Envelope> OnNext { get; set; }
        public abstract void Post(Envelope message);
    }

    /// <summary>
    /// For explorative reasons only
    /// </summary>
    public class ConcurrentQueueMailbox : Mailbox
    {
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> userMessages = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> systemMessages = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();
        
        private WaitCallback handler = null;
        private volatile bool hasUnscheduledMessages = false;
        private int status;

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }

        private void Run(object _)
        {
            Envelope envelope = null;
            while (systemMessages.TryDequeue(out envelope))
            {           
                this.OnNext(envelope);           
            }

            while (userMessages.TryDequeue(out envelope))
            {
                this.OnNext(envelope);
                if (systemMessages.TryDequeue(out envelope))
                {
                    this.OnNext(envelope);
                    break;
                }
            }

            Interlocked.Exchange(ref status, MailboxStatus.Idle);

            if (hasUnscheduledMessages)
            {
                hasUnscheduledMessages = false;
                Schedule();
            }
        }
        public ConcurrentQueueMailbox()
        {                        
        }

        private void Schedule()
        {
            //only schedule if we idle
            if (Interlocked.Exchange(ref status, MailboxStatus.Busy) == MailboxStatus.Idle)
            {
                ThreadPool.QueueUserWorkItem(Run);
            }
        }

        public override void Post(Envelope envelope)
        {
            hasUnscheduledMessages = true;
            if (envelope.Payload is SystemMessage)
            {
                systemMessages.Enqueue(envelope);
            }
            else
            {
                userMessages.Enqueue(envelope);
            }

            Schedule();
        }
    }
}
