//-----------------------------------------------------------------------
// <copyright file="RouterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// The routed actor cell managed by this router actor.
        /// </summary>
        /// <exception cref="ActorInitializationException">The actor is not running inside a routed actor cell.</exception>
        protected RoutedActorCell Cell { get; }

        private IActorRef RoutingLogicController { get; }

        public RouterActor()
        {
            Cell = Context is RoutedActorCell routedActorCell
                ? routedActorCell : throw new ActorInitializationException($"Router actor can only be used in RoutedActorRef, not in {Context.GetType()}");

            var props = Cell.RouterConfig.RoutingLogicController(Cell.Router.RoutingLogic);
            if (props != null)
                RoutingLogicController = Context.ActorOf(
                    props.WithDispatcher(Context.Props.Dispatcher), "routingLogicController");
        }

        /// <summary>
        /// Handles routee management requests and forwards other management messages to the configured controller.
        /// </summary>
        /// <param name="message">The management message to process.</param>
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
        /// Stops the router actor when its configuration requires it and all routees have been removed.
        /// </summary>
        protected virtual void StopIfAllRouteesRemoved()
        {
            if (!Cell.Router.Routees.Any() && Cell.RouterConfig.StopRouterWhenAllRouteesRemoved)
            {
                Context.Stop(Self);
            }
        }

        /// <summary>
        /// Leaves child routees intact when the router actor restarts.
        /// </summary>
        /// <param name="cause">The exception that caused the router actor to restart.</param>
        /// <param name="message">The message being processed when the failure occurred.</param>
        protected override void PreRestart(Exception cause, object message)
        {
            //do not scrap children
        }
    }
}
