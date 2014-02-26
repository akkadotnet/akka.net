using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
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
  override protected def specialHandle(msg: Any, sender: ActorRef): Boolean = msg match {
    case w: Watch ⇒
      if (w.watchee != this && w.watcher != this)
        w.watcher.sendSystemMessage(
          DeathWatchNotification(w.watchee, existenceConfirmed = false, addressTerminated = false))
      true
    case _ ⇒ super.specialHandle(msg, sender)
  }
} 
     */
    public class DeadLetterActorRef : ActorRef
    {
        private EventStream eventStream;
        private ActorPath path;
        private ActorRefProvider provider;
        public DeadLetterActorRef(ActorRefProvider provider, ActorPath path, EventStream eventStream)
        {
            this.eventStream = eventStream;
            this.path = path;
            this.provider = provider; 
        }
        private bool SpecialHandle(object message, ActorRef sender)
        {
            return false;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            message
                .Match()
                .With<Identify>(m => sender.Tell(new ActorIdentity(m.MessageId, this)))
                .With<DeadLetter>(m =>
                {
                    if (!SpecialHandle(m.Message, m.Sender))
                        eventStream.Publish(m);
                })
                .Default(m =>
                {
                    if (!SpecialHandle(message, sender))
                        eventStream.Publish(new DeadLetter(message, sender == ActorRef.NoSender ? provider.DeadLetters : sender, this));
                });
        }        
    }
}
