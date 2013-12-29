using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.SignalR
{
    public class ActorSystemSignalR : ActorSystem
    {
        public static ActorSystem Create(string name, string url)
        {
            return new ActorSystemSignalR(name, url);
        }

        public ActorSystemSignalR(string name, string url)
        {
            CreateHost(name,url);
        }

        private void CreateHost(string name,string url)
        {
            PigeonHostSignalR.Start(name,url);
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
