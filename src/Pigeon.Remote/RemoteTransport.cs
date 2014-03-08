using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Remote
{
    public abstract class RemoteTransport
    {
        public RemoteTransport(ActorSystem system, RemoteActorRefProvider provider)
        {
            System = system;
            Provider = provider;
            Addresses = null;
        }

        public ActorSystem System { get; private set; }
        public RemoteActorRefProvider Provider { get; private set; }

        public ISet<Address> Addresses { get; protected set; }
        public Address DefaultAddress { get; protected set; }
        public bool useUntrustedMode { get; protected set; }
        public bool logRemoteLifeCycleEvents { get; protected set; }

        public abstract void Start();

        public abstract void Send(object message, ActorRef sender, RemoteActorRef recipient);

        public abstract Task<bool> ManagementCommand(object cmd);

        public abstract Address LocalAddressForRemote(Address remote);
    }
}