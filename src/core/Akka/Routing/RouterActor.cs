﻿//-----------------------------------------------------------------------
// <copyright file="RouterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
                var routedActorCell = Context as RoutedActorCell;
                if (routedActorCell != null)
                    return routedActorCell;
                else
                    throw new ActorInitializationException($"Router actor can only be used in RoutedActorRef, not in {Context.GetType()}");
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
                Cell.RemoveRoutee(removeRoutee.Routee, stopChild: true);
                StopIfAllRouteesRemoved();
            }
            else if (message is Terminated)
            {
                var terminated = message as Terminated;
                Cell.RemoveRoutee(new ActorRefRoutee(terminated.ActorRef), stopChild: false);
                StopIfAllRouteesRemoved();
            }
            else
            {
                RoutingLogicController?.Forward(message);
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

