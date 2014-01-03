using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorSelection
    {
    }

    public class BroadcastActorRef : ActorRef
    {
        private List<ActorRef> actors = new List<ActorRef>();
        public BroadcastActorRef(params ActorRef[] actors)
        {
            this.actors.AddRange(actors); 
        }

        public void Add(ActorRef actor)
        {
            this.actors.Add(actor);
        }

        public override void Tell(object message, ActorRef sender = null)
        {
            actors.ForEach(a => a.Tell(message, sender));
        }
    }
}
