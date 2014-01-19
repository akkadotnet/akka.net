using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public abstract class Mailbox : IDisposable
    {
        public Action<Envelope> SystemInvoke { get; set; }
        public Action<Envelope> Invoke { get; set; }
        public abstract void Post(Envelope message);

        public abstract void Stop();

        public abstract void Dispose();
    }

    /// <summary>
    /// For explorative reasons only
    /// </summary>
    public class ConcurrentQueueMailbox : Mailbox
    {
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> userMessages = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> systemMessages = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();
        
    //    private WaitCallback handler = null;
        private volatile bool hasUnscheduledMessages = false;
        private volatile bool isClosed = false;
        private int status;
        private MessageDispatcher dispatcher;        

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }

        private void Run(object _)
        {
            if (isClosed)
            {
                return;
            }

            hasUnscheduledMessages = false;
            Envelope envelope;
            while (systemMessages.TryDequeue(out envelope))
            {           
                this.SystemInvoke(envelope);           
            }

            int left = dispatcher.Throughput;
            while (userMessages.TryDequeue(out envelope))
            {
                this.Invoke(envelope);
                if (systemMessages.TryDequeue(out envelope))
                {
                    this.SystemInvoke(envelope);
                    break;
                }
                left--;
                if (isClosed)
                    return;

                if (left == 0 && userMessages.TryPeek(out envelope)) 
                {
                    // we have processed throughput messages, and there are still envelopes left
                    hasUnscheduledMessages = true;
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

        public ConcurrentQueueMailbox(MessageDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        private void Schedule()
        {
            //only schedule if we idle
            if (Interlocked.Exchange(ref status, MailboxStatus.Busy) == MailboxStatus.Idle)
            {
                this.dispatcher.Schedule(Run);
            }
        }

        public override void Post(Envelope envelope)
        {
            if (isClosed)
                return;

            hasUnscheduledMessages = true;
            if (envelope.Message is SystemMessage)
            {
                systemMessages.Enqueue(envelope);
            }
            else
            {
                userMessages.Enqueue(envelope);
            }

            Schedule();
        }

        public override void Stop()
        {
            isClosed = true;
        }

        public override void Dispose()
        {
            isClosed = true;
        }
    }
}
