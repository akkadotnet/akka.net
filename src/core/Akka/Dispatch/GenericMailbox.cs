﻿using System.Diagnostics;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch.MessageQueues;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    /// <summary>
    /// Class Mailbox of TSys,TUser.
    /// </summary>
    public abstract class Mailbox<TSys,TUser> : MessageQueueMailbox 
        where TSys:MessageQueue
        where TUser:MessageQueue
    {
        private Stopwatch _deadLineTimer;
        private volatile bool _isClosed;
        private TSys _systemMessages;
        private TUser _userMessages;

        protected TSys SystemMessages
        {
            get { return _systemMessages; }
        }

        protected TUser UserMessages
        {
            get { return _userMessages; }
        }

        protected Mailbox()
        {
            InitMessageQueues();
        }

        private void InitMessageQueues()
        {
            _systemMessages = CreateSystemMessagesQueue();
            _userMessages = CreateUserMessagesQueue();
        }


        protected abstract TSys CreateSystemMessagesQueue();

        protected abstract TUser CreateUserMessagesQueue();

        private void Run()
        {
            if (_isClosed)
            {
                return;
            }

            var throughputDeadlineTime = dispatcher.ThroughputDeadlineTime;
            ActorCell.UseThreadContext(() =>
            {
                //if ThroughputDeadlineTime is enabled, start a stopwatch
                if (throughputDeadlineTime.HasValue && throughputDeadlineTime.Value > 0)
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
                    Mailbox.DebugPrint(ActorCell.Self + " processing system message " + envelope);
                    // TODO: Add + " with " + ActorCell.GetChildren());
                    ActorCell.SystemInvoke(envelope);
                }

                //we should process x messages in this run
                var left = dispatcher.Throughput;

                //try dequeue a user message
                while (!_isSuspended && !_isClosed && _userMessages.TryDequeue(out envelope))
                {
                    Mailbox.DebugPrint(ActorCell.Self + " processing message " + envelope);

                    //run the receive handler
                    ActorCell.Invoke(envelope);

                    //check if any system message have arrived while processing user messages
                    if (_systemMessages.TryDequeue(out envelope))
                    {
                        //handle system message
                        Mailbox.DebugPrint(ActorCell.Self + " processing system message " + envelope);
                        // TODO: Add + " with " + ActorCell.GetChildren());
                        ActorCell.SystemInvoke(envelope);
                        break;
                    }
                    left--;
                    if (_isClosed)
                        return;

                    //if deadline time have expired, stop and break
                    if (throughputDeadlineTime.HasValue && throughputDeadlineTime.Value > 0 &&
                        _deadLineTimer.ElapsedTicks > throughputDeadlineTime.Value)
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
        /// Schedules this instance.
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
        /// Posts the specified envelope.
        /// </summary>
        /// <param name="receiver"></param>
        /// <param name="envelope"> The envelope. </param>
        public override void Post(ActorRef receiver, Envelope envelope)
        {
            if (_isClosed)
                return;

            hasUnscheduledMessages = true;
            if (envelope.Message is SystemMessage)
            {
                Mailbox.DebugPrint("{0} enqueued system message {1} to {2}", ActorCell.Self, envelope, ActorCell.Self.Equals(receiver) ? "itself" : receiver.ToString());
                _systemMessages.Enqueue(envelope);
            }
            else
            {
                Mailbox.DebugPrint("{0} enqueued message {1} to {2}", ActorCell.Self, envelope, ActorCell.Self.Equals(receiver) ? "itself" : receiver.ToString());
                _userMessages.Enqueue(envelope);
            }

            Schedule();
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public override void BecomeClosed()
        {
            _isClosed = true;
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public override void Dispose()
        {
            _isClosed = true;
        }

        //TODO: should we only check userMessages? not system messages?
        protected override int GetNumberOfMessages()
        {
            return _userMessages.Count;
        }

        public override void CleanUp()
        {
            var actorCell = ActorCell;
            if (actorCell != null) // actor is null for the deadLetterMailbox
            {
                var deadLetterMailbox = actorCell.System.Mailboxes.DeadLetterMailbox;
                Envelope envelope;
                while (_systemMessages.TryDequeue(out envelope))
                {
                    deadLetterMailbox.Post(actorCell.Self, envelope);
                }
                while (_userMessages.TryDequeue(out envelope))
                {
                    deadLetterMailbox.Post(actorCell.Self, envelope);
                }
            }

            //Akka JVM code:
            //   if (actor ne null) { // actor is null for the deadLetterMailbox
            //     val dlm = actor.dispatcher.mailboxes.deadLetterMailbox
            //     var messageList = systemDrain(new LatestFirstSystemMessageList(NoMessage))
            //     while (messageList.nonEmpty) {
            //       // message must be “virgin” before being able to systemEnqueue again
            //       val msg = messageList.head
            //       messageList = messageList.tail
            //       msg.unlink()
            //       dlm.systemEnqueue(actor.self, msg)
            //     }

            //     if (messageQueue ne null) // needed for CallingThreadDispatcher, which never calls Mailbox.run()
            //       messageQueue.cleanUp(actor.self, actor.dispatcher.mailboxes.deadLetterMailbox.messageQueue)
            //   }
        }

        public override MessageQueue MessageQueue
        {
            get { return _userMessages; }
        }
    }
}