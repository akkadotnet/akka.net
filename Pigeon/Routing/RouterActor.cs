using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RouterActor : UntypedActor
    {
        public RouterActor() : base()
        {
            if (!(Context is RoutedActorCell))
            {
                throw new NotSupportedException("Current Context must be of type RouterActorContext");
            }
        }

        protected RoutedActorCell Cell
        {
            get
            {
                return Context.AsInstanceOf<RoutedActorCell>();
            }
        }

        protected override void PreRestart(Exception cause, object message)
        {
 	 
        }
        
        protected override void OnReceive(object message)
        {
            if (message is GetRoutees)
            {
                Sender.Tell(new Routees(this.Cell.Router.Routees));
            }                
        }
    }
}
