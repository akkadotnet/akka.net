//-----------------------------------------------------------------------
// <copyright file="ISystemMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Dispatch.SysMsg
{
    /**
 * public API
 */
    //@SerialVersionUID(1L)
    //private[akka] case class Create(failure: Option[ActorInitializationException]) extends ISystemMessage // sent to self from Dispatcher.register
    /// <summary>
    ///     Class ISystemMessage.
    /// </summary>
    public interface ISystemMessage : INoSerializationVerificationNeeded
    {
    }

    /// <summary>
    ///     Class NoMessage.
    /// </summary>
    public sealed class NoMessage : ISystemMessage
    {
        public override string ToString()
        {
            return "NoMessage";
        }
    }

    /// <summary>
    ///     Class DeathWatchNotification.
    /// </summary>
    public sealed class DeathWatchNotification : ISystemMessage
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

        public override string ToString()
        {
            return "<DeathWatchNotification>: " + Actor + ", ExistenceConfirmed=" + ExistenceConfirmed + ", AddressTerminated=" + AddressTerminated;
        }
    }

    /// <summary>
    ///     Class Failed.
    /// </summary>
    public sealed class Failed : ISystemMessage
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

        public long Uid { get { return _uid; } }

        public override string ToString()
        {
            return "<Failed>: " + _child + " (" + _uid + ") " + (_cause != null ? ", Cause=" + _cause : "");
        }
    }

    /// <summary>
    ///     Class Supervise.
    /// </summary>
    public sealed class Supervise : ISystemMessage
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

        public override string ToString()
        {
            return "<Supervise>: " + Child + ", Async=" + Async;
        }
    }

    //used to start watching another actor (deathwatch)
    /// <summary>
    ///     Class Watch.
    /// </summary>
    public class Watch : ISystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Watch" /> class.
        /// </summary>
        /// <param name="watchee">The watchee.</param>
        /// <param name="watcher">The watcher.</param>
        public Watch(IActorRef watchee, IActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        /// <summary>
        ///     Gets the watchee.
        /// </summary>
        /// <value>The watchee.</value>
        public IActorRef Watchee { get; private set; }

        /// <summary>
        ///     Gets the watcher.
        /// </summary>
        /// <value>The watcher.</value>
        public IActorRef Watcher { get; private set; }

        public override string ToString()
        {
            return "<Watch>: " + Watcher + " wants to watch " + Watchee;
        }
    }

    //used to unsubscribe to deathwatch
    /// <summary>
    ///     Class Unwatch.
    /// </summary>
    public sealed class Unwatch : ISystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Unwatch" /> class.
        /// </summary>
        /// <param name="watchee">The watchee.</param>
        /// <param name="watcher">The watcher.</param>
        public Unwatch(IActorRef watchee, IActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        /// <summary>
        ///     Gets the watchee.
        /// </summary>
        /// <value>The watchee.</value>
        public IActorRef Watchee { get; private set; }

        /// <summary>
        ///     Gets the watcher.
        /// </summary>
        /// <value>The watcher.</value>
        public IActorRef Watcher { get; private set; }

        public override string ToString()
        {
            return "<Unwatch>: " + Watcher + " wants to unwatch " + Watchee;
        }
    }

    /// <summary>
    ///     Class ActorTask.
    /// </summary>
    public sealed class ActorTask : ISystemMessage
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

    internal sealed class ActorTaskSchedulerMessage : ISystemMessage
    {
        private readonly ActorTaskScheduler _scheduler;
        private readonly Task _task;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorTaskSchedulerMessage" /> class.
        /// </summary>
        public ActorTaskSchedulerMessage(ActorTaskScheduler scheduler, Task task)
        {
            _scheduler = scheduler;
            _task = task;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorTaskSchedulerMessage" /> class.
        /// </summary>
        /// <param name="exception">The exception.</param>
        public ActorTaskSchedulerMessage(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; private set; }

        public void ExecuteTask()
        {
            _scheduler.ExecuteTask(_task);
        }

        public override string ToString()
        {
            return "<ActorTaskSchedulerMessage>";
        }
    }

    /// <summary>
    ///     Class Restart.
    /// </summary>
    public sealed class Restart : ISystemMessage
    {
        private Restart() { }
        private static readonly Restart _instance = new Restart();
        public static Restart Instance
        {
            get
            {
                return _instance;
            }
        }
    }

    /// <summary>
    ///     Class Recreate.
    /// </summary>
    public sealed class Recreate : ISystemMessage
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

        public override string ToString()
        {
            return "<Recreate>" + (Cause == null ? "" : " Cause: " + Cause);
        }
    }

    /// <summary>
    ///     Class Resume.
    /// </summary>
    public sealed class Resume : ISystemMessage
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

        public override string ToString()
        {
            return "<Resume>" + (CausedByFailure == null ? "" : " CausedByFailure: " + CausedByFailure);
        }
    }

    /// <summary>
    ///     Class Suspend.
    /// </summary>
    public sealed class Suspend : ISystemMessage
    {
        private Suspend() { }
        private static readonly Suspend _instance = new Suspend();
        public static Suspend Instance
        {
            get
            {
                return _instance;
            }
        }

        public override string ToString()
        {
            return "<Suspend>";
        }
    }

    /// <summary>
    ///     Class Stop.
    /// </summary>
    public sealed class Stop : ISystemMessage
    {
        private Stop() { }
        private static readonly Stop _instance = new Stop();
        public static Stop Instance
        {
            get
            {
                return _instance;
            }
        }

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


        public override string ToString()
        {
            return "<StopChild> " + Child;
        }
    }

    /// <summary>
    ///     Class Escalate.
    /// </summary>
    public sealed class Escalate : ISystemMessage
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


        public override string ToString()
        {
            return "<Escalate>" + (Reason == null ? "" : " Reason: " + Reason);
        }
    }


    /// <summary>
    ///     Class Terminate.
    /// </summary>
    public sealed class Terminate : ISystemMessage
    {
        private Terminate() { }
        private static readonly Terminate _instance = new Terminate();
        public static Terminate Instance
        {
            get
            {
                return _instance;
            }
        }

        public override string ToString()
        {
            return "<Terminate>";
        }
    }

    public sealed class Create : ISystemMessage
    {
        private readonly ActorInitializationException _failure;

        public Create(ActorInitializationException failure = null)
        {
            _failure = failure;
        }

        public ActorInitializationException Failure
        {
            get { return _failure; }
        }

        public override string ToString()
        {
            return "<Create>" + (_failure == null ? "" : " Failure: " + _failure);
        }
    }

    public sealed class RegisterTerminationHook 
    {
        private RegisterTerminationHook() { }
        private static readonly RegisterTerminationHook _instance = new RegisterTerminationHook();
        public static RegisterTerminationHook Instance
        {
            get
            {
                return _instance;
            }
        }

        public override string ToString()
        {
            return "<RegisterTerminationHook>";
        }
    }

    public sealed class TerminationHook
    {
        private TerminationHook() { }
        private static readonly TerminationHook _instance = new TerminationHook();
        public static TerminationHook Instance
        {
            get
            {
                return _instance;
            }
        }

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
        public static TerminationHookDone Instance
        {
            get
            {
                return _instance;
            }
        }

        public override string ToString()
        {
            return "<TerminationHookDone>";
        }
    }
}

