//-----------------------------------------------------------------------
// <copyright file="Remoting.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Util.Internal;
using Akka.Configuration;

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
        /// <param name="address">The actor address to encode.</param>
        /// <returns>The URL-encoded string representation of the address.</returns>
        public static string Encode(Address address)
        {
            return WebUtility.UrlEncode(address.ToString());
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
        private readonly IRemoteActorRefProvider _provider;

        /// <summary>
        /// Used as part of the <see cref="ExtensionIdProvider{RARP}"/>
        /// </summary>
        public RARP() { }

        private RARP(IRemoteActorRefProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Applies the configured remoting dispatcher to actor properties when one is set.
        /// </summary>
        /// <param name="props">The actor properties to configure.</param>
        /// <returns>The properties with the configured dispatcher, or the original properties when no dispatcher is configured.</returns>
        public Props ConfigureDispatcher(Props props)
        {
            return _provider.RemoteSettings.ConfigureDispatcher(props);
        }

        /// <summary>
        /// Creates the remoting extension for the actor system's remote actor reference provider.
        /// </summary>
        /// <param name="system">The actor system requesting the remoting extension.</param>
        /// <returns>A remoting extension bound to the system's remote provider.</returns>
        public override RARP CreateExtension(ExtendedActorSystem system)
        {
            return new RARP((IRemoteActorRefProvider)system.Provider);
        }

        /// <summary>
        /// The underlying remote actor reference provider.
        /// </summary>
        public IRemoteActorRefProvider Provider
        {
            get { return _provider; }
        }

        #region Static methods

        /// <summary>
        /// Gets the remoting extension for an actor system.
        /// </summary>
        /// <param name="system">The actor system that owns the extension.</param>
        /// <returns>The actor system's remoting extension.</returns>
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
    internal sealed class Remoting : RemoteTransport
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

        private readonly IActorRef _transportSupervisor;
        private readonly EventPublisher _eventPublisher;

        /// <summary>
        /// Initializes the classic remoting transport for an actor system and remote actor reference provider.
        /// </summary>
        /// <param name="system">The actor system that owns remoting.</param>
        /// <param name="provider">The remote actor reference provider used by the transport.</param>
        public Remoting(ExtendedActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
            _log = Logging.GetLogger(system, "remoting");
            _eventPublisher = new EventPublisher(system, _log, Logging.LogLevelFor(provider.RemoteSettings.RemoteLifecycleEventsLogLevel));
            _transportSupervisor = system.SystemActorOf(Props.Create<TransportSupervisor>(), "transports");
        }

        #region RemoteTransport overrides

        /// <summary>
        /// Gets the addresses bound by the configured remoting transports.
        /// </summary>
        public override ISet<Address> Addresses
        {
            get { return _addresses; }
        }

        /// <summary>
        /// Gets the first address bound by the configured transports as the default address.
        /// </summary>
        public override Address DefaultAddress
        {
            get { return _defaultAddress; }
        }

        /// <summary>
        /// Start assumes that it cannot be followed by another Start() without having a Shutdown() first
        /// </summary>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when no transports are enabled under the "akka.remote.enabled-transports" configuration setting.
        /// </exception>
        /// <exception cref="TaskCanceledException">
        /// This exception is thrown when startup is canceled due to a timeout.
        /// </exception>
        /// <exception cref="TimeoutException">
        /// This exception is thrown when startup times out.
        /// </exception>
        /// <exception cref="Exception">
        /// This exception is thrown when a general error occurs during startup.
        /// </exception>
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
                    NotifyError("Startup was canceled due to timeout", ex);
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

        /// <summary>
        /// Shuts down remoting and completes after the endpoint manager flushes and stops its transports.
        /// </summary>
        /// <returns>A task that completes when shutdown and transport flushing finish.</returns>
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

                void Action()
                {
                    _eventPublisher.NotifyListeners(new RemotingShutdownEvent());
                    _endpointManager = null;
                }

                return _endpointManager.Ask<bool>(new EndpointManager.ShutdownAndFlush(), timeout).ContinueWith(result =>
                {
                    if (result.IsFaulted || result.IsCanceled) //Shutdown was not successful
                    {
                        NotifyError("Failure during shutdown of remoting", result.Exception);
                        Action();
                    }
                    else
                    {
                        if (!result.Result)
                        {
                            _log.Warning("Shutdown finished, but flushing might not have been successful and some messages might have been dropped. " +
                                "Increase akka.remote.flush-wait-on-shutdown to a larger value to avoid this.");
                        }
                        Action();
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Sends a message through remoting to a remote actor reference.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender actor reference, if available.</param>
        /// <param name="recipient">The remote actor reference that receives the message.</param>
        /// <exception cref="RemoteTransportException">Remoting is not running.</exception>
        public override void Send(object message, IActorRef sender, RemoteActorRef recipient)
        {
            if (_endpointManager == null)
            {
                throw new RemoteTransportException("Attempted to send remote message but Remoting is not running.", null);
            }

            _endpointManager.Tell(new EndpointManager.Send(message, recipient, sender), sender ?? ActorRefs.NoSender);
        }

        /// <summary>
        /// Sends a management command to the underlying transports and returns whether it was handled.
        /// </summary>
        /// <param name="cmd">The transport management command to send.</param>
        /// <exception cref="RemoteTransportException">Remoting is not running.</exception>
        /// <returns>A task containing <c>true</c> if a transport handled the command; otherwise, <c>false</c>.</returns>
        public override async Task<bool> ManagementCommand(object cmd)
            => await ManagementCommand(cmd, CancellationToken.None);

        public override async Task<bool> ManagementCommand(object cmd, CancellationToken cancellationToken)
        {
            if (_endpointManager == null)
                throw new RemoteTransportException("Attempted to send management command but Remoting is not running.", null);

            var result = await _endpointManager.Ask<EndpointManager.ManagementCommandAck>(
                message: new EndpointManager.ManagementCommand(cmd),
                timeout: Provider.RemoteSettings.CommandAckTimeout, 
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result.Status;
        }

        /// <summary>
        /// Selects the local transport address responsible for reaching a remote address.
        /// </summary>
        /// <param name="remote">The address of the remote actor system.</param>
        /// <returns>The local address of the single responsible transport.</returns>
        public override Address LocalAddressForRemote(Address remote)
        {
            return Remoting.LocalAddressForRemote(_transportMapping, remote);
        }

        /// <summary>
        /// Marks a remote system as out of sync and prevents reconnects until the quarantine timeout elapses.
        /// </summary>
        /// <param name="address">The address of the remote system to be quarantined</param>
        /// <param name="uid">The UID of the remote system; if the uid is not defined it will not be a strong quarantine but the current
        /// endpoint writer will be stopped (dropping system messages) and the address will be gated.</param>
        /// <exception cref="RemoteTransportException">
        /// This exception is thrown when trying to quarantine a system but remoting is not running.
        /// </exception>
        public override void Quarantine(Address address, long? uid)
        {
            if (_endpointManager == null)
            {
                throw new RemoteTransportException($"Attempted to quarantine address {address} with uid {uid} but Remoting is not running");
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

        /// <summary>
        /// Actor name used for the endpoint manager system actor.
        /// </summary>
        public const string EndpointManagerName = "endpointManager";

        /// <summary>
        /// Finds the local bound address of the single transport responsible for a remote address.
        /// </summary>
        /// <param name="transportMapping">Bound addresses grouped by transport protocol.</param>
        /// <param name="remote">The remote address to reach.</param>
        /// <exception cref="RemoteTransportException">No transport for the protocol is loaded, no transport accepts the address, or more than one transport accepts it.</exception>
        /// <returns>The bound local address of the responsible transport.</returns>
        internal static Address LocalAddressForRemote(
            IDictionary<string, HashSet<ProtocolTransportAddressPair>> transportMapping, Address remote)
        {
            if (transportMapping.TryGetValue(remote.Protocol, out var transports))
            {
                ProtocolTransportAddressPair[] responsibleTransports =
                    transports.Where(t => t.ProtocolTransport.IsResponsibleFor(remote)).ToArray();
                if (responsibleTransports.Length == 0)
                    throw new RemoteTransportException(
                        "No transport is responsible for address:[" + remote + "] although protocol [" + remote.Protocol +
                        "] is available." +
                        " Make sure at least one transport is configured to be responsible for the address.",
                        null);

                if (responsibleTransports.Length == 1)
                    return responsibleTransports.First().Address;

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
        /// <summary>
        /// Creates a request for the transport supervisor to start a named actor.
        /// </summary>
        /// <param name="props">The properties for the transport actor.</param>
        /// <param name="name">The actor name assigned by the supervisor.</param>
        public RegisterTransportActor(Props props, string name)
        {
            Props = props;
            Name = name;
        }

        /// <summary>
        /// Gets the properties used to create the transport actor.
        /// </summary>
        public Props Props { get; private set; }

        /// <summary>
        /// Gets the actor name used by the transport supervisor.
        /// </summary>
        public string Name { get; private set; }
    }

    /// <summary>
    /// Actor responsible for supervising the creation of all transport actors
    /// </summary>
    internal sealed class TransportSupervisor : ReceiveActor
    {
        private readonly SupervisorStrategy _strategy = new OneForOneStrategy(_ => Directive.Restart);
        /// <summary>
        /// Returns the supervision strategy used for transport actors.
        /// </summary>
        /// <returns>A one-for-one strategy that restarts failed transport actors.</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _strategy;
        }

        /// <summary>
        /// Creates a supervisor that starts registered transport actors.
        /// </summary>
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

