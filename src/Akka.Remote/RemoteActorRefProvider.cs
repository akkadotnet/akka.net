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
            Config = system.Settings.Config.WithFallback(RemoteConfigFactory.Default());
            RemoteSettings = new RemoteSettings(Config);
            log = Logging.GetLogger(System, this);
        }

        public RemoteDaemon RemoteDaemon { get; private set; }
        internal Remoting Transport { get; private set; }

        internal RemoteSettings RemoteSettings { get; private set; }

        public override void Init()
        {
            //TODO: this should not be here
            Address = new Address("akka", System.Name); //TODO: this should not work this way...
            Deployer = new RemoteDeployer(System.Settings);
            base.Init();

            var daemonMsgCreateSerializer = new DaemonMsgCreateSerializer(System);
            var messageContainerSerializer = new MessageContainerSerializer(System);
            System.Serialization.AddSerializer(daemonMsgCreateSerializer);
            System.Serialization.AddSerializationMap(typeof (DaemonMsgCreate), daemonMsgCreateSerializer);
            System.Serialization.AddSerializer(messageContainerSerializer);
            System.Serialization.AddSerializationMap(typeof (ActorSelectionMessage), messageContainerSerializer);

            RemoteDaemon = new RemoteDaemon(System, RootPath/"remote", null);
            Transport = new Remoting(System, this);
           
            Transport.Start();
            //      RemoteHost.StartHost(System, port);
        }

        public override InternalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path)
        {
            Mailbox mailbox = System.Mailboxes.FromConfig(props.Mailbox);

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
                return RemoteActorOf(system, props, supervisor, path, mailbox);
            }
            return LocalActorOf(system, props, supervisor, path, mailbox);
        }

        private bool HasAddress(Address address)
        {
            return address == Address || Transport.Addresses.Any(a => a.Equals(address));
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
            ActorPath path, Mailbox mailbox)
        {
            var scope = (RemoteScope) props.Deploy.Scope;
            Deploy d = props.Deploy;
            Address addr = scope.Address;

            if (HasAddress(addr))
            {
                return LocalActorOf(System, props, supervisor, path, mailbox);
            }

            Address localAddress = Transport.LocalAddressForRemote(addr);

            ActorPath rpath = (new RootActorPath(addr)/"remote"/localAddress.Protocol/localAddress.HostPort()/
                               path.Elements.Drop(1).ToArray()).
                WithUid(path.Uid);
            var remoteRef = new RemoteActorRef(Transport, localAddress, rpath, supervisor, props, d);
            remoteRef.Start();
            return remoteRef;
        }

        private static InternalActorRef LocalActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path, Mailbox mailbox)
        {
            ActorCell cell = null;
            if (props.RouterConfig is NoRouter || props.RouterConfig == null) //TODO: should not need nullcheck here
            {
                cell = new ActorCell(system, supervisor, props, path, mailbox);
            }
            else
            {
                var routeeProps = props.WithRouter(RouterConfig.NoRouter);
                cell = new RoutedActorCell(system, supervisor, props, routeeProps, path, mailbox);
            }

            cell.NewActor();
            //   parentContext.Watch(cell.Self);
            return cell.Self;
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
    }
}