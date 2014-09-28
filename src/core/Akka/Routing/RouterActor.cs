using System;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Routing
{
    public class RouterActor : UntypedActor
    {
        public RouterActor()
        {
            if (!(Context is RoutedActorCell))
            {
                throw new NotSupportedException("Current Context must be of type RouterActorContext");
            }
        }

        protected RoutedActorCell Cell
        {
            get { return Context.AsInstanceOf<RoutedActorCell>(); }
        }

        protected override void PreRestart(Exception cause, object message)
        {
        }

        protected override void OnReceive(object message)
        {
            if (message is GetRoutees)
            {
                Sender.Tell(new Routees(Cell.Router.Routees));
            }
        }

        protected void StopIfAllRouteesRemoved()
        {
            if (!Cell.Router.Routees.Any())
            {
                Context.Stop(Self);
            }
        }
    }
}