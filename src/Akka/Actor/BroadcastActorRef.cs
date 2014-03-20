using System.Collections.Concurrent;
using System.Linq;

namespace Akka.Actor
{
    public class BroadcastActorRef : ActorRef
    {
        private readonly ConcurrentDictionary<ActorRef, ActorRef> actors =
            new ConcurrentDictionary<ActorRef, ActorRef>();

        public BroadcastActorRef(params ActorRef[] actors)
        {
            foreach (ActorRef a in actors)
                this.actors.TryAdd(a, a);
        }

        public void Add(ActorRef actor)
        {
            actors.TryAdd(actor, actor);
        }

        internal void Remove(ActorRef actor)
        {
            ActorRef tmp;
            actors.TryRemove(actor, out tmp);
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            actors.Values.ToList().ForEach(a => a.Tell(message, sender));
        }
    }
}