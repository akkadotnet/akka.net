using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote
{
    public class RemoteActorRefProvider : ActorRefProvider
    {
        private LoggingAdapter log;
        public RemoteDaemon RemoteDaemon { get;private set; }

        public RemoteActorRefProvider(ActorSystem system)
            : base(system)
        {
            this.config = system.Settings.Config.GetConfig("akka.remote");
            this.log = Logging.GetLogger(system, this);
        }

        private Config config;

        public override void Init()
        {
            //TODO: this should not be here
            this.Address = new Address("akka", this.System.Name); //TODO: this should not work this way...
            //TODO: this should not be here
            //var host = config.GetString("server.host");
            //var port = config.GetInt("server.port");
            //this.Address = new Address("akka.tcp", System.Name, host, port);

            base.Init();
            var daemonMsgCreateSerializer = new Serialization.DaemonMsgCreateSerializer(this.System);
            this.System.Serialization.AddSerializer(daemonMsgCreateSerializer);
            this.System.Serialization.AddSerializationMap(typeof(DaemonMsgCreate), daemonMsgCreateSerializer);

            this.RemoteDaemon = new Remote.RemoteDaemon (this.System,RootPath / "remote",null);
            this.Transport = new Remoting(System, this);
            this.RemoteSettings = new RemoteSettings(System.Settings.Config);
            this.Transport.Start();
      //      RemoteHost.StartHost(System, port);
        }

        public override InternalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor, ActorPath path)
        {
            var mailbox = System.Mailboxes.FromConfig(props.Mailbox);
           
            if (props.Deploy != null && props.Deploy.Scope is RemoteScope)
            {
                return RemoteActorOf(system, props, supervisor, path, mailbox);
            }
            else
            {
                return LocalActorOf(system, props, supervisor, path, mailbox);
            }
        }

        private bool HasAddress(Address address) 
        {
            return address == this.Address || this.Transport.Addresses.Any(a => a.Equals(address));
        }

        public override ActorRef RootGuardianAt(Address address)
        {
            if (HasAddress(address))
            {
                return this.RootCell.Self;
            }
            else
            {
                return new RemoteActorRef(
                    Transport, 
                    Transport.LocalAddressForRemote(address),
                    new RootActorPath(address), 
                    ActorRef.Nobody, 
                    Props.None, 
                    Deploy.None);
            }
        }

        private InternalActorRef RemoteActorOf(ActorSystem system, Props props, InternalActorRef supervisor, ActorPath path, Mailbox mailbox)
        {
            var scope = (RemoteScope)props.Deploy.Scope;
            var d = props.Deploy;
            var addr = scope.Address;

            if (HasAddress(addr))
            {
                return LocalActorOf(System, props, supervisor, path, mailbox);
            }

            var localAddress = Transport.LocalAddressForRemote(addr);

            var rpath = (new RootActorPath(addr) / "remote" / localAddress.Protocol / localAddress.HostPort() / path.Elements.Drop(1).ToArray()).
              WithUid(path.Uid);
            var remoteRef = new RemoteActorRef(Transport, localAddress, rpath, supervisor, props, d);
            remoteRef.Start();
            return remoteRef;            
        }

        private static InternalActorRef LocalActorOf(ActorSystem system, Props props, InternalActorRef supervisor, ActorPath path, Mailbox mailbox)
        {
            ActorCell cell = null;
            if (props.RouterConfig is NoRouter)
            {
                cell = new ActorCell(system, supervisor, props, path, mailbox);

            }
            else
            {
                cell = new RoutedActorCell(system, supervisor, props, path, mailbox);
            }

            cell.NewActor();
            //   parentContext.Watch(cell.Self);
            return cell.Self;
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
                    var parts = actorPath.Elements.Drop(1).ToArray();
                    return RemoteDaemon.GetChild(parts);
                }
                else if (actorPath.Elements.Head() == "temp")
                {
                    //skip ""/"temp", 
                    var parts = actorPath.Elements.Drop(1).ToArray();
                    return TempContainer.GetChild(parts);
                }
                else
                {
                    //standard
                    var currentContext = RootCell;
                    if (actorPath.ToStringWithoutAddress() == "/")
                    {
                        return currentContext.Self;
                    }
                    foreach (var part in actorPath.Elements)
                    {
                        currentContext = ((LocalActorRef)currentContext.Child(part)).Cell;
                    }
                    return currentContext.Self;
                }           
            }
            else
            {
                return new RemoteActorRef(Transport,
                    Transport.LocalAddressForRemote(actorPath.Address),
                    actorPath,
                    ActorRef.Nobody,
                    Props.None,
                    Deploy.None);               
            }
        }

        public void UseActorOnNode(RemoteActorRef actor, Props props, Deploy deploy, InternalActorRef supervisor)
        {
            log.Debug("[{0}] Instantiating Remote Actor [{1}]", RootPath, actor.Path);
            var remoteNode = ResolveActorRef(new RootActorPath(actor.Path.Address) / "remote");
            remoteNode.Tell(new DaemonMsgCreate(props, deploy, actor.Path.ToStringWithAddress() /*TODO: toserializationformat*/, supervisor));
        }

        public Remoting Transport { get;private set; }

        internal RemoteSettings RemoteSettings { get;private set; }
    }
}
