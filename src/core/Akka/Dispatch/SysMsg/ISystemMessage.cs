//-----------------------------------------------------------------------
// <copyright file="ISystemMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Assert = System.Diagnostics.Debug;

namespace Akka.Dispatch.SysMsg
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Value class supporting list operations on <see cref="ISystemMessage"/> instances. The 
    /// </summary>
    internal static class SystemMessageList
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly LatestFirstSystemMessageList LNil = new LatestFirstSystemMessageList(null);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly EarliestFirstSystemMessageList ENil = new EarliestFirstSystemMessageList(null);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="head">TBD</param>
        /// <param name="acc">TBD</param>
        /// <returns>TBD</returns>
        internal static int SizeInner(SystemMessage head, int acc)
        {
            while (true)
            {
                if (head == null) return acc;
                head = head.Next;
                acc = acc + 1;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="head">TBD</param>
        /// <param name="acc">TBD</param>
        /// <returns>TBD</returns>
        internal static SystemMessage ReverseInner(SystemMessage head, SystemMessage acc)
        {
            while (true)
            {
                if (head == null)
                    return acc;
                var next = head.Next;
                head.Next = acc;
                var head1 = head;
                head = next;
                acc = head1;
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Value type supporting list operations on system messages. The `next` field of <see cref="SystemMessage"/>
    /// is hidden, and can only accessed through the value classes <see cref="LatestFirstSystemMessageList"/> and
    /// <see cref="EarliestFirstSystemMessageList"/>, abstracting over the fact that system messages are the
    /// list nodes themselves. If used properly, this stays a compile time construct without any allocation overhead.
    ///
    /// This list is mutable.
    ///
    /// The type of the list also encodes that the messages contained are in reverse order, i.e. the head of the list is the
    /// latest appended element.
    /// </summary>
    internal struct LatestFirstSystemMessageList
    {
        /// <summary>
        /// The front of the list.
        /// </summary>
        public SystemMessage Head;

        /// <summary>
        /// Creates a new message list.
        /// </summary>
        /// <param name="head">The current head item.</param>
        public LatestFirstSystemMessageList(SystemMessage head)
        {
            Head = head;
        }

        /// <summary>
        /// Indicates if the list is empty or not. This operation has constant cost.
        /// </summary>
        public bool IsEmpty => Head == null;

        /// <summary>
        /// Indicates if the list has at least one element. This operation has a constant cost.
        /// </summary>
        public bool NonEmpty => Head != null;

        /// <summary>
        /// Indicates the number of elements contained within this list. O(N) operation time.
        /// </summary>
        public int Size => SystemMessageList.SizeInner(Head, 0);

        /// <summary>
        /// Gives back the list containing all the elements except the first. This operation has constant cost.
        ///
        /// ***Warning:*** as the underlying list nodes (the <see cref="SystemMessage"/> instances) are mutable, care
        /// should be taken when passing the tail to other methods. <see cref="SystemMessage.Unlink"/> should be
        /// called on the head if one wants to detach the tail permanently.
        /// </summary>
        public LatestFirstSystemMessageList Tail => new LatestFirstSystemMessageList(Head.Next);

        /// <summary>
        /// Reverses the list. This operation mutates the underlying list. The cost of the call is O(N), where N is the number of elements.
        /// 
        /// The type of the returned list ios the opposite order: <see cref="EarliestFirstSystemMessageList"/>.
        /// </summary>
        public EarliestFirstSystemMessageList Reverse => new EarliestFirstSystemMessageList(SystemMessageList.ReverseInner(Head, null));

        /// <summary>
        /// Attaches a message to the current head of the list. This operation has constant cost.
        /// </summary>
        /// <param name="list">The list being modified.</param>
        /// <param name="msg">The new item to add to the head of the list.</param>
        /// <returns>A new <see cref="LatestFirstSystemMessageList"/> with <paramref name="msg"/> appended to the front.</returns>
        public static LatestFirstSystemMessageList operator +(LatestFirstSystemMessageList list, SystemMessage msg)
        {
            Assert.Assert(msg != null);
            msg.Next = list.Head;
            return new LatestFirstSystemMessageList(msg);
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Value type supporting list operations on system messages. The `next` field of <see cref="SystemMessage"/>
    /// is hidden, and can only accessed through the value classes <see cref="LatestFirstSystemMessageList"/> and
    /// <see cref="EarliestFirstSystemMessageList"/>, abstracting over the fact that system messages are the
    /// list nodes themselves. If used properly, this stays a compile time construct without any allocation overhead.
    ///
    /// This list is mutable.
    ///
    /// The type of the list also encodes that the messages contained are in reverse order, i.e. the head of the list is the
    /// latest appended element.
    /// </summary>
    internal struct EarliestFirstSystemMessageList
    {
        /// <summary>
        /// The front of the list.
        /// </summary>
        public SystemMessage Head;

        /// <summary>
        /// Creates a new message list.
        /// </summary>
        /// <param name="head">The current head item.</param>
        public EarliestFirstSystemMessageList(SystemMessage head)
        {
            Head = head;
        }

        /// <summary>
        /// Indicates if the list is empty or not. This operation has constant cost.
        /// </summary>
        public bool IsEmpty => Head == null;

        /// <summary>
        /// Indicates if the list has at least one element. This operation has a constant cost.
        /// </summary>
        public bool NonEmpty => Head != null;

        /// <summary>
        /// Indicates the number of elements contained within this list. O(N) operation time.
        /// </summary>
        public int Size => SystemMessageList.SizeInner(Head, 0);

        /// <summary>
        /// Gives back the list containing all the elements except the first. This operation has constant cost.
        ///
        /// ***Warning:*** as the underlying list nodes (the <see cref="SystemMessage"/> instances) are mutable, care
        /// should be taken when passing the tail to other methods. <see cref="SystemMessage.Unlink"/> should be
        /// called on the head if one wants to detach the tail permanently.
        /// </summary>
        public EarliestFirstSystemMessageList Tail => new EarliestFirstSystemMessageList(Head.Next);

        /// <summary>
        /// Reverses the list. This operation mutates the underlying list. The cost of the call is O(N), where N is the number of elements.
        /// 
        /// The type of the returned list ios the opposite order: <see cref="LatestFirstSystemMessageList"/>.
        /// </summary>
        public LatestFirstSystemMessageList Reverse => new LatestFirstSystemMessageList(SystemMessageList.ReverseInner(Head, null));

        /// <summary>
        /// Attaches a message to the current head of the list. This operation has constant cost.
        /// </summary>
        /// <param name="list">The list being modified.</param>
        /// <param name="msg">The new item to add to the head of the list.</param>
        /// <returns>A new <see cref="LatestFirstSystemMessageList"/> with <paramref name="msg"/> appended to the front.</returns>
        public static EarliestFirstSystemMessageList operator +(EarliestFirstSystemMessageList list, SystemMessage msg)
        {
            Assert.Assert(msg != null);
            msg.Next = list.Head;
            return new EarliestFirstSystemMessageList(msg);
        }

        /// <summary>
        /// Prepends a list in a reversed order to the head of this list. The prepended list will be reversed during the process.
        /// </summary>
        /// <param name="list">The original list.</param>
        /// <param name="other">The list to be reversed and prepended.</param>
        /// <returns>A new list with <paramref name="other"/> reversed and prepended to the front of <paramref name="list"/>.</returns>
        /// <example>
        /// Example: (3, 4, 5) reversePrepend (2, 1, 0) == (0, 1, 2, 3, 4, 5)
        /// </example>
        /// <remarks>
        /// The cost of this operation is O(N) in the size of the list that is to be prepended.
        /// </remarks>
        public static EarliestFirstSystemMessageList operator +(
            EarliestFirstSystemMessageList list, LatestFirstSystemMessageList other)
        {
            var remaining = other;
            var result = list;
            while (remaining.NonEmpty)
            {
                var msg = remaining.Head;
                remaining = remaining.Tail;
                result = result + msg;
            }
            return result;
        }
    }



    /// <summary>
    /// INTERNAL API
    /// 
    /// Signals to Akka.NET actors that we need to wait until children
    /// have completed some operation (usually, shutting down) before we
    /// can process this stashed <see cref="ISystemMessage"/>.
    /// </summary>
    internal interface IStashWhenWaitingForChildren { }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Stash this <see cref="ISystemMessage"/> when the actor is in a failed state.
    /// </summary>
    internal interface IStashWhenFailed { }

    // public API

    //@SerialVersionUID(1L)
    //private[akka] case class Create(failure: Option[ActorInitializationException]) extends ISystemMessage // sent to self from Dispatcher.register

    /// <summary>
    ///     Class ISystemMessage.
    /// </summary>
    public interface ISystemMessage : INoSerializationVerificationNeeded
    {

    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// <see cref="ISystemMessage"/> is an interface and too basic to express
    /// all of the capabilities needed to express a full-fledged system message.
    /// </summary>
    [InternalApi]
    public abstract class SystemMessage : ISystemMessage
    {
        /// <summary>
        /// The next <see cref="ISystemMessage"/> in the linked list.
        /// </summary>
        [NonSerialized]
        internal SystemMessage Next;

        /// <summary>
        /// Unlinks this message from the linked list.
        /// </summary>
        public void Unlink()
        {
            Next = null;
        }

        /// <summary>
        /// Returns <c>true</c> if we are unlinked.
        /// </summary>
        public bool Unlinked { get { return Next == null; } }
    }

    /// <summary>
    ///  Switched into the mailbox to signal termination
    /// </summary>
    public sealed class NoMessage : SystemMessage
    {
        /// <inheritdoc cref="object"/>
        public override string ToString()
        {
            return "NoMessage";
        }
    }

    /// <summary>
    ///     Class DeathWatchNotification.
    /// </summary>
    public sealed class DeathWatchNotification : SystemMessage, IDeadLetterSuppression
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DeathWatchNotification" /> class.
        /// </summary>
        /// <param name="actor">The actor.</param>
        /// <param name="existenceConfirmed">if set to <c>true</c> [existence confirmed].</param>
        /// <param name="addressTerminated">if set to <c>true</c> [address terminated].</param>
        public DeathWatchNotification(IActorRef actor, bool existenceConfirmed, bool addressTerminated)
        {
            Actor = actor;
            ExistenceConfirmed = existenceConfirmed;
            AddressTerminated = addressTerminated;
        }

        /// <summary>
        ///     Gets the actor.
        /// </summary>
        /// <value>The actor.</value>
        public IActorRef Actor { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [existence confirmed].
        /// </summary>
        /// <value><c>true</c> if [existence confirmed]; otherwise, <c>false</c>.</value>
        public bool ExistenceConfirmed { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [address terminated].
        /// </summary>
        /// <value><c>true</c> if [address terminated]; otherwise, <c>false</c>.</value>
        public bool AddressTerminated { get; private set; }

        /// <inheritdoc cref="object"/>
        public override string ToString()
        {
            return "<DeathWatchNotification>: " + Actor + ", ExistenceConfirmed=" + ExistenceConfirmed + ", AddressTerminated=" + AddressTerminated;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class Failed : SystemMessage, IStashWhenFailed
    {
        private readonly long _uid;
        private readonly Exception _cause;
        private readonly IActorRef _child;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Failed" /> class.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        /// <param name="uid">The uid</param>
        public Failed(IActorRef child, Exception cause, long uid)
        {
            _uid = uid;
            _child = child;
            _cause = cause;
        }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <value>The child.</value>
        public IActorRef Child { get { return _child; } }

        /// <summary>
        ///     Gets the cause.
        /// </summary>
        /// <value>The cause.</value>
        public Exception Cause { get { return _cause; } }

        /// <summary>
        /// TBD
        /// </summary>
        public long Uid { get { return _uid; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Failed>: " + _child + " (" + _uid + ") " + (_cause != null ? ", Cause=" + _cause : "");
        }
    }

    /// <summary>
    ///     Class Supervise.
    /// </summary>
    public sealed class Supervise : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Supervise" /> class.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="async">if set to <c>true</c> [asynchronous].</param>
        public Supervise(IActorRef child, bool async)
        {
            Child = child;
            Async = async;
        }

        /// <summary>
        ///     Gets a value indicating whether this <see cref="Supervise" /> is asynchronous.
        /// </summary>
        /// <value><c>true</c> if asynchronous; otherwise, <c>false</c>.</value>
        public bool Async { get; private set; }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <value>The child.</value>
        public IActorRef Child { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Supervise>: " + Child + ", Async=" + Async;
        }
    }

    /// <summary>
    /// Creates a deathwatch subscription  between <see cref="Watchee"/> and <see cref="Watcher"/>.
    /// 
    /// <see cref="Watcher"/> will be notified via a <see cref="Terminated"/> message when <see cref="Watchee"/>
    /// is stopped. In the case of a remote actor references, a <see cref="Terminated"/> may also be produced in
    /// the event that the association between the two remote actor systems fails.
    /// </summary>
    public class Watch : SystemMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Watch" /> class.
        /// </summary>
        /// <param name="watchee">The watchee.</param>
        /// <param name="watcher">The watcher.</param>
        public Watch(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        /// <summary>
        /// Gets the watchee.
        /// </summary>
        /// <value>The watchee.</value>
        public IInternalActorRef Watchee { get; }

        /// <summary>
        /// Gets the watcher.
        /// </summary>
        /// <value>The watcher.</value>
        public IInternalActorRef Watcher { get; }

        protected bool Equals(Watch other)
        {
            return Equals(Watchee, other.Watchee) && Equals(Watcher, other.Watcher);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Watch)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Watchee?.GetHashCode() ?? 0) * 397) ^ (Watcher?.GetHashCode() ?? 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return $"<Watch>: {Watcher} wants to watch {Watchee}";
        }
    }

    /// <summary>
    /// Unsubscribes <see cref="Watcher"/> from any death watch notifications for <see cref="Watchee"/>.
    /// </summary>
    public sealed class Unwatch : SystemMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Unwatch" /> class.
        /// </summary>
        /// <param name="watchee">The watchee.</param>
        /// <param name="watcher">The watcher.</param>
        public Unwatch(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        /// <summary>
        /// Gets the watchee.
        /// </summary>
        /// <value>The watchee.</value>
        public IInternalActorRef Watchee { get; }

        /// <summary>
        /// Gets the watcher.
        /// </summary>
        /// <value>The watcher.</value>
        public IInternalActorRef Watcher { get; }

        private bool Equals(Unwatch other)
        {
            return Equals(Watchee, other.Watchee) && Equals(Watcher, other.Watcher);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Unwatch && Equals((Unwatch)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Watchee?.GetHashCode() ?? 0) * 397) ^ (Watcher?.GetHashCode() ?? 0);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return $"<Unwatch>: {Watcher} wants to unwatch {Watchee}";
        }
    }

    /// <summary>
    ///     Class ActorTask.
    /// </summary>
    public sealed class ActorTask : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorTask" /> class.
        /// </summary>
        /// <param name="task">The task.</param>
        public ActorTask(Task task)
        {
            Task = task;
        }

        /// <summary>
        ///     Gets the task.
        /// </summary>
        /// <value>The task.</value>
        public Task Task { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class ActorTaskSchedulerMessage : SystemMessage
    {
        private readonly ActorTaskScheduler _scheduler;
        private readonly Task _task;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorTaskSchedulerMessage" /> class.
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="task">TBD</param>
        /// <param name="message">TBD</param>
        public ActorTaskSchedulerMessage(ActorTaskScheduler scheduler, Task task, object message)
        {
            _scheduler = scheduler;
            _task = task;
            Message = message;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorTaskSchedulerMessage" /> class.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="message">The message causing the exception</param>
        public ActorTaskSchedulerMessage(Exception exception, object message)
        {
            Exception = exception;
            Message = message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Exception Exception { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public void ExecuteTask()
        {
            _scheduler.ExecuteTask(_task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<ActorTaskSchedulerMessage>";
        }
    }

    /// <summary>
    /// Sent to self from <see cref="ActorCell.Restart"/>
    /// </summary>
    public sealed class Recreate : SystemMessage, IStashWhenWaitingForChildren
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Recreate" /> class.
        /// </summary>
        /// <param name="cause">The cause.</param>
        public Recreate(Exception cause)
        {
            Cause = cause;
        }

        /// <summary>
        ///     Gets the cause.
        /// </summary>
        /// <value>The cause.</value>
        public Exception Cause { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Recreate>" + (Cause == null ? "" : " Cause: " + Cause);
        }
    }

    /// <summary>
    ///  Sent to self from <see cref="ActorCell.Resume"/>
    /// </summary>
    public sealed class Resume : SystemMessage, IStashWhenWaitingForChildren
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Resume" /> class.
        /// </summary>
        /// <param name="causedByFailure">The caused by failure.</param>
        public Resume(Exception causedByFailure)
        {
            CausedByFailure = causedByFailure;
        }

        /// <summary>
        ///     Gets or sets the caused by failure.
        /// </summary>
        /// <value>The caused by failure.</value>
        public Exception CausedByFailure { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Resume>" + (CausedByFailure == null ? "" : " CausedByFailure: " + CausedByFailure);
        }
    }

    /// <summary>
    ///  Sent to self from <see cref="ActorCell.Suspend"/>
    /// </summary>
    public sealed class Suspend : SystemMessage, IStashWhenWaitingForChildren
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Suspend>";
        }
    }

    /// <summary>
    ///     Class Stop.
    /// </summary>
    public sealed class Stop : SystemMessage
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Stop>";
        }
    }

    /// <summary>
    ///     INTERNAL
    /// </summary>
    public sealed class StopChild   //StopChild is NOT a ISystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StopChild" /> class.
        /// </summary>
        /// <param name="child">The child.</param>
        public StopChild(IActorRef child)
        {
            Child = child;
        }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <value>The child.</value>
        public IActorRef Child { get; private set; }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<StopChild> " + Child;
        }
    }

    /// <summary>
    ///     Class Escalate.
    /// </summary>
    public sealed class Escalate : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Escalate" /> class.
        /// </summary>
        /// <param name="reason">The reason.</param>
        public Escalate(Exception reason)
        {
            Reason = reason;
        }

        /// <summary>
        ///     Gets the reason.
        /// </summary>
        /// <value>The reason.</value>
        public Exception Reason { get; private set; }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Escalate>" + (Reason == null ? "" : " Reason: " + Reason);
        }
    }


    /// <summary>
    ///     Class Terminate.
    /// </summary>
    public sealed class Terminate : SystemMessage, IPossiblyHarmful, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<Terminate>";
        }
    }

    /// <summary>
    /// Sent to self from <see cref="MessageDispatcher.Register"/>
    /// </summary>
    public sealed class Create : SystemMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Create" /> class.
        /// </summary>
        /// <param name="failure">TBD</param>
        public Create(ActorInitializationException failure = null)
        {
            Failure = failure;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ActorInitializationException Failure { get; }

        private bool Equals(Create other)
        {
            return Equals(Failure, other.Failure);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Create && Equals((Create)obj);
        }

        public override int GetHashCode()
        {
            return Failure?.GetHashCode() ?? 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return $"<Create>{(Failure == null ? "" : " Failure: " + Failure)}";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class RegisterTerminationHook
    {
        private RegisterTerminationHook() { }
        private static readonly RegisterTerminationHook _instance = new RegisterTerminationHook();
        /// <summary>
        /// TBD
        /// </summary>
        public static RegisterTerminationHook Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<RegisterTerminationHook>";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class TerminationHook
    {
        private TerminationHook() { }
        private static readonly TerminationHook _instance = new TerminationHook();
        /// <summary>
        /// TBD
        /// </summary>
        public static TerminationHook Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<TerminationHook>";
        }
    }

    /// <summary>
    ///     Class Terminate.
    /// </summary>
    public sealed class TerminationHookDone
    {
        private TerminationHookDone() { }
        private static readonly TerminationHookDone _instance = new TerminationHookDone();
        /// <summary>
        /// TBD
        /// </summary>
        public static TerminationHookDone Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<TerminationHookDone>";
        }
    }
}
