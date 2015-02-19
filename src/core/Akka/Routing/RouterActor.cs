using System;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class RouterActor : UntypedActor
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
            //do not scrap children
        }

        protected override void OnReceive(object message)
        {
            if (message is GetRoutees)
            {
                Sender.Tell(new Routees(Cell.Router.Routees));
            }
            else if (message is AddRoutee)
            {
                var addRoutee = message as AddRoutee;
                Cell.AddRoutee(addRoutee.Routee);
            }
            else if (message is RemoveRoutee)
            {
                var removeRoutee = message as RemoveRoutee;
                Cell.RemoveRoutee(removeRoutee.Routee, true);
                StopIfAllRouteesRemoved();
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