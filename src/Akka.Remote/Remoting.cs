using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
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

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Remoting : RemoteTransport
    {
        private readonly LoggingAdapter log;
        private volatile IDictionary<string, HashSet<ProtocolTransportAddressPair>> _transportMapping;
        private volatile InternalActorRef _endpointManager;

        // This is effectively a write-once variable similar to a lazy val. The reason for not using a lazy val is exception
        // handling.
        private volatile HashSet<Address> _addresses;

        // This variable has the same semantics as the addresses variable, in the sense it is written once, and emulates
        // a lazy val
        private volatile Address _defaultAddress;

        private InternalActorRef _transportSupervisor;
        private EventPublisher _eventPublisher;

        public Remoting(ActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
            log = Logging.GetLogger(system, "remoting");
            _eventPublisher = new EventPublisher(system, log, Logging.LogLevelFor(provider.RemoteSettings.RemoteLifecycleEventsLogLevel));
            _transportSupervisor = system.ActorOf(Props.Create<TransportSupervisor>(), "transports");
        }

        #region RemoteTransport overrides

        public override void Start()
        {
            log.Info("Starting remoting");

            if (_endpointManager == null)
            {
                _endpointManager =
                System.SystemActorOf(
                    Props.Create(() => new EndpointManager(Provider.RemoteSettings, log)).WithDeploy(Deploy.Local),
                    EndpointManagerName);

                try
                {
                    var addressPromise = new TaskCompletionSource<IList<ProtocolTransportAddressPair>>();
                    _endpointManager.Tell(new EndpointManager.Listen(addressPromise));

                    addressPromise.Task.Wait(Provider.RemoteSettings.StartupTimeout);
                    var akkaProtocolTransports = addressPromise.Task.Result;

                    Addresses = new HashSet<Address>(akkaProtocolTransports.Select(a => a.Address));
                    //   this.transportMapping = akkaProtocolTransports
                    //       .ToDictionary(p => p.ProtocolTransport.Transport.SchemeIdentifier,);
                    IEnumerable<IGrouping<string, ProtocolTransportAddressPair>> tmp =
                        akkaProtocolTransports.GroupBy(t => t.ProtocolTransport.SchemeIdentifier);
                    _transportMapping = new Dictionary<string, HashSet<ProtocolTransportAddressPair>>();
                    foreach (var g in tmp)
                    {
                        var set = new HashSet<ProtocolTransportAddressPair>(g);
                        _transportMapping.Add(g.Key, set);
                    }

                    _defaultAddress = akkaProtocolTransports.Head().Address;
                    _addresses = new HashSet<Address>(akkaProtocolTransports.Select(x => x.Address));

                    log.Info("Remoting started; listening on addresses : [{0}]", string.Join(",", _addresses.Select(x => x.ToString())));

                    _endpointManager.Tell(new EndpointManager.StartupFinished());
                    _eventPublisher.NotifyListeners(new RemotingListenEvent(_addresses.ToList()));

                }
                catch (TaskCanceledException ex)
                {
                    NotifyError("Startup was cancelled due to timeout", ex);
                    throw;
                }
                catch (TimeoutException ex)
                {
                    NotifyError("Startup timed out", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    NotifyError("Startup failed", ex);
                    throw;
                }
            }
            else
            {
                log.Warn("Remoting was already started. Ignoring start attempt.");
            }
        }

        public override Task Shutdown()
        {
            if (_endpointManager == null)
            {
                log.Warn("Remoting is not running. Ignoring shutdown attempt");
                return Task.Run(() => { });
            }
            else
            {
                var timeout = Provider.RemoteSettings.ShutdownTimeout;
                Action finalize = () =>
                {
                    _eventPublisher.NotifyListeners(new RemotingShutdownEvent());
                    _endpointManager = null;
                };

                return _endpointManager.Ask<bool>(new EndpointManager.ShutdownAndFlush(), timeout).ContinueWith(result =>
                {
                    if (result.IsFaulted || result.IsCanceled) //Shutdown was not successful
                    {
                        NotifyError("Failure during shutdown of remoting", result.Exception);
                        finalize();
                    }
                    else
                    {
                        if (!result.Result)
                        {
                            log.Warn(
                                "Shutdown finished, but flushing might not have been successful and some messages might have been dropped. " +
                                "Increase akka.remote.flush-wait-on-shutdown to a larger value to avoid this.");
                        }
                        finalize();
                    }
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
            }
        }

        public override void Send(object message, ActorRef sender, RemoteActorRef recipient)
        {
            if (_endpointManager == null)
            {
                throw new RemoteTransportException("Attempted to send remote message but Remoting is not running.", null);
            }
            if (sender == null)
                sender = ActorRef.NoSender;

            _endpointManager.Tell(new EndpointManager.Send(message, recipient, sender), sender);
        }

        public override Task<bool> ManagementCommand(object cmd)
        {
            if (_endpointManager == null)
            {
                throw new RemoteTransportException("Attempted to send management command but Remoting is not running.", null);
            }

            return
                _endpointManager.Ask<EndpointManager.ManagementCommandAck>(new EndpointManager.ManagementCommand(cmd),
                    Provider.RemoteSettings.CommandAckTimeout)
                    .ContinueWith(result => result.Result.Status,
                        TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }

        public override Address LocalAddressForRemote(Address remote)
        {
            return LocalAddressForRemote(_transportMapping, remote);
        }

        public override void Quarantine(Address address, int? uid)
        {
            if (_endpointManager == null)
            {
                throw new RemoteTransportException(string.Format("Attempted to quarantine address {0} with uid {1} but Remoting is not running", address, uid), null);
            }

            _endpointManager.Tell(new EndpointManager.Quarantine(address, uid));
        }

        #endregion

        #region Internal methods

        private void NotifyError(string msg, Exception cause)
        {
            _eventPublisher.NotifyListeners(new RemotingErrorEvent(new RemoteTransportException(msg, cause)));
        }

        #endregion

        #region Static methods

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

    internal sealed class RegisterTransportActor : NoSerializationVerificationNeeded
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