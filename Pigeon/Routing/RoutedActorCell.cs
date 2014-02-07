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
        public RoutedActorCell(IActorContext parentContext,Props props,string name) :base(parentContext,props,name)
        {

        }
        public Router Router { get; private set; }
    }
}
