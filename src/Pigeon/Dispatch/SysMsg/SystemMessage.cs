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
    /// **
    public abstract class SystemMessage : NoSerializationVerificationNeeded
    {
    }

    public class NoMessage : SystemMessage
    {
    }

    public class DeathWatchNotification : SystemMessage
    {
        public DeathWatchNotification(ActorRef actor, bool existenceConfirmed, bool addressTerminated)
        {
            Actor = actor;
            ExistenceConfirmed = existenceConfirmed;
            AddressTerminated = addressTerminated;
        }

        public ActorRef Actor { get; private set; }

        public bool ExistenceConfirmed { get; private set; }

        public bool AddressTerminated { get; private set; }
    }

    public class Failed : SystemMessage
    {
        public Failed(ActorRef child, Exception cause)
        {
            Child = child;
            Cause = cause;
        }

        public ActorRef Child { get; private set; }
        public Exception Cause { get; private set; }
    }

    public class Supervise : SystemMessage
    {
        public Supervise(ActorRef child, bool async)
        {
            Child = child;
            Async = async;
        }

        public bool Async { get; private set; }
        public ActorRef Child { get; private set; }
    }

    //used to start watching another actor (deathwatch)
    public class Watch : SystemMessage
    {
        public Watch(ActorRef watchee, ActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        public ActorRef Watchee { get; private set; }
        public ActorRef Watcher { get; private set; }
    }

    //used to unsubscribe to deathwatch
    public class Unwatch : SystemMessage
    {
        public Unwatch(ActorRef watchee, ActorRef watcher)
        {
            Watchee = watchee;
            Watcher = watcher;
        }

        public ActorRef Watchee { get; private set; }
        public ActorRef Watcher { get; private set; }
    }

    public class ActorTask : SystemMessage
    {
        public ActorTask(Task task)
        {
            Task = task;
        }

        public Task Task { get; private set; }
    }

    public class CompleteFuture : SystemMessage
    {
        public CompleteFuture(Action action)
        {
            SetResult = action;
        }

        public Action SetResult { get; private set; }
    }

    public class Restart : SystemMessage
    {
    }

    public class Recreate : SystemMessage
    {
        public Recreate(Exception cause)
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }
    }

    public class Resume : SystemMessage
    {
        public Resume(Exception causedByFailure)
        {
            CausedByFailure = causedByFailure;
        }

        public Exception CausedByFailure { get; set; }
    }

    public class Suspend : SystemMessage
    {
    }

    public class Stop : SystemMessage
    {
    }

    public class StopChild : SystemMessage
    {
        public StopChild(LocalActorRef child)
        {
            Child = child;
        }

        public LocalActorRef Child { get; private set; }
    }

    public class Escalate : SystemMessage
    {
        public Escalate(Exception reason)
        {
            Reason = reason;
        }

        public Exception Reason { get; private set; }
    }


    public class Terminate : SystemMessage
    {
    }
}