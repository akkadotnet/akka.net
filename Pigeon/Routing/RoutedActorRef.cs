using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RoutedActorRef : ActorRef
    {
        protected override void TellInternal(object message, ActorRef sender)
        {
            
        }
    }
}
