using Pigeon.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actors;

namespace Pigeon
{
    public class ActorSystem : IDisposable
    {
        private System.Collections.Concurrent.ConcurrentDictionary<string, ActorBase> actors = new System.Collections.Concurrent.ConcurrentDictionary<string, ActorBase>();

        public ActorSystem()
        {
        }       

        public ActorRef GetActor<TActor>(ActorBase owner = null) where TActor : ActorBase
        {
            var actorName = typeof(TActor).Name;
            if (actorName.EndsWith("Actor"))
                actorName = actorName.Substring(0, actorName.Length - 5);

            return GetActor<TActor>(actorName,owner);
        }

        public ActorRef GetActor<TActor>(string actorName,ActorBase owner = null) where TActor : ActorBase
        {
            if (actors.ContainsKey(actorName))
            {
                return new LocalActorRef(actors[actorName]);
            }
            else
            {
                var actor = (ActorBase)Activator.CreateInstance(typeof(TActor), new object[] { new ActorStart{
                  System = this,
                  Name = actorName,
                }});
                actors.TryAdd(actorName, actor);

                var actorRef = new LocalActorRef(actor);
                
                if (owner != null)
                    actorRef.Owner = new LocalActorRef(owner);

                return actorRef;                
            }
        }
        public ActorRef GetRemoteActor(string remoteUrl, string remoteActor,ActorBase owner = null)
        {
            var actorRef = new RemoteActorRef(this, remoteUrl, remoteActor);
            if (owner != null)
                actorRef.Owner = new LocalActorRef(owner);

            return actorRef;
        }

        public ActorRef GetRemoteActor(string remoteActorPath,ActorBase owner = null)
        {
            if (string.IsNullOrWhiteSpace(remoteActorPath))
                return null;

            var parts = remoteActorPath.Split('|');
            string remoteUrl = parts[0];
            string remoteActor = parts[1];
            return this.GetRemoteActor(remoteUrl, remoteActor);
        }

        public void Dispose()
        {            
        }

        public ActorRef GetActor(string localActor, ActorBase owner = null)
        {
            var actorRef = new LocalActorRef(actors[localActor]);           
            if (owner != null)
                actorRef.Owner = new LocalActorRef(owner);

            return actorRef;
        }
    }
}