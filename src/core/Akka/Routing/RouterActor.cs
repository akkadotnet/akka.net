//-----------------------------------------------------------------------
// <copyright file="RouterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class RouterActor : UntypedActor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="ActorInitializationException">TBD</exception>
        protected RoutedActorCell Cell
        {
            get
            {
                return Context is RoutedActorCell routedActorCell
                    ? routedActorCell : throw new ActorInitializationException($"Router actor can only be used in RoutedActorRef, not in {Context.GetType()}");
            }
        }

        private IActorRef RoutingLogicController
        {
            get
            {
                return Context.ActorOf(Cell.RouterConfig.RoutingLogicController(Cell.Router.RoutingLogic).
                    WithDispatcher(Context.Props.Dispatcher), "routingLogicController");
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case GetRoutees getRoutees:
                    Sender.Tell(new Routees(Cell.Router.Routees));
                    break;
                case AddRoutee addRoutee:
                    Cell.AddRoutee(addRoutee.Routee);
                    break;
                case RemoveRoutee removeRoutee:
                    Cell.RemoveRoutee(removeRoutee.Routee, stopChild: true);
                    StopIfAllRouteesRemoved();
                    break;
                case Terminated terminated:
                    Cell.RemoveRoutee(new ActorRefRoutee(terminated.ActorRef), stopChild: false);
                    StopIfAllRouteesRemoved();
                    break;
                default:
                    RoutingLogicController?.Forward(message);
                    break;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual void StopIfAllRouteesRemoved()
        {
            if (!Cell.Router.Routees.Any() && Cell.RouterConfig.StopRouterWhenAllRouteesRemoved)
            {
                Context.Stop(Self);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="message">TBD</param>
        protected override void PreRestart(Exception cause, object message)
        {
            //do not scrap children
        }
    }
}

