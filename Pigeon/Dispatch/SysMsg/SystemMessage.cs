using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Dispatch.SysMsg
{

 /**
 * INTERNAL API
 */
//@SerialVersionUID(1L)
//private[akka] case class Create(failure: Option[ActorInitializationException]) extends SystemMessage // sent to self from Dispatcher.register
///**

    internal abstract class SystemMessage
    {
    }

    internal class NoMessage : SystemMessage
    {
    }

    internal class DeathWatchNotification : SystemMessage
    {
        internal DeathWatchNotification(ActorRef actor,bool existenceConfirmed,bool addressTerminated)
        {
            this.Actor = actor;
            this.ExistenceConfirmed = existenceConfirmed;
            this.AddressTerminated = addressTerminated;
        }

        internal ActorRef Actor { get;private set; }

        internal bool ExistenceConfirmed { get;private set; }

        internal bool AddressTerminated { get;private set; }
    }

    internal class Failed : SystemMessage
    {
        internal Failed(ActorRef child, Exception cause)
        {
            this.Child = child;
            this.Cause = cause;
        }
        internal ActorRef Child { get;private set; }
        internal Exception Cause { get; private set; }
    }

    internal class Supervise : SystemMessage
    {
        internal Supervise(ActorRef child,bool async)
        {
            this.Child = child;
            this.Async = async;
        }

        internal bool Async { get;private set; }
        internal ActorRef Child { get;private set; }
    }
    //used to start watching another actor (deathwatch)
    internal class Watch : SystemMessage
    {
        internal Watch(ActorRef watchee,ActorRef watcher)
        {
            this.Watchee = watchee;
            this.Watcher = watcher;
        }
        internal ActorRef Watchee { get;private set; }
        internal ActorRef Watcher { get;private set; }
    }

    //used to unsubscribe to deathwatch
    internal class Unwatch : SystemMessage
    {
        internal Unwatch(ActorRef watchee, ActorRef watcher)
        {
            this.Watchee = watchee;
            this.Watcher = watcher;
        }
        internal ActorRef Watchee { get;private set; }
        internal ActorRef Watcher { get;private set; }
    }

    internal class CompleteFuture : SystemMessage
    {
        internal CompleteFuture(Action action)
        {
            this.SetResult = action;
        }
        internal Action SetResult { get; private set; }
    }

    internal class Restart : SystemMessage
    {
    }

    internal class Recreate : SystemMessage
    {
        internal Recreate(Exception cause)
        {
            this.Cause = cause;
        }

        internal Exception Cause { get;private set; }
    }

    internal class Resume : SystemMessage
    {
        internal Resume(Exception causedByFailure)
        {
            this.CausedByFailure = causedByFailure;
        }

        internal Exception CausedByFailure { get; set; }
    }

    internal class Suspend : SystemMessage
    {
    }

    internal class Stop : SystemMessage
    {
    }

    internal class StopChild : SystemMessage
    {
        internal StopChild(LocalActorRef child)
        {
            this.Child = child;
        }
        internal LocalActorRef Child { get; private set; }
    }

    internal class Escalate : SystemMessage
    {
        internal Escalate(Exception reason)
        {
            this.Reason = reason;
        }
        internal Exception Reason { get; private set; }
    }

    internal class UnhandledMessage : SystemMessage
    {
        internal UnhandledMessage(object message, ActorRef sender, ActorRef recipient)
        {
            this.Message = message;
            this.Sender = sender;
            this.Recipient = recipient;
        }

        internal object Message { get; private set; }
        internal ActorRef Sender { get; private set; }
        internal ActorRef Recipient { get; private set; }
    }



    internal class Terminate : SystemMessage
    {
    }

}
