using Pigeon.Actor;
using Pigeon.Event;
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
            log = Logging.GetLogger(this.System, "remoting");
        }

        private LoggingAdapter log;
        public override void Start()
        {
            log.Info("Starting remoting");

            this.EndpointManager = System.SystemActorOf(Props.Create(() => new EndpointManager(Provider.RemoteSettings.Config, log)).WithDeploy(Deploy.Local), Remoting.EndpointManagerName);


            var task = EndpointManager.Ask(new Listen());
            if (!task.Wait(3000) || task.Result == null)
            {
                throw new RemoteTransportException("No transport drivers were loaded.", null);
            }
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
                "No transport is responsible for address:[" + remote + "] although protocol 8" + remote.Protocol + "] is available." +
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
              "Multiple transports are available for [" + remote + ": " + string.Join(",",responsibleTransports.Select(t => t.ToString()))  + "] " +
                "Remoting cannot decide which transport to use to reach the remote system. Change your configuration " +
                "so that only one transport is responsible for the address.",
              null);
                }
            }

            throw new RemoteTransportException(
        "No transport is loaded for protocol: [" + remote.Protocol + "], available protocols: [" + string.Join(",", transportMapping.Keys.Select(t => t.ToString())) + "]", null);
        }

        public static readonly string EndpointManagerName = "endpointManager";

        public InternalActorRef EndpointManager { get;private set; }
    }
}
