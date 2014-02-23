using Pigeon.Actor;
using Pigeon.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public abstract class RemoteTransport
    {
        public ActorSystem System { get; private set; }
        public RemoteActorRefProvider Provider { get; private set; }

        public ISet<Address> Addresses
        {
            get;
            protected set;
        }

        public RemoteTransport(ActorSystem system, RemoteActorRefProvider provider)
        {
            this.System = system;
            this.Provider = provider;
            this.Addresses = null;
        }

        public abstract void Start();

        public abstract void Send(object message, ActorRef sender, RemoteActorRef recipient);

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
