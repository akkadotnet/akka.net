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

    public class DeadLetterActorRef : ActorRef
    {
        private EventBus eventStream;
        private ActorPath path;
        public DeadLetterActorRef(ActorPath path, EventBus eventStream)
        {
            this.eventStream = eventStream;
            this.path = path;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
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
