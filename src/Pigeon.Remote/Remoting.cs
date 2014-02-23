using Pigeon.Actor;
using Pigeon.Remote.Transport;
using Pigeon.Tools;
using System;
using System.Collections.Concurrent;
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

        private IDictionary<string, ConcurrentSet<ProtocolTransportAddressPair>> transportMapping = new ConcurrentDictionary<string, ConcurrentSet<ProtocolTransportAddressPair>>();
        public override Address LocalAddressForRemote(Address remote)
        {
            return LocalAddressForRemote(transportMapping,remote);
        }
        private Address LocalAddressForRemote(IDictionary<string, ConcurrentSet<ProtocolTransportAddressPair>> transportMapping, Address remote)
        {
            ConcurrentSet<ProtocolTransportAddressPair> transports;

            if (transportMapping.TryGetValue(remote.Protocol, out transports))
            {
                var responsibleTransports = transports.Where(t => t.ProtocolTransport.IsResponsibleFor(remote)).ToArray();
                if (responsibleTransports.Length == 0)
                {
                    throw new RemoteTransportException(
                "No transport is responsible for address:" + remote + " although protocol " + remote.Protocol + " is available." +
                " Make sure at least one transport is configured to be responsible for the address.",
              null);
                }
                else if (responsibleTransports.Length == 1)
                {
                    return responsibleTransports.First().Address;
                }
                else
                {
                    throw new RemoteTransportException(
              "Multiple transports are available for " + remote + ": " + string.Join(",",responsibleTransports.Select(t => t.ToString()))  + " " +
                "Remoting cannot decide which transport to use to reach the remote system. Change your configuration " +
                "so that only one transport is responsible for the address.",
              null);
                }
            }

            throw new RemoteTransportException(
        "No transport is loaded for protocol: " + remote + ", available protocols: [" + string.Join(",", transportMapping.Keys.Select(t => t.ToString())) + "]", null);
        }
    }
}
