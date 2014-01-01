using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ActorRefFactory
    {
        public virtual ActorSystem System { get; set; }

        public abstract ActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase;

        public abstract void Stop(ActorRef actor);
        
        public abstract ActorRef ActorSelection(string remoteActorPath);

        public abstract ActorRef Child(string name);
    }
}
