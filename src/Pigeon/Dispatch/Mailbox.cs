using Pigeon.Actor;
using Pigeon.Dispatch;
using Pigeon.Dispatch.SysMsg;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Dispatch
{
    public abstract class Mailbox : IDisposable
    {
        public Action<Envelope> SystemInvoke { get; set; }
        public Action<Envelope> Invoke { get; set; }
        public abstract void Post(Envelope envelope);

        public abstract void Stop();

        public abstract void Dispose();
        protected MessageDispatcher dispatcher;
        public void Setup(MessageDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }
    }

    public class DaemonMailbox : Mailbox
    {

        public override void Post(Envelope envelope)
        {
            if (envelope.Message is SystemMessage)
                SystemInvoke(envelope);
            else
                Invoke(envelope);
        }

        public override void Stop()
        {
            
        }

        public override void Dispose()
        {
            
        }
    }

    public class ConcurrentQueueMailbox : Mailbox
    {
        private ConcurrentQueue<Envelope> userMessages = new ConcurrentQueue<Envelope>();
        private ConcurrentQueue<Envelope> systemMessages = new ConcurrentQueue<Envelope>();
            
        private volatile bool hasUnscheduledMessages = false;
        private volatile bool isClosed = false;
        private int status;
        
        private Stopwatch deadLineTimer = null;

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }

        public ConcurrentQueueMailbox()
        {            
        }

        private void Run(object _)
        {
            if (isClosed)
            {
                return;
            }

            if (dispatcher.ThroughputDeadlineTime.HasValue)
            {
                if (deadLineTimer != null)
                {
                    deadLineTimer.Restart();
                }
                else
                {
                    deadLineTimer = Stopwatch.StartNew();
                }
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

                if (left == 0 && userMessages.TryPeek(out envelope) || (dispatcher.ThroughputDeadlineTime.HasValue && deadLineTimer.ElapsedTicks > dispatcher.ThroughputDeadlineTime.Value)) 
                {
                    if (dispatcher.ThroughputDeadlineTime.HasValue)
                    {
                        deadLineTimer.Stop();
                    }
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

    /// <summary>
    /// Special mailbox that processes the last message first
    /// Useful for some real time edge cases
    /// </summary>
    public class ConcurrentStackMailbox : Mailbox
    {
        private ConcurrentStack<Envelope> userMessages = new ConcurrentStack<Envelope>();
        private ConcurrentQueue<Envelope> systemMessages = new ConcurrentQueue<Envelope>();

        private volatile bool hasUnscheduledMessages = false;
        private volatile bool isClosed = false;
        private int status;

        private Stopwatch deadLineTimer = null;

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }

        public ConcurrentStackMailbox()
        {
        }

        private void Run(object _)
        {
            if (isClosed)
            {
                return;
            }

            if (dispatcher.ThroughputDeadlineTime.HasValue)
            {
                if (deadLineTimer != null)
                {
                    deadLineTimer.Restart();
                }
                else
                {
                    deadLineTimer = Stopwatch.StartNew();
                }
            }

            hasUnscheduledMessages = false;
            Envelope envelope;
            while (systemMessages.TryDequeue(out envelope))
            {
                this.SystemInvoke(envelope);
            }

            int left = dispatcher.Throughput;

            while (userMessages.TryPop(out envelope))
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

                if (left == 0 && userMessages.TryPeek(out envelope) || (dispatcher.ThroughputDeadlineTime.HasValue && deadLineTimer.ElapsedTicks > dispatcher.ThroughputDeadlineTime.Value))
                {
                    if (dispatcher.ThroughputDeadlineTime.HasValue)
                    {
                        deadLineTimer.Stop();
                    }
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
                userMessages.Push(envelope);
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
