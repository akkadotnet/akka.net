using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public abstract class RemoteTransport
    {
        private RemoteActorRefProvider provider;
        private ActorSystem system;

        public ISet<Address> Addresses
        {
            get;
            protected set;
        }

        public RemoteTransport(ActorSystem system, RemoteActorRefProvider provider)
        {
            this.system = system;
            this.provider = provider;
        }

        public abstract void Startup();

        public abstract void Send(object message, ActorRef sender, BrokenRemoteActorRef recipient);

        public abstract Task<bool> ManagementCommand(object cmd);

        public abstract Address LocalAddressForRemote(Address remote);
        public Address DefaultAddress
        {
            get;
            protected set;
        }
        public bool useUntrustedMode
        {
            get;
            protected set;
        }
        public bool logRemoteLifeCycleEvents
        {
            get;
            protected set;
        }
    }
}
