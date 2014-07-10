using Akka.Actor;
using Akka.Dispatch.SysMsg;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Dispatch
{

    /// <summary>
    ///     Class ConcurrentQueueMailbox.
    /// </summary>
    public class ConcurrentQueueMailbox : Mailbox
    {
        private readonly ConcurrentQueue<Envelope> _systemMessages = new ConcurrentQueue<Envelope>();
        private readonly ConcurrentQueue<Envelope> _userMessages = new ConcurrentQueue<Envelope>();
        private Stopwatch _deadLineTimer;
        private volatile bool _isClosed;
        private void Run()
        {
            if (_isClosed)
            {
                return;
            }

            ActorCell.UseThreadContext(() =>
            {
                //if ThroughputDeadlineTime is enabled, start a stopwatch
                if (dispatcher.ThroughputDeadlineTime.HasValue)
                {
                    if (_deadLineTimer != null)
                    {
                        _deadLineTimer.Restart();
                    }
                    else
                    {
                        _deadLineTimer = Stopwatch.StartNew();
                    }
                }

                //we are about to process all enqueued messages
                hasUnscheduledMessages = false;
                Envelope envelope;

                //start with system messages, they have the highest priority
                while (_systemMessages.TryDequeue(out envelope))
                {
                    if (Mailbox.Debug) Console.WriteLine(ActorCell.Self + " processing system message " + envelope); // TODO: Add + " with " + ActorCell.GetChildren());
                    ActorCell.SystemInvoke(envelope);
                }

                //we should process x messages in this run
                int left = dispatcher.Throughput;

                //try dequeue a user message
                while (!_isSuspended && _userMessages.TryDequeue(out envelope))
                {
                    if (Mailbox.Debug) Console.WriteLine(ActorCell.Self + " processing message " + envelope);

                    //run the receive handler
                    ActorCell.Invoke(envelope);

                    //check if any system message have arrived while processing user messages
                    if (_systemMessages.TryDequeue(out envelope))
                    {
                        //handle system message
                        if (Mailbox.Debug) Console.WriteLine(ActorCell.Self + " processing system message " + envelope); // TODO: Add + " with " + ActorCell.GetChildren());
                        ActorCell.SystemInvoke(envelope);
                        break;
                    }
                    left--;
                    if (_isClosed)
                        return;

                    //if deadline time have expired, stop and break
                    if (dispatcher.ThroughputDeadlineTime.HasValue &&
                        _deadLineTimer.ElapsedTicks > dispatcher.ThroughputDeadlineTime.Value)
                    {
                        _deadLineTimer.Stop();
                        break;
                    }

                    //we are done processing messages for this run
                    if (left == 0)
                    {
                        break;
                    }
                }

                //there are still messages that needs to be processed
                if (_systemMessages.Count > 0 || (!_isSuspended && _userMessages.Count > 0))
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
            });
        }


        /// <summary>
        ///     Schedules this instance.
        /// </summary>
        protected override void Schedule()
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
            if (_isClosed)
                return;

            hasUnscheduledMessages = true;
            if (envelope.Message is SystemMessage)
            {
                if (Mailbox.Debug) Console.WriteLine(ActorCell.Self + " enqueued system message " + envelope);
                _systemMessages.Enqueue(envelope);
            }
            else
            {
                if (Mailbox.Debug) Console.WriteLine(ActorCell.Self + " enqueued message " + envelope);
                _userMessages.Enqueue(envelope);
            }

            Schedule();
        }

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public override void Stop()
        {
            _isClosed = true;
        }

        /// <summary>
        ///     Disposes this instance.
        /// </summary>
        public override void Dispose()
        {
            _isClosed = true;
        }


        protected override int GetNumberOfMessages()
        {
            return _userMessages.Count;
        }
    }   
}
