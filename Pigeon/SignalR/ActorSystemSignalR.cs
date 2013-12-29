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
            CreateClient(name, url);
        }

        protected void CreateClient(string name, string url)
        {
            PigeonClientSignalR.Start(name, url);
        }

        private void CreateHost(string name,string url)
        {
            PigeonHostSignalR.Start(name,"http://localhost:8080/");
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
