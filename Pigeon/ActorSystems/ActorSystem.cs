using Pigeon.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon
{
    public class ActorSystem : IDisposable
    {
        private System.Collections.Concurrent.ConcurrentDictionary<string, ActorRef> actors = new System.Collections.Concurrent.ConcurrentDictionary<string, ActorRef>();
        protected ActorSystem()
        {
        }       

        public ActorRef GetActor<TActor>() where TActor : ActorBase, new()
        {
            var actorName = typeof(TActor).Name;
            if (actorName.EndsWith("Actor"))
                actorName = actorName.Substring(0, actorName.Length - 5);

            if (actors.ContainsKey(actorName))
            {
                return actors[actorName];
            }
            else
            {
                var actor = new LocalActorRef(new TActor());
                actors.TryAdd(actorName, actor);
                return actor;
            }
        }

        public void Dispose()
        {            
        }

        public ActorRef GetActor(string actorName)
        {
            return actors[actorName];
        }
    }
}
