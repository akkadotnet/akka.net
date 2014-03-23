using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Class Mailbox.
    /// </summary>
    public abstract class Mailbox : IDisposable
    {
        /// <summary>
        ///     The dispatcher
        /// </summary>
        protected MessageDispatcher dispatcher;

        /// <summary>
        ///     Gets or sets the system invoke.
        /// </summary>
        /// <value>The system invoke.</value>
        public Action<Envelope> SystemInvoke { get; set; }

        /// <summary>
        ///     Gets or sets the invoke.
        /// </summary>
        /// <value>The invoke.</value>
        public Action<Envelope> Invoke { get; set; }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        ///     Posts the specified envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        public abstract void Post(Envelope envelope);

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public abstract void Stop();

        /// <summary>
        ///     Setups the specified dispatcher.
        /// </summary>
        /// <param name="dispatcher">The dispatcher.</param>
        public void Setup(MessageDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }
    }

    ///// <summary>
    /////     Class DaemonMailbox.
    ///// </summary>
    //public class DaemonMailbox : Mailbox
    //{
    //    /// <summary>
    //    ///     Posts the specified envelope.
    //    /// </summary>
    //    /// <param name="envelope">The envelope.</param>
    //    public override void Post(Envelope envelope)
    //    {
    //        if (envelope.Message is SystemMessage)
    //            SystemInvoke(envelope);
    //        else
    //            Invoke(envelope);
    //    }

    //    /// <summary>
    //    ///     Stops this instance.
    //    /// </summary>
    //    public override void Stop()
    //    {
    //    }

    //    /// <summary>
    //    ///     Disposes this instance.
    //    /// </summary>
    //    public override void Dispose()
    //    {
    //    }
    //}

    /// <summary>
    ///     Class ConcurrentQueueMailbox.
    /// </summary>
    public class ConcurrentQueueMailbox : Mailbox
    {
        /// <summary>
        ///     The system messages
        /// </summary>
        private readonly ConcurrentQueue<Envelope> systemMessages = new ConcurrentQueue<Envelope>();

        /// <summary>
        ///     The user messages
        /// </summary>
        private readonly ConcurrentQueue<Envelope> userMessages = new ConcurrentQueue<Envelope>();

        /// <summary>
        ///     The dead line timer
        /// </summary>
        private Stopwatch deadLineTimer;

        /// <summary>
        ///     The has unscheduled messages
        /// </summary>
        private volatile bool hasUnscheduledMessages;

        /// <summary>
        ///     The is closed
        /// </summary>
        private volatile bool isClosed;

        /// <summary>
        ///     The status
        /// </summary>
        private int status;

        /// <summary>
        ///     Runs the Message Pump.
        /// </summary>
        private void Run()
        {
            if (isClosed)
            {
                return;
            }

            //if ThroughputDeadlineTime is enabled, start a stopwatch
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

            //we are about to process all enqueued messages
            hasUnscheduledMessages = false;
            Envelope envelope;

            //start with system messages, they have the highest priority
            while (systemMessages.TryDequeue(out envelope))
            {
                SystemInvoke(envelope);
            }

            //we should process x messages in this run
            int left = dispatcher.Throughput;

            //try dequeue a user message
            while (userMessages.TryDequeue(out envelope))
            {
                //run the receive handler
                Invoke(envelope);

                //check if any system message have arrived while processing user messages
                if (systemMessages.TryDequeue(out envelope))
                {
                    //handle system message
                    SystemInvoke(envelope);
                    break;
                }
                left--;
                if (isClosed)
                    return;

                //if deadline time have expired, stop and break
                if (dispatcher.ThroughputDeadlineTime.HasValue && deadLineTimer.ElapsedTicks > dispatcher.ThroughputDeadlineTime.Value)
                {
                    deadLineTimer.Stop();
                    break;
                }

                //we are done processing messages for this run
                if (left == 0)
                {
                    break;
                }
            }

            //there are still messages that needs to be processed
            if (userMessages.Count > 0)
            {
                hasUnscheduledMessages = true;
            }

            if (hasUnscheduledMessages)
            {
                dispatcher.Schedule(Run);
            }
            else
            {
                Interlocked.Exchange(ref status, MailboxStatus.Idle);
            }
        }


        /// <summary>
        ///     Schedules this instance.
        /// </summary>
        private void Schedule()
        {
            //only schedule if we idle
            if (Interlocked.Exchange(ref status, MailboxStatus.Busy) == MailboxStatus.Idle)
            {
                dispatcher.Schedule(Run);
            }
        }

        /// <summary>
        ///     Posts the specified envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
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

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public override void Stop()
        {
            isClosed = true;
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public override void Dispose()
        {
            isClosed = true;
        }

        /// <summary>
        ///     Class MailboxStatus.
        /// </summary>
        private static class MailboxStatus
        {
            /// <summary>
            ///     The idle
            /// </summary>
            public const int Idle = 0;

            /// <summary>
            ///     The busy
            /// </summary>
            public const int Busy = 1;
        }
    }

    /// <summary>
    ///     Special mailbox that processes the last message first
    ///     Useful for some real time edge cases
    /// </summary>
    public class ConcurrentStackMailbox : Mailbox
    {
        /// <summary>
        ///     The system messages
        /// </summary>
        private readonly ConcurrentQueue<Envelope> systemMessages = new ConcurrentQueue<Envelope>();

        /// <summary>
        ///     The user messages
        /// </summary>
        private readonly ConcurrentStack<Envelope> userMessages = new ConcurrentStack<Envelope>();

        /// <summary>
        ///     The dead line timer
        /// </summary>
        private Stopwatch deadLineTimer;

        /// <summary>
        ///     The has unscheduled messages
        /// </summary>
        private volatile bool hasUnscheduledMessages;

        /// <summary>
        ///     The is closed
        /// </summary>
        private volatile bool isClosed;

        /// <summary>
        ///     The status
        /// </summary>
        private int status;

        /// <summary>
        ///     Runs the Message Pump.
        /// </summary>
        private void Run()
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


        /// <summary>
        ///     Schedules this instance.
        /// </summary>
        private void Schedule()
        {
            //only schedule if we idle
            if (Interlocked.Exchange(ref status, MailboxStatus.Busy) == MailboxStatus.Idle)
            {
                dispatcher.Schedule(Run);
            }
        }

        /// <summary>
        ///     Posts the specified envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
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

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public override void Stop()
        {
            isClosed = true;
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public override void Dispose()
        {
            isClosed = true;
        }

        /// <summary>
        ///     Class MailboxStatus.
        /// </summary>
        private static class MailboxStatus
        {
            /// <summary>
            ///     The idle
            /// </summary>
            public const int Idle = 0;

            /// <summary>
            ///     The busy
            /// </summary>
            public const int Busy = 1;
        }
    }
}