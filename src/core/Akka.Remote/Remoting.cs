//-----------------------------------------------------------------------
// <copyright file="Remoting.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Util.Internal;

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
    /// 
    /// (used for forcing all /system level remoting actors onto a dedicated dispatcher)
    /// </summary>
// ReSharper disable once InconsistentNaming
    internal sealed class RARP : ExtensionIdProvider<RARP>,  IExtension
    {
        //this is why this extension is called "RARP"
        private readonly RemoteActorRefProvider _provider;

        /// <summary>
        /// Used as part of the <see cref="ExtensionIdProvider{RARP}"/>
        /// </summary>
        public RARP() { }

        private RARP(RemoteActorRefProvider provider)
        {
            _provider = provider;
        }

        public Props ConfigureDispatcher(Props props)
        {
            return _provider.RemoteSettings.ConfigureDispatcher(props);
        }

        public override RARP CreateExtension(ExtendedActorSystem system)
        {
            return new RARP(system.Provider.AsInstanceOf<RemoteActorRefProvider>());
        }

        public RemoteActorRefProvider Provider
        {
            get { return _provider; }
        }

        #region Static methods

        public static RARP For(ActorSystem system)
        {
            return system.WithExtension<RARP, RARP>();
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// Messages marked with this interface will be sent before other messages when buffering is active.
    /// This means that these messages don't obey normal message ordering.
    /// It is used for failure detector heartbeat messages.
    /// </summary>
    internal interface IPriorityMessage { }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class Remoting : RemoteTransport
    {
        private readonly ILoggingAdapter _log;
        private volatile IDictionary<string, HashSet<ProtocolTransportAddressPair>> _transportMapping;
        private volatile IActorRef _endpointManager;

        // This is effectively a write-once variable similar to a lazy val. The reason for not using a lazy val is exception
        // handling.
        private volatile HashSet<Address> _addresses;

        // This variable has the same semantics as the addresses variable, in the sense it is written once, and emulates
        // a lazy val
        private volatile Address _defaultAddress;

        private IActorRef _transportSupervisor;
        private readonly EventPublisher _eventPublisher;

        public Remoting(ExtendedActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
            _log = Logging.GetLogger(system, "remoting");
            _eventPublisher = new EventPublisher(system, _log, Logging.LogLevelFor(provider.RemoteSettings.RemoteLifecycleEventsLogLevel));
            _transportSupervisor = system.SystemActorOf(Props.Create<TransportSupervisor>(), "transports");
        }

        #region RemoteTransport overrides

        public override ISet<Address> Addresses
        {
            get { return _addresses; }
        }

        public override Address DefaultAddress
        {
            get { return _defaultAddress; }
        }

        /// <summary>
        /// Start assumes that it cannot be followed by another Start() without having a Shutdown() first
        /// </summary>
        public override void Start()
        {
            if (_endpointManager == null)
            {
                _log.Info("Starting remoting");
                _endpointManager =
                System.SystemActorOf(RARP.For(System).ConfigureDispatcher(
                    Props.Create(() => new EndpointManager(System.Settings.Config, _log)).WithDeploy(Deploy.Local)),
                    EndpointManagerName);

                try
                {
                    var addressPromise = new TaskCompletionSource<IList<ProtocolTransportAddressPair>>();

                    // tells the EndpointManager to start all transports and bind them to listenable addresses, and then set the results
                    // of this promise to include them.
                    _endpointManager.Tell(new EndpointManager.Listen(addressPromise)); 

                    addressPromise.Task.Wait(Provider.RemoteSettings.StartupTimeout);
                    var akkaProtocolTransports = addressPromise.Task.Result;
                    if(akkaProtocolTransports.Count==0)
                        throw new ConfigurationException(@"No transports enabled under ""akka.remote.enabled-transports""");
                    _addresses = new HashSet<Address>(akkaProtocolTransports.Select(a => a.Address));

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

                    _log.Info("Remoting started; listening on addresses : [{0}]", string.Join(",", _addresses.Select(x => x.ToString())));

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
                _log.Warning("Remoting was already started. Ignoring start attempt.");
            }
        }

        public override Task Shutdown()
        {
            if (_endpointManager == null)
            {
                _log.Warning("Remoting is not running. Ignoring shutdown attempt");
                return Task.FromResult(true);
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
                            _log.Warning("Shutdown finished, but flushing might not have been successful and some messages might have been dropped. " +
                                "Increase akka.remote.flush-wait-on-shutdown to a larger value to avoid this.");
                        }
                        finalize();
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        public override void Send(object message, IActorRef sender, RemoteActorRef recipient)
        {
            if (_endpointManager == null)
            {
                throw new RemoteTransportException("Attempted to send remote message but Remoting is not running.", null);
            }

            _endpointManager.Tell(new EndpointManager.Send(message, recipient, sender), sender ?? ActorRefs.NoSender);
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
                    .ContinueWith(result =>
                    {
                        if (result.IsCanceled || result.IsFaulted)
                            return false;
                        return result.Result.Status;
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public override Address LocalAddressForRemote(Address remote)
        {
            return Remoting.LocalAddressForRemote(_transportMapping, remote);
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

    /// <summary>
    /// Message type used to provide both <see cref="Props"/> and a name for a new transport actor
    /// </summary>
    internal sealed class RegisterTransportActor : INoSerializationVerificationNeeded
    {
        public RegisterTransportActor(Props props, string name)
        {
            Props = props;
            Name = name;
        }

        public Props Props { get; private set; }

        public string Name { get; private set; }
    }

    /// <summary>
    /// Actor responsible for supervising the creation of all transport actors
    /// </summary>
    internal class TransportSupervisor : ReceiveActor
    {
        private readonly SupervisorStrategy _strategy = new OneForOneStrategy(exception => Directive.Restart);
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _strategy;
        }

        public TransportSupervisor()
        {
            Receive<RegisterTransportActor>(
                r =>
                    Sender.Tell(
                        Context.ActorOf(RARP.For(Context.System).ConfigureDispatcher(r.Props.WithDeploy(Deploy.Local)),
                            r.Name)));
        }
    }
}

