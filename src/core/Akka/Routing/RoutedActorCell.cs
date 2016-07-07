//-----------------------------------------------------------------------
// <copyright file="RoutedActorCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
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

        public Router Router { get; private set; }

        public Props RouteeProps { get; }

        public RouterConfig RouterConfig { get; }

        internal void AddRoutee(Routee routee)
        {
            AddRoutees(new[] { routee });
        }

        internal void AddRoutees(IList<Routee> routees)
        {
            foreach (var routee in routees)
            {
                Watch(routee);
            }
            var r = Router;
            Router = r.WithRoutees(r.Routees.Concat(routees).ToArray());
        }

        internal void RemoveRoutee(Routee routee, bool stopChild)
        {
            RemoveRoutees(new[] { routee }, stopChild);
        }

        /// <summary>
        /// Remove routees from <see cref="Router"/>. Messages in flight may still
        /// be routed to the old <see cref="Router"/> instance containing the old routees.
        /// </summary>
        /// <param name="affectedRoutees"></param>
        /// <param name="stopChild"></param>
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
            var actorRef = routee as ActorRefRoutee;
            if (actorRef != null) Watch(actorRef.Actor);
        }

        private void Unwatch(Routee routee)
        {
            var actorRef = routee as ActorRefRoutee;
            if (actorRef != null) Unwatch(actorRef.Actor);
        }

        /// <summary>
        /// Used to stop child routees - typically used in resizable <see cref="Pool"/> routers
        /// </summary>
        /// <param name="routee"></param>
        private void StopIfChild(Routee routee)
        {
            var actorRefRoutee = routee as ActorRefRoutee;
            IChildStats childActorStats;
            if (actorRefRoutee != null && TryGetChildStatsByName(actorRefRoutee.Actor.Path.Name, out childActorStats))
            {
                var childRef = childActorStats as ChildRestartStats;
                if (childRef != null && childRef.Child != null)
                {
                    // The reason for the delay is to give concurrent
                    // messages a chance to be placed in mailbox before sending PoisonPill,
                    // best effort.
                    System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(100), actorRefRoutee.Actor,
                        PoisonPill.Instance, Self);
                }
            }
        }

        public override void Start()
        {
            // create the initial routees before scheduling the Router actor
            Router = RouterConfig.CreateRouter(System);

            if (RouterConfig is Pool)
            {
                var pool = (Pool)RouterConfig;
                // must not use pool.GetNrOfInstances(system) for old (not re-compiled) custom routers
                // for binary backwards compatibility reasons
                var deprecatedNrOfInstances = pool.NrOfInstances;

                var nrOfRoutees = deprecatedNrOfInstances < 0
                    ? pool.GetNrOfInstances(System)
                    : deprecatedNrOfInstances;

                if (nrOfRoutees > 0)
                    AddRoutees(Vector.Fill<Routee>(nrOfRoutees)(() => pool.NewRoutee(RouteeProps, this)));
            }
            else if (RouterConfig is Group)
            {
                var group = (Group)RouterConfig;
                // must not use group.paths(system) for old (not re-compiled) custom routers
                // for binary backwards compatibility reasons
                var deprecatedPaths = group.Paths;

                var paths = deprecatedPaths == null
                        ? group.GetPaths(System).ToArray()
                        : deprecatedPaths.ToArray();

                if (paths.NonEmpty())
                    AddRoutees(paths.Select(p => group.RouteeFor(p, this)).ToList());
            }

            PreSuperStart();
            base.Start();
        }

        /// <summary>
        /// Called when <see cref="Router"/> is initialized but before the base class' <see cref="Start"/> to
        /// be able to do extra initialization in a subclass.
        /// </summary>
        protected virtual void PreSuperStart() { }

        public override void SendMessage(IActorRef sender, object message)
        {
            if (RouterConfig.IsManagementMessage(message))
                base.SendMessage(sender, message);
            else
            {
                Router.Route(message, sender);
            }
        }

        protected override ActorBase CreateNewActorInstance()
        {
            RouterActor instance = RouterConfig.CreateRouterActor();
            return instance;
        }
    }
}