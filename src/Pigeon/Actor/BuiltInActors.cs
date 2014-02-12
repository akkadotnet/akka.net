using Pigeon.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class EventStreamActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
        }
    }

    public class GuardianActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Unhandled(message);
        }
    }

    /*
private[akka] class DeadLetterActorRef(_provider: ActorRefProvider,
                                       _path: ActorPath,
                                       _eventStream: EventStream) extends EmptyLocalActorRef(_provider, _path, _eventStream) {

  override def !(message: Any)(implicit sender: ActorRef = this): Unit = message match {
    case null                ⇒ throw new InvalidMessageException("Message is null")
    case Identify(messageId) ⇒ sender ! ActorIdentity(messageId, Some(this))
    case d: DeadLetter       ⇒ if (!specialHandle(d.message, d.sender)) eventStream.publish(d)
    case _ ⇒ if (!specialHandle(message, sender))
      eventStream.publish(DeadLetter(message, if (sender eq Actor.noSender) provider.deadLetters else sender, this))
  }

  override protected def specialHandle(msg: Any, sender: ActorRef): Boolean = msg match {
    case w: Watch ⇒
      if (w.watchee != this && w.watcher != this)
        w.watcher.sendSystemMessage(
          DeathWatchNotification(w.watchee, existenceConfirmed = false, addressTerminated = false))
      true
    case _ ⇒ super.specialHandle(msg, sender)
  }

  @throws(classOf[java.io.ObjectStreamException])
  override protected def writeReplace(): AnyRef = DeadLetterActorRef.serialized
} 
     */
    public class DeadLetterActorRef : ActorRef
    {
        private EventStream eventStream;
        private ActorPath path;
        public DeadLetterActorRef(ActorPath path, EventStream eventStream)
        {
            this.eventStream = eventStream;
            this.path = path;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            //TODO: need to special handle some messages here
            //e.g. identity,
            eventStream.Publish(new DeadLetter(message, sender, this));
        }

        public override void Resume(Exception causedByFailure = null)
        {
        }

        public override void Stop()
        {
        }
    }
}
