using Pigeon.Actor;
using Pigeon.Dispatch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RoutedActorCell : ActorCell
    {
        public RoutedActorCell(ActorSystem system,InternalActorRef supervisor,Props props,ActorPath path,Mailbox mailbox) :base(system,supervisor,props,path,mailbox)
        {
            var routerConfig = props.RouterConfig;
            var routees = routerConfig.GetRoutees(this.System).ToArray();
            this.Router = new Routing.Router(routerConfig.GetLogic(),routees);            
            Self = new RoutedActorRef(this.Router,path,this);
        }
        public Router Router { get; private set; }
    }
}
