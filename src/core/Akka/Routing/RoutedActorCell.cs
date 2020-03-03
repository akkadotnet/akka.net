//-----------------------------------------------------------------------
// <copyright file="RoutedActorCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// Initializes a new instance of the <see cref="RoutedActorCell"/> class.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="self">TBD</param>
        /// <param name="routerProps">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="routeeProps">TBD</param>
        /// <param name="supervisor">TBD</param>
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
        /// TBD
        /// </summary>
        public Router Router { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Props RouteeProps { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public RouterConfig RouterConfig { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routee">TBD</param>
        internal void AddRoutee(Routee routee)
        {
            AddRoutees(new[] { routee });
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routees">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <param name="stopChild">TBD</param>
        internal void RemoveRoutee(Routee routee, bool stopChild)
        {
            RemoveRoutees(new[] { routee }, stopChild);
        }

        /// <summary>
        /// Remove routees from <see cref="Router"/>. Messages in flight may still
        /// be routed to the old <see cref="Router"/> instance containing the old routees.
        /// </summary>
        /// <param name="affectedRoutees">TBD</param>
        /// <param name="stopChild">TBD</param>
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
        /// <param name="routee">TBD</param>
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
        /// TBD
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
                var deprecatedPaths = group.Paths;

                var paths = deprecatedPaths == null
                        ? group.GetPaths(System)?.ToArray()
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        public override void SendMessage(Envelope envelope)
        {
            if (RouterConfig.IsManagementMessage(envelope.Message))
                base.SendMessage(envelope);
            else
                Router.Route(envelope.Message, envelope.Sender);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override ActorBase CreateNewActorInstance()
        {
            RouterActor instance = RouterConfig.CreateRouterActor();
            return instance;
        }
    }
}
