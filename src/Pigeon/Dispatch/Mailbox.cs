using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    public abstract class Mailbox : IDisposable
    {
        protected MessageDispatcher dispatcher;
        public Action<Envelope> SystemInvoke { get; set; }
        public Action<Envelope> Invoke { get; set; }
        public abstract void Dispose();
        public abstract void Post(Envelope envelope);

        public abstract void Stop();

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
        private readonly ConcurrentQueue<Envelope> systemMessages = new ConcurrentQueue<Envelope>();
        private readonly ConcurrentQueue<Envelope> userMessages = new ConcurrentQueue<Envelope>();
        private Stopwatch deadLineTimer;

        private volatile bool hasUnscheduledMessages;
        private volatile bool isClosed;
        private int status;

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
                SystemInvoke(envelope);
            }

            int left = dispatcher.Throughput;

            while (userMessages.TryDequeue(out envelope))
            {
                Invoke(envelope);
                if (systemMessages.TryDequeue(out envelope))
                {
                    SystemInvoke(envelope);
                    break;
                }
                left--;
                if (isClosed)
                    return;

                if (left == 0 && userMessages.TryPeek(out envelope) ||
                    (dispatcher.ThroughputDeadlineTime.HasValue &&
                     deadLineTimer.ElapsedTicks > dispatcher.ThroughputDeadlineTime.Value))
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
                dispatcher.Schedule(Run);
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

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }
    }

    /// <summary>
    ///     Special mailbox that processes the last message first
    ///     Useful for some real time edge cases
    /// </summary>
    public class ConcurrentStackMailbox : Mailbox
    {
        private readonly ConcurrentQueue<Envelope> systemMessages = new ConcurrentQueue<Envelope>();
        private readonly ConcurrentStack<Envelope> userMessages = new ConcurrentStack<Envelope>();
        private Stopwatch deadLineTimer;

        private volatile bool hasUnscheduledMessages;
        private volatile bool isClosed;
        private int status;

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
                SystemInvoke(envelope);
            }

            int left = dispatcher.Throughput;

            while (userMessages.TryPop(out envelope))
            {
                Invoke(envelope);
                if (systemMessages.TryDequeue(out envelope))
                {
                    SystemInvoke(envelope);
                    break;
                }
                left--;
                if (isClosed)
                    return;

                if (left == 0 && userMessages.TryPeek(out envelope) ||
                    (dispatcher.ThroughputDeadlineTime.HasValue &&
                     deadLineTimer.ElapsedTicks > dispatcher.ThroughputDeadlineTime.Value))
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
                dispatcher.Schedule(Run);
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

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }
    }
}