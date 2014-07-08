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
    /// Mailbox base class
    /// </summary>
    public abstract class Mailbox : IDisposable
    {
        private volatile ActorCell _actorCell;
        protected MessageDispatcher dispatcher;
        protected ActorCell ActorCell { get { return _actorCell; } }

        /// <summary>
        ///     Attaches an ActorCell to the Mailbox.
        /// </summary>
        /// <param name="actorCell"></param>
        public void SetActor(ActorCell actorCell)
        {
            _actorCell = actorCell;
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        ///     Posts the specified envelope to the mailbox.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        public abstract void Post(Envelope envelope);

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public abstract void Stop();

        /// <summary>
        ///     Attaches a MessageDispatcher to the Mailbox.
        /// </summary>
        /// <param name="dispatcher">The dispatcher.</param>
        public void Setup(MessageDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        /// <summary>
        ///     The has unscheduled messages
        /// </summary>
// ReSharper disable once InconsistentNaming
        protected volatile bool hasUnscheduledMessages;

        internal bool HasUnscheduledMessages
        {
            get
            {
                return hasUnscheduledMessages;
            }
        }


        public void Start()
        {
            status = MailboxStatus.Idle;
            Schedule();
        }

        /// <summary>
        ///     The mailbox status (busy or idle)
        /// </summary>
// ReSharper disable once InconsistentNaming
        protected int status = MailboxStatus.Busy;  //HACK: Initially set the mailbox as busy in order for it not to scheduled until we want it to

        internal int Status
        {
            get
            {
                return status;
            }
        }

        /// <summary>
        ///     Class MailboxStatus.
        /// </summary>
        internal static class MailboxStatus
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

        protected abstract int GetNumberOfMessages();

        internal int NumberOfMessages
        {
            get
            {
                return GetNumberOfMessages();
            }
        }

        protected volatile bool _isSuspended;

        public void Suspend()
        {
            _isSuspended = true;
        }

        public void Resume()
        {
            _isSuspended = false;
            Schedule();
        }

        protected abstract void Schedule();



        /// <summary>
        /// Prints a message tosStandard out if the Compile symbol "MAILBOXDEBUG" has been set.
        /// If the symbol is not set all invocations to this method will be removed by the compiler.
        /// </summary>
        [Conditional("MAILBOXDEBUG")]
        public static void DebugPrint(string message, params object[] args)
        {
            Console.WriteLine(message, args);
        }

    }   

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
                    Mailbox.DebugPrint(ActorCell.Self + " processing system message " + envelope); // TODO: Add + " with " + ActorCell.GetChildren());
                    ActorCell.SystemInvoke(envelope);
                }

                //we should process x messages in this run
                int left = dispatcher.Throughput;

                //try dequeue a user message
                while (!_isSuspended && _userMessages.TryDequeue(out envelope))
                {
                    Mailbox.DebugPrint(ActorCell.Self + " processing message " + envelope);

                    //run the receive handler
                    ActorCell.Invoke(envelope);

                    //check if any system message have arrived while processing user messages
                    if (_systemMessages.TryDequeue(out envelope))
                    {
                        //handle system message
                        Mailbox.DebugPrint(ActorCell.Self + " processing system message " + envelope); // TODO: Add + " with " + ActorCell.GetChildren());
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
                Mailbox.DebugPrint(ActorCell.Self + " enqueued system message " + envelope);
                _systemMessages.Enqueue(envelope);
            }
            else
            {
                Mailbox.DebugPrint(ActorCell.Self + " enqueued message " + envelope);
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