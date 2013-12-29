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
        protected ActorSystem()
        {
        }

        public ActorRef GetActor(string system, string actor)
        {
            return null;
        }

        public ActorRef GetActor<TActor>() where TActor : ActorBase, new()
        {
            return new LocalActorRef(new TActor());
        }

        public void Dispose()
        {            
        }
    }
}
