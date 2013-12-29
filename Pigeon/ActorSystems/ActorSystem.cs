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
        private System.Collections.Concurrent.ConcurrentDictionary<string, ActorRef> actors = new System.Collections.Concurrent.ConcurrentDictionary<string, ActorRef>();
        public string Url { get; private set; }
        protected ActorSystem(string url)
        {
            this.Url = url;
        }       

        public ActorRef GetActor<TActor>() where TActor : ActorBase
        {
            var actorName = typeof(TActor).Name;
            if (actorName.EndsWith("Actor"))
                actorName = actorName.Substring(0, actorName.Length - 5);

            return GetActor<TActor>(actorName);
        }

        public ActorRef GetActor<TActor>(string actorName) where TActor : ActorBase
        {
            if (actors.ContainsKey(actorName))
            {
                return actors[actorName];
            }
            else
            {
                var actor = (ActorBase)Activator.CreateInstance(typeof(TActor), new object[] { this });
                var actorref = new LocalActorRef(actor);
                actors.TryAdd(actorName, actorref);
                return actorref;
            }
        }
        public ActorRef GetRemoteActor(string remoteUrl, string remoteActor)
        {
            return new RemoteActorRef(this, remoteUrl, remoteActor);
        }

        public ActorRef GetRemoteActor(string remoteActorPath)
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

        public ActorRef GetActor(string localActor)
        {
            return actors[localActor];
        }
    }
}