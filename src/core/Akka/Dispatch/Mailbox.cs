//-----------------------------------------------------------------------
// <copyright file="Mailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            /// <summary>
            /// TBD
            /// </summary>
            public const int Open = 0; // _status is not initialized in AbstractMailbox, so default must be zero!
            /// <summary>
            /// TBD
            /// </summary>
            public const int Closed = 1;

            // secondary status
            /// <summary>
            /// TBD
            /// </summary>
            public const int Scheduled = 2;

            // shifted by 2 - the suspend count
            /// <summary>
            /// TBD
            /// </summary>
            public const int ShouldScheduleMask = 3;
            /// <summary>
            /// TBD
            /// </summary>
            public const int ShouldNotProcessMask = ~2;
            /// <summary>
            /// TBD
            /// </summary>
            public const int SuspendMask = ~3;
            /// <summary>
            /// TBD
            /// </summary>
            public const int SuspendUnit = 4;
            /// <summary>
            /// TBD
            /// </summary>
            public const int SuspendAwaitTask = ~4;
        }

        /*
         * This is needed for actually executing the mailbox, i.e. invoking the
         * ActorCell. There are situations (e.g. RepointableActorRef) where a Mailbox
         * is constructed but we know that we will not execute it, in which case this
         * will be null. It must be a var to support switching into an "active"
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
        /// <param name="receiver">The actor sending this message to the mailbox</param>
        /// <param name="envelope">The envelope.</param>
        internal void Enqueue(IActorRef receiver, Envelope envelope)
        {
            MessageQueue.Enqueue(receiver, envelope);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="msg">TBD</param>
        /// <returns>TBD</returns>
        internal bool TryDequeue(out Envelope msg)
        {
            return MessageQueue.TryDequeue(out msg);
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal bool HasMessages => MessageQueue.HasMessages;

        /// <summary>
        /// TBD
        /// </summary>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="old">TBD</param>
        /// <param name="newQueue">TBD</param>
        /// <returns>TBD</returns>
        internal bool SystemQueuePut(LatestFirstSystemMessageList old, LatestFirstSystemMessageList newQueue)
        {
            // Note: calling .head is not actually existing on the bytecode level as the parameters _old and _new
            // are SystemMessage instances hidden during compile time behind the SystemMessageList value class.
            // Without calling .head the parameters would be boxed in SystemMessageList wrapper.
            var prev = old.Head;
            return Interlocked.CompareExchange(ref _systemQueueDoNotCallMeDirectly, newQueue.Head, old.Head) == prev;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="hasMessageHint">TBD</param>
        /// <param name="hasSystemMessageHint">TBD</param>
        /// <returns>TBD</returns>
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
        /// <param name="actorCell">TBD</param>
        public virtual void SetActor(ActorCell actorCell)
        {
            _actor = actorCell;
        }

        /// <summary>
        /// TBD
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int CurrentStatus() { return Volatile.Read(ref _statusDotNotCallMeDirectly); }

        /// <summary>
        /// TBD
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool ShouldProcessMessage() { return (CurrentStatus() & MailboxStatus.ShouldNotProcessMask) == 0; }

        /// <summary>
        /// Returns the number of times this mailbox is currently suspended.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int SuspendCount() { return CurrentStatus() / MailboxStatus.SuspendUnit; }

        /// <summary>
        /// Returns <c>true</c> if the mailbox is currently suspended from processing. <c>false</c> otherwise.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsSuspended() { return (CurrentStatus() & MailboxStatus.SuspendMask) != 0; }

        /// <summary>
        /// Returns <c>true</c> if the mailbox is closed. <c>false</c> otherwise.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsClosed() { return (CurrentStatus() == MailboxStatus.Closed); }

        /// <summary>
        /// Returns <c>true</c> if the mailbox is scheduled for execution on a <see cref="Dispatcher"/>. <c>false</c> otherwise.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsScheduled()
        {
            return (CurrentStatus() & MailboxStatus.Scheduled) != 0;
        }

        /// <summary>
        /// Updates the status of the current mailbox.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool UpdateStatus(int oldStatus, int newStatus)
        {
            return Interlocked.CompareExchange(ref _statusDotNotCallMeDirectly, newStatus, oldStatus) == oldStatus;
        }

        /// <summary>
        /// Forcefully sets the status of the current mailbox.
        /// </summary>
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
        ///  Set the new primary status to <see cref="MailboxStatus.Closed"/>.
        /// </summary>
        /// <returns><c>true</c> if we were able to successfully close the mailbox. <c>false</c> otherwise.</returns>
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
        /// <returns>Returns <c>true</c> if the set operation succeeded. <c>false</c> otherwise.</returns>
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
        /// <returns>Returns <c>true</c> if the set operation succeeded. <c>false</c> otherwise.</returns>
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
                if (!TryDequeue(out var next)) return;

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
                Actor.SystemInvoke(msg);

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

        /* In JVM the following three methods are implemented as an internal trait. Added them directly onto the Mailbox itself instead. */
        /// <summary>
        /// Enqueues a new <see cref="ISystemMessage"/> into the <see cref="Mailbox"/> for a given actor.
        /// </summary>
        /// <param name="receiver">The actor who will receive the system message.</param>
        /// <param name="message">The system message.</param>
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

        /// <summary>
        /// Drains <see cref="ISystemMessage"/> from this mailbox.
        /// </summary>
        /// <param name="newContents">The replacement queue for the system messages inside this mailbox.</param>
        internal virtual EarliestFirstSystemMessageList SystemDrain(LatestFirstSystemMessageList newContents)
        {
            var currentList = SystemQueue;
            if (currentList.Head is NoMessage) return new EarliestFirstSystemMessageList(null);
            else if (SystemQueuePut(currentList, newContents)) return currentList.Reverse;
            else return SystemDrain(newContents);
        }

        /// <summary>
        /// Returns <c>true</c> if there are <see cref="ISystemMessage"/> instances inside this mailbox.
        /// <c>false</c> otherwise.
        /// </summary>
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
        /// <param name="message">TBD</param>
        /// <param name="args">TBD</param>
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
    /// Possibly important notice.
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
        /// <summary>
        /// The settings for the given <see cref="ActorSystem"/>.
        /// </summary>
        protected readonly Settings Settings;

        /// <summary>
        /// The configuration for this mailbox.
        /// </summary>
        protected readonly Config Config;

        /// <summary>
        /// Constructor used for creating a <see cref="MailboxType"/>
        /// </summary>
        /// <param name="settings">The <see cref="ActorSystem.Settings"/> for this system.</param>
        /// <param name="config">The <see cref="Config"/> for this mailbox.</param>
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
    /// Compliment to <see cref="IRequiresMessageQueue{T}"/>
    /// </summary>
    /// <typeparam name="TQueue">The type of <see cref="IMessageQueue"/> produced by this class.</typeparam>
    public interface IProducesMessageQueue<TQueue> where TQueue : IMessageQueue { }

    /// <summary>
    /// UnboundedMailbox is the default <see cref="MailboxType"/> used by Akka.NET Actors
    /// </summary>
    public sealed class UnboundedMailbox : MailboxType, IProducesMessageQueue<UnboundedMessageQueue>
    {
        /// <inheritdoc cref="MailboxType"/>
        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new UnboundedMessageQueue();
        }


        /// <summary>
        /// Default constructor for an unbounded mailbox.
        /// </summary>
        public UnboundedMailbox() : this(null, null)
        {
        }

        /// <inheritdoc cref="MailboxType"/>
        public UnboundedMailbox(Settings settings, Config config) : base(settings, config)
        {
        }
    }

    /// <summary>
    /// The default bounded mailbox implementation
    /// </summary>
    public sealed class BoundedMailbox : MailboxType, IProducesMessageQueue<BoundedMessageQueue>
    {
        /// <summary>
        /// The capacity of this mailbox.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// The push timeout value. Will throw a timeout error after this period of time
        /// </summary>
        public TimeSpan PushTimeout { get; }

        /// <inheritdoc cref="MailboxType"/>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the 'mailbox-capacity' in <paramref name="config"/>
        /// or the 'mailbox-push-timeout-time' in <paramref name="config"/> is negative.
        /// </exception>
        public BoundedMailbox(Settings settings, Config config) : base(settings, config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<BoundedMailbox>();

            Capacity = config.GetInt("mailbox-capacity", 0);
            PushTimeout = config.GetTimeSpan("mailbox-push-timeout-time", TimeSpan.FromSeconds(-1));

            if (Capacity < 0) throw new ArgumentException("The capacity for BoundedMailbox cannot be negative", nameof(config));
            if (PushTimeout.TotalSeconds < 0) throw new ArgumentException("The push time-out for BoundedMailbox cannot be be negative", nameof(config));
        }

        /// <inheritdoc cref="MailboxType"/>
        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new BoundedMessageQueue(Capacity, PushTimeout);
        }
    }

    /// <summary>
    /// Priority mailbox base class; unbounded mailbox that allows for prioritization of its contents.
    /// Extend this class and implement the <see cref="PriorityGenerator"/> method with your own prioritization.
    /// The value returned by the <see cref="PriorityGenerator"/> method will be used to order the message in the mailbox.
    /// Lower values will be delivered first. Messages ordered by the same number will remain in delivered in undefined order.
    /// </summary>
    public abstract class UnboundedPriorityMailbox : MailboxType, IProducesMessageQueue<UnboundedPriorityMessageQueue>
    {
        /// <summary>
        /// Function responsible for generating the priority value of a message based on its type and content.
        /// </summary>
        /// <param name="message">The message to inspect.</param>
        /// <returns>An integer. The lower the value, the higher the priority.</returns>
        protected abstract int PriorityGenerator(object message);

        /// <summary>
        /// The initial capacity of the unbounded mailbox.
        /// </summary>
        public int InitialCapacity { get; }

        /// <summary>
        /// The default capacity of an unbounded priority mailbox.
        /// </summary>
        public const int DefaultCapacity = 11;

        /// <inheritdoc cref="MailboxType"/>
        public sealed override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new UnboundedPriorityMessageQueue(PriorityGenerator, InitialCapacity);
        }

        /// <inheritdoc cref="MailboxType"/>
        protected UnboundedPriorityMailbox(Settings settings, Config config) : base(settings, config)
        {
            InitialCapacity = DefaultCapacity;
        }
    }

    //todo: bounded priority mailbox;

    /// <summary>
    /// Priority mailbox - an unbounded mailbox that allows for prioritization of its contents.
    /// Extend this class and implement the <see cref="PriorityGenerator"/> method with your own prioritization.
    /// The value returned by the <see cref="PriorityGenerator"/> method will be used to order the message in the mailbox.
    /// Lower values will be delivered first. Messages ordered by the same number will remain in delivery order.
    /// </summary>
    public abstract class UnboundedStablePriorityMailbox : MailboxType, IProducesMessageQueue<UnboundedStablePriorityMessageQueue>
    {
        /// <summary>
        /// Function responsible for generating the priority value of a message based on its type and content.
        /// </summary>
        /// <param name="message">The message to inspect.</param>
        /// <returns>An integer. The lower the value, the higher the priority.</returns>
        protected abstract int PriorityGenerator(object message);

        /// <summary>
        /// The initial capacity of the unbounded mailbox.
        /// </summary>
        public int InitialCapacity { get; }

        /// <summary>
        /// The default capacity of an unbounded priority mailbox.
        /// </summary>
        public const int DefaultCapacity = 11;

        /// <inheritdoc cref="MailboxType"/>
        public sealed override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new UnboundedStablePriorityMessageQueue(PriorityGenerator, InitialCapacity);
        }

        /// <inheritdoc cref="MailboxType"/>
        protected UnboundedStablePriorityMailbox(Settings settings, Config config) : base(settings, config)
        {
            InitialCapacity = DefaultCapacity;
        }
    }

    /// <summary>
    /// UnboundedDequeBasedMailbox is an unbounded <see cref="MailboxType"/> backed by a double-ended queue. Used for stashing.
    /// </summary>
    public sealed class UnboundedDequeBasedMailbox : MailboxType, IProducesMessageQueue<UnboundedDequeMessageQueue>
    {
        /// <inheritdoc cref="MailboxType"/>
        public UnboundedDequeBasedMailbox(Settings settings, Config config) : base(settings, config)
        {
        }

        /// <inheritdoc cref="MailboxType"/>
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
        /// <summary>
        /// The capacity of this mailbox.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// The push timeout. Fires a <see cref="TimeoutException"/> if it takes longer than this to add a message to
        /// a full bounded mailbox.
        /// </summary>
        public TimeSpan PushTimeout { get; }

        /// <inheritdoc cref="MailboxType"/>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the 'mailbox-capacity' in <paramref name="config"/>
        /// or the 'mailbox-push-timeout-time' in <paramref name="config"/> is negative.
        /// </exception>
        public BoundedDequeBasedMailbox(Settings settings, Config config) : base(settings, config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<BoundedDequeBasedMailbox>();

            Capacity = config.GetInt("mailbox-capacity", 0);
            PushTimeout = config.GetTimeSpan("mailbox-push-timeout-time", TimeSpan.FromSeconds(-1));

            if (Capacity < 0) throw new ArgumentException("The capacity for BoundedMailbox cannot be negative", nameof(config));
            if (PushTimeout.TotalSeconds < 0) throw new ArgumentException("The push time-out for BoundedMailbox cannot be null", nameof(config));
        }

        /// <inheritdoc cref="MailboxType"/>
        public override IMessageQueue Create(IActorRef owner, ActorSystem system)
        {
            return new BoundedDequeMessageQueue(Capacity, PushTimeout);
        }
    }

}

