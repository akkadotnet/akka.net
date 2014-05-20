using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    class RemoteActorRefProvider : ActorRefProvider
    {
        private readonly LoggingAdapter log;

        public Config Config { get; private set; }

        public RemoteActorRefProvider(ActorSystem system)
            : base(system)
        {
            _local = new LocalActorRefProvider(system);
            Config = system.Settings.Config.WithFallback(RemoteConfigFactory.Default());
            RemoteSettings = new RemoteSettings(Config);
            log = Logging.GetLogger(System, this);
        }

        private LocalActorRefProvider _local;
        private Internals _internals;

        private Internals RemoteInternals
        {
            get {
                return _internals ??
                       (_internals =
                           new Internals(new Remoting(System, this), System.Serialization,
                               new RemoteDaemon(System, RootPath/"remote", SystemGuardian)));
            }
        }

        public InternalActorRef RemoteDaemon { get { return RemoteInternals.RemoteDaemon; } }
        internal RemoteTransport Transport { get { return RemoteInternals.Transport; } }

        internal RemoteSettings RemoteSettings { get; private set; }

        /* these are only available after Init() is called */

        public override ActorPath RootPath
        {
            get { return _local.RootPath; }
        }

        public override ActorCell RootCell { get { return _local.RootCell; } }
        public override LocalActorRef Guardian { get { return _local.Guardian; } }
        public override LocalActorRef SystemGuardian { get { return _local.SystemGuardian; } }
        public override VirtualPathContainer TempContainer{ get { return _local.TempContainer; } }
        public override ActorPath TempNode { get { return _local.TempNode; } }
        public override ActorRef DeadLetters { get { return _local.DeadLetters; } }

        public override void Init()
        {
            //TODO: this should not be here
            Address = new Address("akka", System.Name); //TODO: this should not work this way...
            Deployer = new RemoteDeployer(System.Settings);
            _local.Init();

            var daemonMsgCreateSerializer = new DaemonMsgCreateSerializer(System);
            var messageContainerSerializer = new MessageContainerSerializer(System);
            System.Serialization.AddSerializer(daemonMsgCreateSerializer);
            System.Serialization.AddSerializationMap(typeof (DaemonMsgCreate), daemonMsgCreateSerializer);
            System.Serialization.AddSerializer(messageContainerSerializer);
            System.Serialization.AddSerializationMap(typeof (ActorSelectionMessage), messageContainerSerializer);
           
            Transport.Start();
            //      RemoteHost.StartHost(System, port);
        }

        public override InternalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path, bool systemService = false)
        {
            if (systemService) return LocalActorOf(system, props, supervisor, path, true);

            Deploy configDeploy = System.Provider.Deployer.Lookup(path);
            var deploy = configDeploy ?? props.Deploy ?? Deploy.None;
            if (deploy.Mailbox != null)
                props = props.WithMailbox(deploy.Mailbox);
            if (deploy.Dispatcher != null)
                props = props.WithDispatcher(deploy.Dispatcher);
            if (deploy.Scope is RemoteScope)
            {

            }

            //If this actor is a Router
            if (!(props.RouterConfig is NoRouter || props.RouterConfig == null))
            {
                //if no Router config value was specified, override with procedural input
                if (deploy.RouterConfig is NoRouter)
                {
                    deploy = deploy.WithRouterConfig(props.RouterConfig);
                }
            }
            props = props.WithDeploy(deploy);
            

            if (string.IsNullOrEmpty(props.Mailbox))
            {
                //   throw new NotSupportedException("Mailbox can not be configured as null or empty");
            }
            if (string.IsNullOrEmpty(props.Dispatcher))
            {
                //TODO: fix this..
                //    throw new NotSupportedException("Dispatcher can not be configured as null or empty");
            }


            if (props.Deploy != null && props.Deploy.Scope is RemoteScope)
            {
                var addr = props.Deploy.Scope.AsInstanceOf<RemoteScope>().Address;

                //Even if this actor is in RemoteScope, it might still be a local address
                if (HasAddress(addr))
                {
                    return LocalActorOf(system, props, supervisor, path, false);
                }

                return RemoteActorOf(system, props, supervisor, path);
            }
            return LocalActorOf(system, props, supervisor, path);
        }

        private bool HasAddress(Address address)
        {
            return address.Equals(Address) || address.Equals(_local.RootPath.Address) || Transport.Addresses.Any(a => a.Equals(address));
        }

        public override ActorRef RootGuardianAt(Address address)
        {
            if (HasAddress(address))
            {
                return RootCell.Self;
            }
            return new RemoteActorRef(
                Transport,
                Transport.LocalAddressForRemote(address),
                new RootActorPath(address),
                ActorRef.Nobody,
                Props.None,
                Deploy.None);
        }

        private InternalActorRef RemoteActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path)
        {
            var scope = (RemoteScope) props.Deploy.Scope;
            var d = props.Deploy;
            var addr = scope.Address;

            var localAddress = Transport.LocalAddressForRemote(addr);

            var rpath = (new RootActorPath(addr)/"remote"/localAddress.Protocol/localAddress.HostPort()/
                               path.Elements.ToArray()).
                WithUid(path.Uid);
            var remoteRef = new RemoteActorRef(Transport, localAddress, rpath, supervisor, props, d);
            remoteRef.Start();
            return remoteRef;
        }

        private InternalActorRef LocalActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path, bool systemService = false)
        {
            return _local.ActorOf(system, props, supervisor, path, systemService);
        }

        /// <summary>
        /// INTERNAL API.
        /// 
        /// Called in deserialization of incoming remote messages where the correct local address is known.
        /// </summary>
        internal InternalActorRef ResolveActorRefWithLocalAddress(string actorPath, Address localAddress)
        {
            var path = ActorPath.Parse(actorPath);
            //the actor's local address was already included in the ActorPath
            if (HasAddress(path.Address)) return (InternalActorRef)ResolveActorRef(actorPath);
            else return new RemoteActorRef(Transport, localAddress, new RootActorPath(path.Address) / path.Elements, ActorRef.Nobody, Props.None, Deploy.None);
        }

        public override ActorRef ResolveActorRef(ActorPath actorPath)
        {
            if (HasAddress(actorPath.Address))
            {
                if (actorPath.Elements.Head() == "remote")
                {
                    if (actorPath.ToStringWithoutAddress() == "/remote")
                    {
                        return RemoteDaemon;
                    }
                    //skip ""/"remote", 
                    string[] parts = actorPath.Elements.Drop(1).ToArray();
                    return RemoteDaemon.GetChild(parts);
                }
                if (actorPath.Elements.Head() == "temp")
                {
                    //skip ""/"temp", 
                    string[] parts = actorPath.Elements.Drop(1).ToArray();
                    return TempContainer.GetChild(parts);
                }
                //standard
                ActorCell currentContext = RootCell;
                if (actorPath.ToStringWithoutAddress() == "/")
                {
                    return currentContext.Self;
                }
                foreach (string part in actorPath.Elements)
                {
                    currentContext = ((LocalActorRef) currentContext.Child(part)).Cell;
                }
                return currentContext.Self;
            }
            return new RemoteActorRef(Transport,
                Transport.LocalAddressForRemote(actorPath.Address),
                actorPath,
                ActorRef.Nobody,
                Props.None,
                Deploy.None);
        }

        public override Address GetExternalAddressFor(Address address)
        {
            if (HasAddress(address)) { return _local.RootPath.Address; }
            if (!string.IsNullOrEmpty(address.Host) && address.Port.HasValue)
            {
                try
                {
                    return Transport.LocalAddressForRemote(address);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public void UseActorOnNode(RemoteActorRef actor, Props props, Deploy deploy, InternalActorRef supervisor)
        {
            Akka.Serialization.Serialization.CurrentTransportInformation = new Information
            {
                System = System,
                Address = actor.LocalAddressToUse,
            };
            log.Debug("[{0}] Instantiating Remote Actor [{1}]", RootPath, actor.Path);
            ActorRef remoteNode = ResolveActorRef(new RootActorPath(actor.Path.Address)/"remote");
            remoteNode.Tell(new DaemonMsgCreate(props, deploy, actor.Path.ToSerializationFormat(), supervisor));
        }


        #region Internals

        class Internals : NoSerializationVerificationNeeded
        {
            public Internals(RemoteTransport transport, Akka.Serialization.Serialization serialization, InternalActorRef remoteDaemon)
            {
                Transport = transport;
                Serialization = serialization;
                RemoteDaemon = remoteDaemon;
            }

            public RemoteTransport Transport { get; private set; }

            public Akka.Serialization.Serialization Serialization { get; private set; }

            public InternalActorRef RemoteDaemon { get; private set; }
        }

        #endregion

        #region RemotingTerminator

        enum TerminatorState
        {
            Uninitialized,
            Idle,
            WaitDaemonShutdown,
            WaitTransportShutdown,
            Finished
        }

        private class RemotingTerminator : FSM<TerminatorState, Internals>
        {
            private readonly ActorRef _systemGuardian;

            public RemotingTerminator(ActorRef systemGuardian)
            {
                _systemGuardian = systemGuardian;
                InitFSM();
            }

            private void InitFSM()
            {

                When(TerminatorState.Uninitialized, @event =>
                {
                    var internals = @event.StateData;
                    if (internals != null)
                    {
                        //TODO: add a termination hook to the system guardian
                        return GoTo(TerminatorState.Idle).Using(internals);
                    }
                    return null;
                });

                StartWith(TerminatorState.Uninitialized, null);
            }
        }

        #endregion

    }
}