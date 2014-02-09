using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RoutedActorCell : ActorCell
    {
        public RoutedActorCell(IActorContext parentContext,Props props,string name,Mailbox mailbox) :base(parentContext,props,name,mailbox)
        {
            var routerConfig = props.RouterConfig;
            var routees = routerConfig.GetRoutees(this.System).ToArray();
            this.Router = new Routing.Router(routerConfig.GetLogic(),routees);
            var path = Self.Path;
            Self = new RoutedActorRef(this.Router,path,this);
        }
        public Router Router { get; private set; }
    }
}
