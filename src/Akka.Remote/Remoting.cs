using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class AddressUrlEncoder
    {
        /// <summary>
        /// URL-encodes an actor <see cref="Address"/>. Used when generating the names
        /// of some system remote actors.
        /// </summary>
        public static string Encode(Address address)
        {
            return HttpUtility.UrlEncode(address.ToString(), Encoding.UTF8);
        }
    }

    internal class Remoting : RemoteTransport
    {
        
        private readonly LoggingAdapter log;
        private IDictionary<string, HashSet<ProtocolTransportAddressPair>> transportMapping;

        public Remoting(ActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
            log = Logging.GetLogger(System, "remoting");
        }

        public InternalActorRef EndpointManager { get; private set; }

        public override void Start()
        {
            log.Info("Starting remoting");

            EndpointManager =
                System.SystemActorOf(
                    Props.Create(() => new EndpointManager(Provider.RemoteSettings, log)).WithDeploy(Deploy.Local),
                    EndpointManagerName);


            Task<object> task = EndpointManager.Ask(new EndpointManager.Listen(new TaskCompletionSource<IList<ProtocolTransportAddressPair>>()));
            //TODO: Aaron: Endpointmanager does not reply here //Roger
            if (!task.Wait(3000))
            {
                throw new RemoteTransportException("Transport loading timed out", null);
            }
            if (task.Result == null)
            {
                throw new RemoteTransportException("No transport drivers were loaded.", null);
            }
            var akkaProtocolTransports = (ProtocolTransportAddressPair[]) task.Result;

            Addresses = new HashSet<Address>(akkaProtocolTransports.Select(a => a.Address));
            //   this.transportMapping = akkaProtocolTransports
            //       .ToDictionary(p => p.ProtocolTransport.Transport.SchemeIdentifier,);
            IEnumerable<IGrouping<string, ProtocolTransportAddressPair>> tmp =
                akkaProtocolTransports.GroupBy(t => t.ProtocolTransport.SchemeIdentifier);
            transportMapping = new Dictionary<string, HashSet<ProtocolTransportAddressPair>>();
            foreach (var g in tmp)
            {
                var set = new HashSet<ProtocolTransportAddressPair>(g);
                transportMapping.Add(g.Key, set);
            }
        }

        public override Task Shutdown()
        {
            throw new NotImplementedException();
        }

        public override void Send(object message, ActorRef sender, RemoteActorRef recipient)
        {
            if (EndpointManager == null)
            {
                throw new RemotingException("Attempted to send remote message but Remoting is not running.", null);
            }
            if (sender == null)
                sender = ActorRef.NoSender;

            EndpointManager.Tell(new EndpointManager.Send(message, recipient, sender), sender);
        }

        public override Task<bool> ManagementCommand(object cmd)
        {
            return null;
        }

        public override Address LocalAddressForRemote(Address remote)
        {
            return LocalAddressForRemote(transportMapping, remote);
        }

        #region Static methods and properties

        public const string EndpointManagerName = "endpointManager";

        internal static Address LocalAddressForRemote(
            IDictionary<string, HashSet<ProtocolTransportAddressPair>> transportMapping, Address remote)
        {
            HashSet<ProtocolTransportAddressPair> transports;

            if (transportMapping.TryGetValue(remote.Protocol, out transports))
            {
                ProtocolTransportAddressPair[] responsibleTransports =
                    transports.Where(t => t.ProtocolTransport.IsResponsibleFor(remote)).ToArray();
                if (responsibleTransports.Length == 0)
                {
                    throw new RemoteTransportException(
                        "No transport is responsible for address:[" + remote + "] although protocol [" + remote.Protocol +
                        "] is available." +
                        " Make sure at least one transport is configured to be responsible for the address.",
                        null);
                }
                if (responsibleTransports.Length == 1)
                {
                    return responsibleTransports.First().Address;
                }
                throw new RemoteTransportException(
                    "Multiple transports are available for [" + remote + ": " +
                    string.Join(",", responsibleTransports.Select(t => t.ToString())) + "] " +
                    "Remoting cannot decide which transport to use to reach the remote system. Change your configuration " +
                    "so that only one transport is responsible for the address.",
                    null);
            }

            throw new RemoteTransportException(
                "No transport is loaded for protocol: [" + remote.Protocol + "], available protocols: [" +
                string.Join(",", transportMapping.Keys.Select(t => t.ToString())) + "]", null);
        }

        #endregion
    }

    public sealed class RegisterTransportActor
    {
        public RegisterTransportActor(Props props, string name)
        {
            Props = props;
            Name = name;
        }

        public Props Props { get; private set; }

        public string Name { get; private set; }
    }

    internal class TransportSupervisor : ActorBase
    {
        private readonly SupervisorStrategy _strategy = new OneForOneStrategy(-1, TimeSpan.MaxValue, exception => Directive.Restart);
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _strategy;
        }

        protected override void OnReceive(object message)
        {
            PatternMatch.Match(message)
                .With<RegisterTransportActor>(r =>
                {
                    /*
                     * TODO: need to add support for RemoteDispatcher here.
                     * See https://github.com/akka/akka/blob/master/akka-remote/src/main/scala/akka/remote/RemoteSettings.scala#L42 
                     * and https://github.com/akka/akka/blob/master/akka-remote/src/main/scala/akka/remote/Remoting.scala#L95
                     */
                    Sender.Tell(Context.ActorOf(r.Props.WithDeploy(Deploy.Local), r.Name));
                });
        }
    }
}