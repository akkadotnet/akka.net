//-----------------------------------------------------------------------
// <copyright file="Mailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.MessageQueues;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Assert = System.Diagnostics.Debug;

namespace Akka.Dispatch
{


    /// <summary>
    /// Mailbox base class
    /// </summary>
    public class Mailbox : IRunnable
    {
        /// <summary>
        ///  Status codes for the state of the mailbox
        /// </summary>
        internal static class MailboxStatus
        {
            // primary status
            public const int Open = 0; // _status is not initialized in AbstractMailbox, so default must be zero! 
            public const int Closed = 1;

            // secondary status
            public const int Scheduled = 2;

            // shifted by 2 - the suspend count
            public const int ShouldScheduleMask = 3;
            public const int ShouldNotProcessMask = ~2;
            public const int SuspendMask = ~3;
            public const int SuspendUnit = 4;
            public const int SuspendAwaitTask = ~4;
        }

        /*
         * This is needed for actually executing the mailbox, i.e. invoking the
         * ActorCell. There are situations (e.g. RepointableActorRef) where a Mailbox
         * is constructed but we know that we will not execute it, in which case this
         * will be null. It must be a var to support switching into an “active”
         * mailbox, should the owning ActorRef turn local.
         *
         * ANOTHER THING, IMPORTANT:
         *
         * ActorCell.start() publishes actorCell & self to the dispatcher, which
         * means that messages may be processed theoretically before self’s constructor
         * ends. The JMM guarantees visibility for final fields only after the end
         * of the constructor, so safe publication requires that THIS WRITE BELOW
         * stay as it is.
         */
        private volatile ActorCell _actor = null;
        private volatile SystemMessage _systemQueueDoNotCallMeDirectly = null; // null by default
        private volatile int _statusDotNotCallMeDirectly; //0 by default

        /// <summary>
        /// The queue used for user-defined messages inside this mailbox
        /// </summary>
        public IMessageQueue MessageQueue { get; }

        /// <summary>
        /// Creates a new mailbox
        /// </summary>
        /// <param name="messageQueue">The <see cref="IMessageQueue"/> used by this mailbox.</param>
        public Mailbox(IMessageQueue messageQueue)
        {
            MessageQueue = messageQueue;
        }

        /// <summary>
        ///     Posts the specified envelope to the mailbox.
        /// </summary>
        /// <param name="receiver"></param>
        /// <param name="envelope">The envelope.</param>
        internal void Enqueue(IActorRef receiver, Envelope envelope)
        {
            MessageQueue.Enqueue(receiver, envelope);
        }

        internal bool TryDequeue(out Envelope msg)
        {
            return MessageQueue.TryDequeue(out msg);
        }

        internal bool HasMessages => MessageQueue.HasMessages;

        internal int NumberOfMessages => MessageQueue.Count;


        /// <summary>
        /// Atomic reader for the system message queue
        /// </summary>
        internal LatestFirstSystemMessageList SystemQueue
        {
            get
            {
                // Note: contrary how it looks, there is no allocation here, as SystemMessageList is a value class and as such
                // it just exists as a typed view during compile-time. The actual return type is still SystemMessage.
                return new LatestFirstSystemMessageList(Volatile.Read(ref _systemQueueDoNotCallMeDirectly));
            }
        }

        internal bool SystemQueuePut(LatestFirstSystemMessageList old, LatestFirstSystemMessageList newQueue)
        {
            // Note: calling .head is not actually existing on the bytecode level as the parameters _old and _new
            // are SystemMessage instances hidden during compile time behind the SystemMessageList value class.
            // Without calling .head the parameters would be boxed in SystemMessageList wrapper.
            var prev = old.Head;
            return Interlocked.CompareExchange(ref _systemQueueDoNotCallMeDirectly, newQueue.Head, old.Head) == prev;
        }

        internal bool CanBeScheduledForExecution(bool hasMessageHint, bool hasSystemMessageHint)
        {
            var currentStatus = CurrentStatus();
            if (currentStatus == MailboxStatus.Open || currentStatus == MailboxStatus.Scheduled)
                return hasMessageHint || hasSystemMessageHint || HasSystemMessages || HasMessages;
            if (currentStatus == MailboxStatus.Closed) return false;
            return hasSystemMessageHint || HasSystemMessages;
        }

        /// <summary>
        /// The <see cref="MessageDispatcher"/> for the underlying mailbox.
        /// </summary>
        public MessageDispatcher Dispatcher => _actor.Dispatcher;

        /// <summary>
        /// INTERNAL API
        /// 
        /// <see cref="Actor"/> must not be visible to user-defined implementations
        /// </summary>
        internal ActorCell Actor => _actor;

        /// <summary>
        ///     Attaches an ActorCell to the Mailbox.
        /// </summary>
        /// <param name="actorCell"></param>
        public virtual void SetActor(ActorCell actorCell)
        {
            _actor = actorCell;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int CurrentStatus() { return Volatile.Read(ref _statusDotNotCallMeDirectly); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ShouldProcessMessage() { return (CurrentStatus() & MailboxStatus.ShouldNotProcessMask) == 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int SuspendCount() { return CurrentStatus() / MailboxStatus.SuspendUnit; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsSuspended() { return (CurrentStatus() & MailboxStatus.SuspendMask) != 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsClosed() { return (CurrentStatus() == MailboxStatus.Closed); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsScheduled()
        {
            return (CurrentStatus() & MailboxStatus.Scheduled) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool UpdateStatus(int oldStatus, int newStatus)
        {
            return Interlocked.CompareExchange(ref _statusDotNotCallMeDirectly, newStatus, oldStatus) == oldStatus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetStatus(int newStatus)
        {
            Volatile.Write(ref _statusDotNotCallMeDirectly, newStatus);
        }

        /// <summary>
        /// Reduce the suspend count by one. Caller does not need to worry about whether
        /// status was <see cref="MailboxStatus.Scheduled"/> or not.
        /// </summary>
        /// <returns><c>true</c> if the suspend count reached zero.</returns>
        internal bool Resume()
        {
            var status = CurrentStatus();
            if (status == MailboxStatus.Closed)
            {
                SetStatus(MailboxStatus.Closed);
                return false;
            }
            else
            {
                var next = status < MailboxStatus.SuspendUnit ? status : status - MailboxStatus.SuspendUnit;
                if (UpdateStatus(status, next)) return next < MailboxStatus.SuspendUnit;
                else return Resume();
            }
        }

        /// <summary>
        /// Increment the suspend count by one. Caller does not need to worry about whether
        /// status was <see cref="MailboxStatus.Scheduled"/> or not.
        /// </summary>
        /// <returns><c>true</c> if the previous suspend count was zero.</returns>
        internal bool Suspend()
        {
            var status = CurrentStatus();
            if (status == MailboxStatus.Closed)
            {
                SetStatus(MailboxStatus.Closed);
                return false;
            }
            else
            {
                if (UpdateStatus(status, status + MailboxStatus.SuspendUnit)) return status < MailboxStatus.SuspendUnit;
                else return Suspend();
            }
        }

        /// <summary>
        ///  Set the new primary status to 
        /// </summary>
        internal bool BecomeClosed()
        {
            var status = CurrentStatus();
            if (status == MailboxStatus.Closed)
            {
                SetStatus(MailboxStatus.Closed);
                return false;
            }
            return UpdateStatus(status, MailboxStatus.Closed) || BecomeClosed();
        }

        /// <summary>
        /// Set scheduled status, keeping primary status as-is.
        /// </summary>
        internal bool SetAsScheduled()
        {
            while (true)
            {
                var s = CurrentStatus();
                /*
                 * Only try to add Scheduled bit if pure Open/Suspended, not Closed or with
                 * Scheduled bit already set.
                 */
                if ((s & MailboxStatus.ShouldScheduleMask) != MailboxStatus.Open) return false;
                if (UpdateStatus(s, s | MailboxStatus.Scheduled))
                    return true;
            }
        }

        /// <summary>
        /// Reset Scheduled status, keeping primary status as-is
        /// </summary>
        internal bool SetAsIdle()
        {
            while (true)
            {
                var s = CurrentStatus();
                if (UpdateStatus(s, s & ~MailboxStatus.Scheduled))
                    return true;
            }
        }

        /// <summary>
        /// Processes the contents of the mailbox
        /// </summary>
        public void Run()
        {
            try
            {
                if (!IsClosed()) // Volatile read, needed here
                {
                    Actor.UseThreadContext(() =>
                    {
                        ProcessAllSystemMessages(); // First, deal with any system messages
                        ProcessMailbox(); // Then deal with messages
                    });
                }
            }
            finally
            {
                SetAsIdle(); // Volatile write, needed here
                Dispatcher.RegisterForExecution(this, false, false); // schedule to run again if there are more messages, possibly
            }
        }

        private void ProcessMailbox()
        {
            ProcessMailbox(Math.Max(1, Dispatcher.Throughput), Dispatcher.ThroughputDeadlineTime ?? 0L);
        }

        private void ProcessMailbox(int left, long deadlineTicks)
        {
            while (ShouldProcessMessage())
            {
                Envelope next;
                if (!TryDequeue(out next)) return;

                DebugPrint("{0} processing message {1}", Actor.Self, next);

                // not going to bother catching ThreadAbortExceptions here, since they'll get rethrown anyway
                Actor.Invoke(next);
                ProcessAllSystemMessages();
                if (left > 1 && (Dispatcher.ThroughputDeadlineTime.HasValue == false || (MonotonicClock.GetTicks() - deadlineTicks) < 0))
                {
                    left = left - 1;
                    continue;
                }
                break;
            }
        }

        /// <summary>
        /// Will at least try to process all queued system messages: in case of
        /// failure simply drop and go on to the next, because there is nothing to
        /// restart here (failure is in <see cref="ActorCell"/> somewhere …). In case the mailbox
        /// becomes closed (because of processing a <see cref="Terminate"/> message), dump all
        /// already dequeued message to deadLetters. 
        /// </summary>
        private void ProcessAllSystemMessages()
        {
            Exception interruption = null;
            var messageList = SystemDrain(SystemMessageList.LNil);
            while (messageList.NonEmpty && !IsClosed())
            {
                var msg = messageList.Head;
                messageList = messageList.Tail;
                msg.Unlink();
                DebugPrint("{0} processing system message {1} with {2}", Actor.Self, msg, string.Join(",", Actor.GetChildren()));
                // we know here that SystemInvoke ensures that only "fatal" exceptions get rethrown
#if UNSAFE_THREADING
                try
                {
                    Actor.SystemInvoke(msg);
                }

                catch (ThreadInterruptedException ex)
                // thrown only if thread is explicitly interrupted, which should never happen
                {
                    interruption = ex;
                }
                catch (ThreadAbortException ex) // can be thrown if dispatchers shutdown / application terminates / etc
                {
                    interruption = ex;
                }
#else 
                Actor.SystemInvoke(msg);
#endif

                // don't ever execute normal message when system message present!
                if (messageList.IsEmpty && !IsClosed())
                    messageList = SystemDrain(SystemMessageList.LNil);
            }

            /*
             * if we closed the mailbox, we must dump the remaining system messages
             * to deadLetters (this is essential for DeathWatch)
             */
            var dlm = Actor.Dispatcher.Mailboxes.DeadLetterMailbox;
            while (messageList.NonEmpty)
            {
                var msg = messageList.Head;
                messageList = messageList.Tail;
                msg.Unlink();
                try
                {
                    dlm.SystemEnqueue(Actor.Self, msg);
                }
#if UNSAFE_THREADING
                catch (ThreadInterruptedException ex)
                // thrown only if thread is explicitly interrupted, which should never happen
                {
                    interruption = ex;
                }
                catch (ThreadAbortException ex) // can be thrown if dispatchers shutdown / application terminates / etc
                {
                    interruption = ex;
                }
#endif
                catch (Exception ex)
                {
                    Actor.System.EventStream.Publish(new Error(ex, GetType().FullName, GetType(), $"error while enqueuing {msg} to deadletters: {ex.Message}"));
                }
            }

            // if we got an interruption while handling system messages, rethrow it
            if (interruption != null)
            {
                // no need to clear interrupted flag in CLR, unlike JVM
                throw interruption;
            }
        }

        /// <summary>
        /// Overrideable callback to clean up the mailbox, called
        /// when an actor is unregistered.
        /// 
        /// By default it dequeues all system messages + messages and ships them to the owning actor's systems' <see cref="DeadLetterMailbox"/>.
        /// </summary>
        public virtual void CleanUp()
        {
            if (Actor != null)
            {
                var dlm = Actor.Dispatcher.Mailboxes.DeadLetterMailbox;
                var messageList = SystemDrain(new LatestFirstSystemMessageList(new NoMessage()));
                while (messageList.NonEmpty)
                {
                    // message must be "virgin" before being able to SystemEnqueue again
                    var msg = messageList.Head;
                    messageList = messageList.Tail;
                    msg.Unlink();
                    dlm.SystemEnqueue(Actor.Self, msg);
                }

                if (MessageQueue != null) // needed for CallingThreadDispatcher, which never calls Mailbox.Run
                {
                    MessageQueue.CleanUp(Actor.Self, dlm.MessageQueue);
                }
            }
        }

        /* In JVM the following three methods are implented as an internal trait. Added them directly onto the Mailbox itself instead. */
        internal virtual void SystemEnqueue(IActorRef receiver, SystemMessage message)
        {
            Assert.Assert(message.Unlinked);
            DebugPrint(receiver + " having enqueued " + message);
            var currentList = SystemQueue;
            if (currentList.Head is NoMessage)
            {
                Actor?.Dispatcher.Mailboxes.DeadLetterMailbox.SystemEnqueue(receiver, message);
            }
            else
            {
                if (!SystemQueuePut(currentList, currentList + message))
                {
                    message.Unlink();
                    SystemEnqueue(receiver, message);
                }
            }
        }

        internal virtual EarliestFirstSystemMessageList SystemDrain(LatestFirstSystemMessageList newContents)
        {
            var currentList = SystemQueue;
            if (currentList.Head is NoMessage) return new EarliestFirstSystemMessageList(null);
            else if (SystemQueuePut(currentList, newContents)) return currentList.Reverse;
            else return SystemDrain(newContents);
        }

        internal virtual bool HasSystemMessages
        {
            get
            {
                var head = SystemQueue.Head;
                return head != null && !(head is NoMessage);
            }
        }

        /// <summary>
        /// Prints a message tosStandard out if the Compile symbol "MAILBOXDEBUG" has been set.
        /// If the symbol is not set all invocations to this method will be removed by the compiler.
        /// </summary>
        [Conditional("MAILBOXDEBUG")]
        public static void DebugPrint(string message, params object[] args)
        {
            var formattedMessage = args.Length == 0 ? message : string.Format(message, args);
            Console.WriteLine("[MAILBOX][{0}][Thread {1:0000}] {2}", DateTime.Now.ToString("o"), Thread.CurrentThread.ManagedThreadId, formattedMessage);
        }

    }

    /// <summary>
    /// A factory to create <see cref="IMessageQueue"/>s for an optionally provided <see cref="IActorContext"/>.
    /// </summary>
    /// <remarks>
    /// Possibily important notice.
    /// 
    /// When implementing a custom MailboxType, be aware that there is special semantics attached to
    /// <see cref="ActorSystem.ActorOf"/> in that sending the returned <see cref="IActorRef"/> may, for a short
    /// period of time, enqueue the messages first in a dummy queue. Top-level actors are created in two steps, and only
    /// after the guardian actor ahs performed that second step will all previously sent messages be transferred from the
    /// dummy queue to the real mailbox.
    /// 
    /// Implemented as an abstract class in order to enforce constructor requirements.
    /// </remarks>
    public abstract class MailboxType
    {
        protected readonly Settings Settings;
        protected readonly Config Config;

        protected MailboxType(Settings settings, Config config)
        {
            Settings = settings;
            Config = config;
        }

        /// <summary>
        /// Creates a new <see cref="IMessageQueue"/> from the specified parameters.
        /// </summary>
        /// <param name="owner">Optional.</param>
        /// <param name="system">Optional.</param>
        /// <returns>The resulting <see cref="IMessageQueue"/></returns>
        public abstract IMessageQueue Create(IActorRef owner, ActorSystem system);
    }

    /// <summary>
    /// Compilment to <see cref="IRequiresMessageQueue{T}"/>
    /// </summary>
    /// <typeparam name="TQueue">The type of <see cref="IMessageQueue"/> produced by this class.</typeparam>
    public interface IProducesMessageQueue<TQueue> where TQueue : IMessageQueue { }

    /// <summary>
    /// UnboundedMailbox is the default <see cref="MailboxType"/> used by Akka.NET Actors
    /// </summary>
    public sealed class UnboundedMailbox : MailboxType, IProducesMessageQueue<UnboundedMessageQueue>
    {
        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new UnboundedMessageQueue();
        }


        public UnboundedMailbox() : this(null, null)
        {
        }

        public UnboundedMailbox(Settings settings, Config config) : base(settings, config)
        {
        }
    }

    /// <summary>
    /// The default bounded mailbox implementation
    /// </summary>
    public sealed class BoundedMailbox : MailboxType, IProducesMessageQueue<BoundedMessageQueue>
    {
        public int Capacity { get; }
        public TimeSpan PushTimeout { get; }

        public BoundedMailbox(Settings settings, Config config) : base(settings, config)
        {
            Capacity = config.GetInt("mailbox-capacity");
            PushTimeout = config.GetTimeSpan("mailbox-push-timeout-time", TimeSpan.FromSeconds(-1));

            if (Capacity < 0) throw new ArgumentOutOfRangeException(nameof(config), "The capacity for BoundedMailbox cannot be negative");
            if (PushTimeout.TotalSeconds < 0) throw new ArgumentNullException(nameof(config), "The push time-out for BoundedMailbox cannot be null");
        }

        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new BoundedMessageQueue(Capacity, PushTimeout);
        }
    }

    /// <summary>
    /// Priority mailbox base class; unbounded mailbox that allows for prioritization of its contents.
    /// Extend this class and implement the <see cref="PriorityGenerator"/> method with your own prioritization.
    /// </summary>
    public abstract class UnboundedPriorityMailbox : MailboxType, IProducesMessageQueue<UnboundedPriorityMessageQueue>
    {
        protected abstract int PriorityGenerator(object message);

        public int InitialCapacity { get; }

        public const int DefaultCapacity = 11;

        public sealed override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new UnboundedPriorityMessageQueue(PriorityGenerator, InitialCapacity);
        }

        protected UnboundedPriorityMailbox(Settings settings, Config config) : base(settings, config)
        {
            InitialCapacity = DefaultCapacity;
        }
    }

    //todo: bounded priority mailbox; stable priority mailboxes

    /// <summary>
    /// UnboundedDequeBasedMailbox is an unbounded <see cref="MailboxType"/> backed by a double-ended queue. Used for stashing.
    /// </summary>
    public sealed class UnboundedDequeBasedMailbox : MailboxType, IProducesMessageQueue<UnboundedDequeMessageQueue>
    {
        public UnboundedDequeBasedMailbox(Settings settings, Config config) : base(settings, config)
        {
        }

        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new UnboundedDequeMessageQueue();
        }
    }

    /// <summary>
    /// BoundedDequeBasedMailbox is an bounded <see cref="MailboxType"/> backed by a double-ended queue. Used for stashing.
    /// </summary>
    public sealed class BoundedDequeBasedMailbox : MailboxType, IProducesMessageQueue<BoundedDequeMessageQueue>
    {
        public int Capacity { get; }
        public TimeSpan PushTimeout { get; }

        public BoundedDequeBasedMailbox(Settings settings, Config config) : base(settings, config)
        {
            Capacity = config.GetInt("mailbox-capacity");
            PushTimeout = config.GetTimeSpan("mailbox-push-timeout-time", TimeSpan.FromSeconds(-1));

            if (Capacity < 0) throw new ArgumentOutOfRangeException(nameof(config), "The capacity for BoundedMailbox cannot be negative");
            if (PushTimeout.TotalSeconds < 0) throw new ArgumentNullException(nameof(config), "The push time-out for BoundedMailbox cannot be null");
        }

        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new BoundedDequeMessageQueue(Capacity, PushTimeout);
        }
    }

}

