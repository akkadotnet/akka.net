using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Dispatch;
using Pigeon.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class RemoteActorRefProvider : ActorRefProvider
    {
        public RemoteActorRefProvider(ActorSystem system)
            : base(system)
        {
            this.config = system.Settings.Config.GetConfig("akka.remote");
        }

        private Config config;

        public override void Init()
        {
            base.Init();

            var host = config.GetString("server.host");
            var port = config.GetInt("server.port");

            this.Address = new Address("akka.tcp", System.Name, host, port);
            RemoteHost.StartHost(System, port);
        }

        public override LocalActorRef ActorOf(ActorCell parentContext, Props props, string name,long uid)
        {
            var mailbox = System.Mailboxes.FromConfig(props.Mailbox);

            if (props.RouterConfig != null)
            {
                var cell = new RoutedActorCell(parentContext, props, name, mailbox);
                parentContext.NewActor(cell);
                parentContext.Watch(cell.Self);
                return cell.Self;
            }
            else
            {
                var cell = new ActorCell(parentContext, props, name, mailbox);
                parentContext.NewActor(cell);
                parentContext.Watch(cell.Self);
                return cell.Self;
            }
        }

        public override ActorRef ResolveActorRef(ActorPath actorPath)
        {
            if (this.Address.Equals(actorPath.Address))
            {
                //standard
                var currentContext = RootCell;
                foreach (var part in actorPath.Skip(1))
                {
                    currentContext = ((LocalActorRef)currentContext.Child(part)).Cell;
                }
                return currentContext.Self;
            }
            else
            {
                return new RemoteActorRef(System, actorPath, this.Address.Port.Value);
            }
        }
    }
}
