using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorSelection : BroadcastActorRef
    {
        public ActorSelection(ActorPath path, params ActorRef[] actors) :base(actors)
        {
            this.Path = path;
        }
    }

    
}
