using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class Remoting : RemoteTransport
    {
        public Remoting(ActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
        }

        public override void Startup()
        {
        }

        public override void Send(object message, Actor.ActorRef sender, RemoteActorRef recipient)
        {
        }

        public override Task<bool> ManagementCommand(object cmd)
        {
            return null;
        }

        public override Address LocalAddressForRemote(Actor.Address remote)
        {
            return null;
        }
    }
}
