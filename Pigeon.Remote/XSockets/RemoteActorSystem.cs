using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class RemoteActorSystem : ActorSystem
    {
        public static ActorSystem Create(string name, int port)
        {
            return new RemoteActorSystem(name, port);
        }

        private int port;
        public RemoteActorSystem(string name, int port) : base(name)
        {
            this.port = port;
            CreateHost(name,port);
        }

        private void CreateHost(string name,int port)
        {
            RemoteHost.StartHost(this,port);
        }       

        public new void Dispose()
        {
            
        }

        protected override ActorRef GetRemoteRef(ActorCell actorCell, ActorPath actorPath)
        {
            return new RemoteActorRef(actorCell, actorPath,this.port);
        }

        public override string GetSystemName()
        {
            return string.Format("127.0.0.1:{0}", port);
        }
    }
}
