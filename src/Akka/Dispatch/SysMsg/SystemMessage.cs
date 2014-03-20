using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Dispatch.SysMsg
{
    /**
 * public API
 */
//@SerialVersionUID(1L)
//private[akka] case class Create(failure: Option[ActorInitializationException]) extends SystemMessage // sent to self from Dispatcher.register
    /// <summary>
    ///     Class SystemMessage.
    /// </summary>
    /// **
    public abstract class SystemMessage : NoSerializationVerificationNeeded
    {
    }

    /// <summary>
    ///     Class NoMessage.
    /// </summary>
    public sealed class NoMessage : SystemMessage
    {
    }

    /// <summary>
    ///     Class DeathWatchNotification.
    /// </summary>
    public sealed class DeathWatchNotification : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DeathWatchNotification" /> class.
        /// </summary>
        /// <param name="actor">The actor.</param>
        /// <param name="existenceConfirmed">if set to <c>true</c> [existence confirmed].</param>
        /// <param name="addressTerminated">if set to <c>true</c> [address terminated].</param>
        public DeathWatchNotification(ActorRef actor, bool existenceConfirmed, bool addressTerminated)
        {
            Actor = actor;
            ExistenceConfirmed = existenceConfirmed;
            AddressTerminated = addressTerminated;
        }

        /// <summary>
        ///     Gets the actor.
        /// </summary>
        /// <value>The actor.</value>
        public ActorRef Actor { get; private set; }

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
    }

    /// <summary>
    ///     Class Failed.
    /// </summary>
    public sealed class Failed : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Failed" /> class.
        /// </summary>
        /// <param name="child">The child.</param>
        /// <param name="cause">The cause.</param>
        public Failed(ActorRef child, Exception cause)
        {
            Child = child;
            Cause = cause;
        }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <value>The child.</value>
        public ActorRef Child { get; private set; }

        /// <summary>
        ///     Gets the cause.
        /// </summary>
        /// <value>The cause.</value>
        public Exception Cause { get; private set; }
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
        public Supervise(ActorRef child, bool async)
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
        public ActorRef Child { get; private set; }
    }

    //used to start watching another actor (deathwatch)
    /// <summary>
    ///     Class Watch.
    /// </summary>
    public sealed class Watch : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Watch" /> class.
        /// </summary>
        /// <param name="watchee">The watchee.</param>
        /// <param name="watcher">The watcher.</param>
        public Watch(ActorRef watchee, ActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        /// <summary>
        ///     Gets the watchee.
        /// </summary>
        /// <value>The watchee.</value>
        public ActorRef Watchee { get; private set; }

        /// <summary>
        ///     Gets the watcher.
        /// </summary>
        /// <value>The watcher.</value>
        public ActorRef Watcher { get; private set; }
    }

    //used to unsubscribe to deathwatch
    /// <summary>
    ///     Class Unwatch.
    /// </summary>
    public sealed class Unwatch : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Unwatch" /> class.
        /// </summary>
        /// <param name="watchee">The watchee.</param>
        /// <param name="watcher">The watcher.</param>
        public Unwatch(ActorRef watchee, ActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        /// <summary>
        ///     Gets the watchee.
        /// </summary>
        /// <value>The watchee.</value>
        public ActorRef Watchee { get; private set; }

        /// <summary>
        ///     Gets the watcher.
        /// </summary>
        /// <value>The watcher.</value>
        public ActorRef Watcher { get; private set; }
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
    ///     Class CompleteFuture.
    /// </summary>
    public sealed class CompleteFuture : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="CompleteFuture" /> class.
        /// </summary>
        /// <param name="action">The action.</param>
        public CompleteFuture(Action action)
        {
            SetResult = action;
        }

        /// <summary>
        ///     Gets the set result.
        /// </summary>
        /// <value>The set result.</value>
        public Action SetResult { get; private set; }
    }

    /// <summary>
    ///     Class Restart.
    /// </summary>
    public sealed class Restart : SystemMessage
    {
    }

    /// <summary>
    ///     Class Recreate.
    /// </summary>
    public sealed class Recreate : SystemMessage
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
    }

    /// <summary>
    ///     Class Resume.
    /// </summary>
    public sealed class Resume : SystemMessage
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
    }

    /// <summary>
    ///     Class Suspend.
    /// </summary>
    public sealed class Suspend : SystemMessage
    {
    }

    /// <summary>
    ///     Class Stop.
    /// </summary>
    public sealed class Stop : SystemMessage
    {
    }

    /// <summary>
    ///     Class StopChild.
    /// </summary>
    public sealed class StopChild : SystemMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StopChild" /> class.
        /// </summary>
        /// <param name="child">The child.</param>
        public StopChild(LocalActorRef child)
        {
            Child = child;
        }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <value>The child.</value>
        public LocalActorRef Child { get; private set; }
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
    }


    /// <summary>
    ///     Class Terminate.
    /// </summary>
    public sealed class Terminate : SystemMessage
    {
    }
}