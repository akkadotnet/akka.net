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
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Recreate(cause: Throwable) extends SystemMessage with StashWhenWaitingForChildren // sent to self from ActorCell.restart
///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Suspend() extends SystemMessage with StashWhenWaitingForChildren // sent to self from ActorCell.suspend
///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Resume(causedByFailure: Throwable) extends SystemMessage with StashWhenWaitingForChildren // sent to self from ActorCell.resume
///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Terminate() extends SystemMessage // sent to self from ActorCell.stop
///**
// * INTERNAL API


//@SerialVersionUID(1L)
//private[akka] case class DeathWatchNotification(
//  actor: ActorRef,
//  existenceConfirmed: Boolean,
//  addressTerminated: Boolean) extends SystemMessage

    public abstract class SystemMessage
    {
    }

    public class NoMessage : SystemMessage
    {
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

    public class PoisonPill : SystemMessage
    {
    }
    public class Kill : SystemMessage
    {
    }

    public class Restart : SystemMessage
    {
    }

    public class RestartChild : SystemMessage
    {
        public RestartChild(LocalActorRef child)
        {
            this.Child = child;
        }
        public LocalActorRef Child { get; private set; }
    }

    public class Resume : SystemMessage
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

    public class UnhandledMessage : SystemMessage
    {
        public UnhandledMessage(object message, ActorRef sender, ActorRef recipient)
        {
            this.Message = message;
            this.Sender = sender;
            this.Recipient = recipient;
        }

        public object Message { get; private set; }
        public ActorRef Sender { get; private set; }
        public ActorRef Recipient { get; private set; }
    }

    public class Terminated : SystemMessage
    {
    }

    //request to an actor ref, to get back the identity of the underlying actors
    public class Identity : SystemMessage
    {
        public Identity(Guid messageId)
        {
            MessageId = messageId;
        }
        public Guid MessageId { get; private set; }
    }

    //response to the Identity message, get identity by Sender
    public class ActorIdentity : SystemMessage
    {
        public Guid MessageId { get; private set; }
        public LocalActorRef Subject { get; private set; }

        public ActorIdentity(Guid messageId, LocalActorRef subject)
        {
            // TODO: Complete member initialization
            this.MessageId = messageId;
            this.Subject = subject;
        }
    }


}
