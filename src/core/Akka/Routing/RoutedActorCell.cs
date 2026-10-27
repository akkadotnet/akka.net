//-----------------------------------------------------------------------
// <copyright file="RoutedActorCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Scheduler;
using Akka.Dispatch;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class RoutedActorCell : ActorCell
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RoutedActorCell"/> class.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <param name="self">The router actor reference backed by this cell.</param>
        /// <param name="routerProps">The properties used to create the router actor.</param>
        /// <param name="dispatcher">The dispatcher used by the router actor.</param>
        /// <param name="routeeProps">The properties used to create pool routees.</param>
        /// <param name="supervisor">The actor supervising this router.</param>
        public RoutedActorCell(
            ActorSystemImpl system,
            IInternalActorRef self,
            Props routerProps,
            MessageDispatcher dispatcher,
            Props routeeProps,
            IInternalActorRef supervisor)
            : base(system, self, routerProps, dispatcher, supervisor)
        {
            RouteeProps = routeeProps;
            RouterConfig = routerProps.RouterConfig;
            Router = null;
        }

        /// <summary>
        /// The routing logic and current routees used to send messages.
        /// </summary>
        public Router Router { get; private set; }

        /// <summary>
        /// The properties used to create pool routees.
        /// </summary>
        public Props RouteeProps { get; }

        /// <summary>
        /// The configuration that defines this router.
        /// </summary>
        public RouterConfig RouterConfig { get; }

        /// <summary>
        /// Adds a routee to the router and starts watching it when it is actor-backed.
        /// </summary>
        /// <param name="routee">The routee to add.</param>
        internal void AddRoutee(Routee routee)
        {
            AddRoutees(new[] { routee });
        }

        /// <summary>
        /// Adds routees to the router and starts watching actor-backed routees.
        /// </summary>
        /// <param name="routees">The routees to add.</param>
        internal void AddRoutees(IList<Routee> routees)
        {
            foreach (var routee in routees)
            {
                Watch(routee);
            }
            var r = Router;
            Router = r.WithRoutees(r.Routees.Concat(routees).ToArray());
        }

        /// <summary>
        /// Removes a routee, stops watching it, and optionally stops it when it is a child.
        /// </summary>
        /// <param name="routee">The routee to remove.</param>
        /// <param name="stopChild">Whether to stop the routee when it is a child of this router.</param>
        internal void RemoveRoutee(Routee routee, bool stopChild)
        {
            RemoveRoutees(new[] { routee }, stopChild);
        }

        /// <summary>
        /// Remove routees from <see cref="Router"/>. Messages in flight may still
        /// be routed to the old <see cref="Router"/> instance containing the old routees.
        /// </summary>
        /// <param name="affectedRoutees">The routees to remove from the router.</param>
        /// <param name="stopChild">Whether to stop removed routees that are children of this router.</param>
        internal void RemoveRoutees(IList<Routee> affectedRoutees, bool stopChild)
        {
            var r = Router;
            var routees = r.Routees
                .Where(routee => !affectedRoutees.Contains(routee))
                .ToArray();

            Router = r.WithRoutees(routees);

            foreach (var affectedRoutee in affectedRoutees)
            {
                Unwatch(affectedRoutee);
                if (stopChild)
                    StopIfChild(affectedRoutee);
            }
        }

        private void Watch(Routee routee)
        {
            if (routee is ActorRefRoutee actorRef)
                Watch(actorRef.Actor);
        }

        private void Unwatch(Routee routee)
        {
            if (routee is ActorRefRoutee actorRef)
                Unwatch(actorRef.Actor);
        }

        /// <summary>
        /// Used to stop child routees - typically used in resizable <see cref="Pool"/> routers
        /// </summary>
        /// <param name="routee">The routee to stop when it is a child actor of this router.</param>
        private void StopIfChild(Routee routee)
        {
            if (routee is ActorRefRoutee actorRefRoutee && TryGetChildStatsByName(actorRefRoutee.Actor.Path.Name, out IChildStats childActorStats))
            {
                if (childActorStats is ChildRestartStats childRef && childRef.Child != null)
                {
                    // The reason for the delay is to give concurrent
                    // messages a chance to be placed in mailbox before sending PoisonPill,
                    // best effort.
                    System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(100), actorRefRoutee.Actor,
                        PoisonPill.Instance, Self);
                }
            }
        }

        /// <summary>
        /// Creates the initial router and routees before starting the router actor.
        /// </summary>
        public override void Start()
        {
            // create the initial routees before scheduling the Router actor
            Router = RouterConfig.CreateRouter(System);

            if (RouterConfig is Pool pool)
            {
                var nrOfRoutees = pool.GetNrOfInstances(System);
                if (nrOfRoutees > 0)
                    AddRoutees(Vector.Fill<Routee>(nrOfRoutees)(() => pool.NewRoutee(RouteeProps, this)));
            }
            else if (RouterConfig is Group group)
            {
                // must not use group.paths(system) for old (not re-compiled) custom routers
                // for binary backwards compatibility reasons
                var deprecatedPaths = group.GetPaths(System);

                var paths = deprecatedPaths == null
                        ? group.GetPaths(System)?.ToArray()
                        : deprecatedPaths.ToArray();

                if (paths.NonEmpty())
                    AddRoutees(paths!.Select(p => group.RouteeFor(p, this)).ToList());
            }

            PreSuperStart();
            base.Start();
        }

        /// <summary>
        /// Called when <see cref="Router"/> is initialized but before the base class' <see cref="Start"/> to
        /// be able to do extra initialization in a subclass.
        /// </summary>
        protected virtual void PreSuperStart() { }

        /// <summary>
        /// Routes user messages through the current router and sends management messages to the router actor.
        /// </summary>
        /// <param name="envelope">The message envelope to route or process as a management message.</param>
        public override void SendMessage(Envelope envelope)
        {
            if (RouterConfig.IsManagementMessage(envelope.Message))
                base.SendMessage(envelope);
            else
            {
                // Bugfix: https://github.com/akkadotnet/akka.net/issues/7247 
                if(envelope.Message is IScheduledTellMsg scheduledTellMsg)
                    Router.Route(scheduledTellMsg.Message, envelope.Sender);
                else
                    Router.Route(envelope.Message, envelope.Sender);
            }
               
        }

        /// <summary>
        /// Creates the router actor selected by this router's configuration.
        /// </summary>
        protected override ActorBase CreateNewActorInstance()
        {
            ActorBase instance = RouterConfig.CreateRouterActor();
            return instance;
        }
    }
}
