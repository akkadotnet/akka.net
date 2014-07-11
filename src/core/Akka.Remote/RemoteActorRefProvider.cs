using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
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
        private readonly LoggingAdapter _log;

        public Config Config { get; private set; }

        public RemoteActorRefProvider(string systemName, Settings settings, EventStream eventStream)
        {
            var remoteDeployer = new RemoteDeployer(settings);
            Func<ActorPath, InternalActorRef> deadLettersFactory = null; //TODO:  path => new RemoteDeadLetterActorRef(this, path, eventStream);
            _local = new LocalActorRefProvider(systemName, settings, eventStream, remoteDeployer, deadLettersFactory);
            Config = settings.Config.WithFallback(RemoteConfigFactory.Default());
            RemoteSettings = new RemoteSettings(Config);
            Deployer = remoteDeployer;
            _log = _local.Log;
        }

        private readonly LocalActorRefProvider _local;
        private Internals _internals;
        private ActorSystem _system;

        private Internals RemoteInternals
        {
            get
            {
                return _internals ??
                       (_internals =
                           new Internals(new Remoting(_system, this), _system.Serialization,
                               new RemoteDaemon(_system, RootPath / "remote", SystemGuardian, _log)));
            }
        }

        public InternalActorRef RemoteDaemon { get { return RemoteInternals.RemoteDaemon; } }
        internal RemoteTransport Transport { get { return RemoteInternals.Transport; } }

        internal RemoteSettings RemoteSettings { get; private set; }

        /* these are only available after Init() is called */

        public ActorPath RootPath
        {
            get { return _local.RootPath; }
        }


        public InternalActorRef RootGuardian { get { return _local.RootGuardian; } }
        public LocalActorRef Guardian { get { return _local.Guardian; } }
        public LocalActorRef SystemGuardian { get { return _local.SystemGuardian; } }
        public InternalActorRef TempContainer { get { return _local.TempContainer; } }
        public ActorRef DeadLetters { get { return _local.DeadLetters; } }
        public Deployer Deployer { get; private set; }
        public Address DefaultAddress { get; private set; } //TODO: Should be Transport.DefaultAddress
        public Settings Settings { get { return _local.Settings; } }
        public Task TerminationTask { get { return _local.TerminationTask; } }
        private InternalActorRef InternalDeadLetters { get { return (InternalActorRef) _local.DeadLetters; } }

        public ActorPath TempPath()
        {
            return _local.TempPath();
        }

        public void RegisterTempActor(InternalActorRef actorRef, ActorPath path)
        {
            _local.RegisterTempActor(actorRef, path);
        }

        public void UnregisterTempActor(ActorPath path)
        {
            _local.UnregisterTempActor(path);
        }

        public void Init(ActorSystem system)
        {
            _system = system;
            //TODO: this should not be here
            DefaultAddress = new Address("akka", system.Name); //TODO: this should not work this way...

            _local.Init(system);

            var daemonMsgCreateSerializer = new DaemonMsgCreateSerializer(system);
            var messageContainerSerializer = new MessageContainerSerializer(system);
            system.Serialization.AddSerializer(daemonMsgCreateSerializer);
            system.Serialization.AddSerializationMap(typeof(DaemonMsgCreate), daemonMsgCreateSerializer);
            system.Serialization.AddSerializer(messageContainerSerializer);
            system.Serialization.AddSerializationMap(typeof(ActorSelectionMessage), messageContainerSerializer);

            Transport.Start();
            //      RemoteHost.StartHost(System, port);
        }

        public InternalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor, ActorPath path, bool systemService, Deploy deploy, bool lookupDeploy, bool async)
        {
            if(systemService) return LocalActorOf(system, props, supervisor, path, true, deploy, lookupDeploy, async);

            Deploy configDeploy = system.Provider.Deployer.Lookup(path);
            deploy = configDeploy ?? props.Deploy ?? Deploy.None;
            if(deploy.Mailbox != null)
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
                    //if (path.Elements.First() == "remote")
                    //{
                    //    //this is a remote deployed actor
                        
                    //}
                    //else
                    //{
                    return LocalActorOf(system, props, supervisor, path, false, null, false, async);     //TODO: replace deploy:null with deployment.headOption
                    // }
                }

                return RemoteActorOf(system, props, supervisor, path);
            }
            return LocalActorOf(system, props, supervisor, path, false, null, false, async);        //TODO: replace deploy:null with deployment.headOption
        }

        private bool HasAddress(Address address)
        {
            return address == _local.RootPath.Address || address == RootPath.Address || Transport.Addresses.Any(a => a == address);
        }

        public ActorRef RootGuardianAt(Address address)
        {
            if (HasAddress(address))
            {
                return RootGuardian;
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
            ActorPath path, bool systemService, Deploy deploy, bool lookupDeploy, bool async)
        {
            return _local.ActorOf(system, props, supervisor, path, systemService, deploy, lookupDeploy, async);
        }


        /// <summary>
        /// INTERNAL API.
        /// 
        /// Called in deserialization of incoming remote messages where the correct local address is known.
        /// </summary>
        internal InternalActorRef ResolveActorRefWithLocalAddress(string path, Address localAddress)
        {
            ActorPath actorPath;
            if(ActorPath.TryParse(path, out actorPath))
            {
                //the actor's local address was already included in the ActorPath
                if(HasAddress(actorPath.Address)) 
                    return (InternalActorRef)ResolveActorRef(actorPath);
                return new RemoteActorRef(Transport, localAddress, new RootActorPath(actorPath.Address) / actorPath.Elements, ActorRef.Nobody, Props.None, Deploy.None);                
            }
            _log.Debug("resolve of unknown path [{0}] failed", path);
            return InternalDeadLetters;
        }

        public ActorRef ResolveActorRef(string path)
        {
            if(path == "")
                return ActorRef.NoSender;

            ActorPath actorPath;
            if(ActorPath.TryParse(path,out actorPath))
                return ResolveActorRef(actorPath);

            _log.Debug("resolve of unknown path [{0}] failed", path);
            return DeadLetters;
        }


        public ActorRef ResolveActorRef(ActorPath actorPath)
        {
            if(HasAddress(actorPath.Address))
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
                var rootGuardian = RootGuardian;
                if(actorPath.ToStringWithoutAddress() == "/")
                {
                    return rootGuardian;
                }
                return rootGuardian.GetChild(actorPath.Elements);
            }
            return new RemoteActorRef(Transport,
                Transport.LocalAddressForRemote(actorPath.Address),
                actorPath,
                ActorRef.Nobody,
                Props.None,
                Deploy.None);
        }

        public Address GetExternalAddressFor(Address address)
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
                System = _system,
                Address = actor.LocalAddressToUse,
            };
            _log.Debug("[{0}] Instantiating Remote Actor [{1}]", RootPath, actor.Path);
            ActorRef remoteNode = ResolveActorRef(new RootActorPath(actor.Path.Address) / "remote");
            remoteNode.Tell(new DaemonMsgCreate(props, deploy, actor.Path.ToSerializationFormat(), supervisor));
        }

        /// <summary>
        ///     Afters the send system message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void AfterSendSystemMessage(SystemMessage message)
        {
            message.Match()
                .With<Watch>(m => { })
                .With<Unwatch>(m => { });

            //    message match {
            //  // Sending to local remoteWatcher relies strong delivery guarantees of local send, i.e.
            //  // default dispatcher must not be changed to an implementation that defeats that
            //  case rew: RemoteWatcher.Rewatch ⇒
            //    remoteWatcher ! RemoteWatcher.RewatchRemote(rew.watchee, rew.watcher)
            //  case Watch(watchee, watcher)   ⇒ remoteWatcher ! RemoteWatcher.WatchRemote(watchee, watcher)
            //  case Unwatch(watchee, watcher) ⇒ remoteWatcher ! RemoteWatcher.UnwatchRemote(watchee, watcher)
            //  case _                         ⇒
            //}
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