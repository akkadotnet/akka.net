using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorContext
    {
        public ActorSystem System { get; set; }
        public string Name { get; set; }

        public ActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return this.System.ActorOf<TActor>(name, null);
        }

        public ActorRef ActorSelection(string remoteActorPath)
        {
            return this.System.ActorSelection(remoteActorPath, null);
        }

        public ActorRef Self { get; private set; }
        public ActorRef Parent { get;private set; }

        private IEnumerable<ActorRef> Children { get; set; }
        public ActorRef Child(string name)
        {
            return Children.Where(actorRef => actorRef.Name == name).FirstOrDefault();
        }
    }
}
