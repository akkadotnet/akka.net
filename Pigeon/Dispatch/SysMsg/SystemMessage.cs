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
// */
//@SerialVersionUID(1L)
//private[akka] case class Supervise(child: ActorRef, async: Boolean) extends SystemMessage // sent to supervisor ActorRef from ActorCell.start
///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Watch(watchee: InternalActorRef, watcher: InternalActorRef) extends SystemMessage // sent to establish a DeathWatch
///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Unwatch(watchee: ActorRef, watcher: ActorRef) extends SystemMessage // sent to tear down a DeathWatch
///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case object NoMessage extends SystemMessage // switched into the mailbox to signal termination

///**
// * INTERNAL API
// */
//@SerialVersionUID(1L)
//private[akka] case class Failed(child: ActorRef, cause: Throwable, uid: Int) extends SystemMessage
//  with StashWhenFailed
//  with StashWhenWaitingForChildren

//@SerialVersionUID(1L)
//private[akka] case class DeathWatchNotification(
//  actor: ActorRef,
//  existenceConfirmed: Boolean,
//  addressTerminated: Boolean) extends SystemMessage

    public abstract class SystemMessage
    {
    }

    public class SuperviceChild : SystemMessage
    {
        public SuperviceChild(Exception reason)
        {
            this.Reason = reason;
        }

        public Exception Reason { get; private set; }
    }

    public class CompleteFuture : SystemMessage
    {
        public CompleteFuture(Action action)
        {
            this.SetResult = action;
        }
        public Action SetResult { get; private set; }
    }

    public class Ping : SystemMessage
    {
        public DateTime LocalUtcNow { get; set; }
    }

    public class Pong : SystemMessage
    {
        public DateTime LocalUtcNow { get; set; }
        public DateTime RemoteUtcNow { get; set; }
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

    //used to start watching another actor (deathwatch)
    public class Watch : SystemMessage
    {
    }

    //used to unsubscribe to deathwatch
    public class Unwatch : SystemMessage
    {
    }
}
