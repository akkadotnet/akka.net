using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public abstract class Mailbox
    {
        public Action<Envelope> OnNext { get; set; }
        public abstract void Post(Envelope message);

        public abstract void Stop();
    }

    public class DataFlowMailbox : Mailbox
    {
        ActionBlock<Envelope> inner;

        public DataFlowMailbox()
        {
            inner = new ActionBlock<Envelope>((Action<Envelope>)this.OnNextWrapper);
        }
        private void OnNextWrapper(Envelope envelope)
        {
            OnNext(envelope);
        }
        public override void Post(Envelope message)
        {
            inner.Post(message);
        }

        public override void Stop()
        {
            throw new NotImplementedException();
        }
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
                return;

            Envelope envelope;
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

        public override void Stop()
        {
            Stopped = true;
        }
    }
}
