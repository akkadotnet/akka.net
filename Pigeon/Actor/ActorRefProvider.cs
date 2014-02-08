using Pigeon.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorRefProvider
    {
        public LocalActorRef ActorOf(ActorCell parentContext,Props props,string name)
        {
            if (props.RouterConfig != null)
            {
                var cell = new RoutedActorCell(parentContext, props, name);
                parentContext.NewActor(cell);
                parentContext.Watch(cell.Self);
                return cell.Self;
            }
            else
            {
                var cell = new ActorCell(parentContext, props, name);
                parentContext.NewActor(cell);
                parentContext.Watch(cell.Self);
                return cell.Self;
            }
        }
    }
}
