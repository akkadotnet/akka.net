using Pigeon.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;

namespace Pigeon.Actor
{
    public class ActorSystem : IDisposable
    {
        private System.Collections.Concurrent.ConcurrentDictionary<string, ActorBase> actors = new System.Collections.Concurrent.ConcurrentDictionary<string, ActorBase>();

        public ActorSystem()
        {
        }              

        public ActorRef ActorOf<TActor>(string name = null,ActorBase owner = null) where TActor : ActorBase
        {
            name = name ?? typeof(TActor).Name;
            if (name.EndsWith("Actor"))
                name = name.Substring(0, name.Length - 5);

            if (actors.ContainsKey(name))
            {
                return new LocalActorRef(actors[name]);
            }
            else
            {
                var actor = (ActorBase)Activator.CreateInstance(typeof(TActor), new object[] { new ActorContext{
                  System = this,
                  Name = name,
                }});
                actors.TryAdd(name, actor);

                var actorRef = new LocalActorRef(actor);
                
                if (owner != null)
                    actorRef.Owner = new LocalActorRef(owner);

                return actorRef;                
            }
        }

        public ActorRef ActorOf(string localActor, ActorBase owner = null)
        {
            var actorRef = new LocalActorRef(actors[localActor]);
            if (owner != null)
                actorRef.Owner = new LocalActorRef(owner);

            return actorRef;
        }

        public ActorRef ActorSelection(string remoteUrl, string remoteActor,ActorBase owner = null)
        {
            var actorRef = new RemoteActorRef(this, remoteUrl, remoteActor);
            if (owner != null)
                actorRef.Owner = new LocalActorRef(owner);

            return actorRef;
        }

        public ActorRef ActorSelection(string remoteActorPath,ActorBase owner = null)
        {
            if (string.IsNullOrWhiteSpace(remoteActorPath))
                return null;

            var parts = remoteActorPath.Split('|');
            string remoteUrl = parts[0];
            string remoteActor = parts[1];
            return this.ActorSelection(remoteUrl, remoteActor);
        }

        public void Dispose()
        {            
        }       
    }
}