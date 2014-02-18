using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Dispatch.SysMsg
{

 /**
 * public API
 */
//@SerialVersionUID(1L)
//private[akka] case class Create(failure: Option[ActorInitializationException]) extends SystemMessage // sent to self from Dispatcher.register
///**

    public abstract class SystemMessage : NoSerializationVerificationNeeded
    {
    }

    public class NoMessage : SystemMessage
    {
    }

    public class DeathWatchNotification : SystemMessage
    {
        public DeathWatchNotification(ActorRef actor,bool existenceConfirmed,bool addressTerminated)
        {
            this.Actor = actor;
            this.ExistenceConfirmed = existenceConfirmed;
            this.AddressTerminated = addressTerminated;
        }

        public ActorRef Actor { get;private set; }

        public bool ExistenceConfirmed { get;private set; }

        public bool AddressTerminated { get;private set; }
    }

    public class Failed : SystemMessage
    {
        public Failed(ActorRef child, Exception cause)
        {
            this.Child = child;
            this.Cause = cause;
        }
        public ActorRef Child { get;private set; }
        public Exception Cause { get; private set; }
    }

    public class Supervise : SystemMessage
    {
        public Supervise(ActorRef child,bool async)
        {
            this.Child = child;
            this.Async = async;
        }

        public bool Async { get;private set; }
        public ActorRef Child { get;private set; }
    }
    //used to start watching another actor (deathwatch)
    public class Watch : SystemMessage
    {
        public Watch(ActorRef watchee,ActorRef watcher)
        {
            this.Watchee = watchee;
            this.Watcher = watcher;
        }
        public ActorRef Watchee { get;private set; }
        public ActorRef Watcher { get;private set; }
    }

    //used to unsubscribe to deathwatch
    public class Unwatch : SystemMessage
    {
        public Unwatch(ActorRef watchee, ActorRef watcher)
        {
            this.Watchee = watchee;
            this.Watcher = watcher;
        }
        public ActorRef Watchee { get;private set; }
        public ActorRef Watcher { get;private set; }
    }

    public class CompleteFuture : SystemMessage
    {
        public CompleteFuture(Action action)
        {
            this.SetResult = action;
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
            this.Cause = cause;
        }

        public Exception Cause { get;private set; }
    }

    public class Resume : SystemMessage
    {
        public Resume(Exception causedByFailure)
        {
            this.CausedByFailure = causedByFailure;
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
            this.Child = child;
        }
        public LocalActorRef Child { get; private set; }
    }

    public class Escalate : SystemMessage
    {
        public Escalate(Exception reason)
        {
            this.Reason = reason;
        }
        public Exception Reason { get; private set; }
    }

    



    public class Terminate : SystemMessage
    {
    }

}
