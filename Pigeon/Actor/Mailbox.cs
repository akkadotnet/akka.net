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
        public Action<Envelope> OnNext { get; set; }
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
        private volatile bool Stopped = false;
        private int status;

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }

        private void Run(object _)
        {
            if (Stopped)
            {
                return;
            }

            hasUnscheduledMessages = false;
            Envelope envelope;
            while (systemMessages.TryDequeue(out envelope))
            {           
                this.OnNext(envelope);           
            }
            int throughput = 100;
            int left = throughput;
            while (userMessages.TryDequeue(out envelope))
            {
                this.OnNext(envelope);
                if (systemMessages.TryDequeue(out envelope))
                {
                    this.OnNext(envelope);
                    break;
                }
                left--;
                if (Stopped)
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
        public ConcurrentQueueMailbox()
        {                        
        }

        private void Schedule()
        {
            //only schedule if we idle
            if (Interlocked.Exchange(ref status, MailboxStatus.Busy) == MailboxStatus.Idle)
            {
                //NaiveThreadPool.Schedule(Run);
                ThreadPool.UnsafeQueueUserWorkItem(Run,null);
            }
        }

        public override void Post(Envelope envelope)
        {
            if (Stopped)
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
            Stopped = true;
        }

        public override void Dispose()
        {
            Stopped = true;
        }
    }
}
