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
using Akka.Dispatch.SysMsg;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class RoutedActorCell : ActorCell
    {
        private readonly RouterConfig _routerConfig;
        private volatile Router _router;
        private readonly Props _routeeProps;


        public RoutedActorCell(ActorSystemImpl system, IInternalActorRef self, Props routerProps, MessageDispatcher dispatcher, Props routeeProps, IInternalActorRef supervisor)
            : base(system, self, routerProps, dispatcher, supervisor)
        {
            _routeeProps = routeeProps;
            _routerConfig = routerProps.RouterConfig;
        }

        public Router Router { get { return _router; } }

        public Props RouteeProps { get { return _routeeProps; } }

        public RouterConfig RouterConfig
        {
            get { return _routerConfig; }
        }


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
            _router = _router.WithRoutees(_router.Routees.Concat(routees).ToArray());
        }

        protected override ActorBase CreateNewActorInstance()
        {
            RouterActor instance = _routerConfig.CreateRouterActor();
            return instance;
        }

        /// <summary>
        /// Remove routees from <see cref="Router"/>. Messages in flight may still
        /// be routed to the old <see cref="Router"/> instance containing the old routees.
        /// </summary>
        /// <param name="affectedRoutees"></param>
        /// <param name="stopChild"></param>
        internal void RemoveRoutees(IList<Routee> affectedRoutees, bool stopChild)
        {
            var routees = _router.Routees
                .Where(routee => !affectedRoutees.Contains(routee))
                .ToArray();

            _router = _router.WithRoutees(routees);

            foreach (var affectedRoutee in affectedRoutees)
            {
                Unwatch(affectedRoutee);
                if (stopChild)
                    StopIfChild(affectedRoutee);
            }

        }

        private void Unwatch(Routee routee)
        {
            var actorRef = routee as ActorRefRoutee;
            if (actorRef != null) Unwatch(actorRef.Actor);
        }

        private void Watch(Routee routee)
        {
            var actorRef = routee as ActorRefRoutee;
            if (actorRef != null) Watch(actorRef.Actor);
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
                // The reason for the delay is to give concurrent
                // messages a chance to be placed in mailbox before sending PoisonPill,
                // best effort.
                System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(100), actorRefRoutee.Actor, PoisonPill.Instance, Self);
            }
        }

        public override void Start()
        {
            // create the initial routees before scheduling the Router actor
            _router = _routerConfig.CreateRouter(System);
            _routerConfig.Match()
                .With<Pool>(pool =>
                {
                    var nrOfRoutees = pool.GetNrOfInstances(System);
                    if (nrOfRoutees > 0)
                        AddRoutees(Vector.Fill<Routee>(nrOfRoutees)(() => pool.NewRoutee(_routeeProps, this)));
                })
                .With<Group>(group =>
                {
                    var paths = group.Paths;
                    if (paths.NonEmpty())
                        AddRoutees(paths.Select(p => group.RouteeFor(p, this)).ToList());
                });
            PreSuperStart();
            base.Start();
        }

        /// <summary>
        /// Called when <see cref="Router"/> is initialized but before the base class' <see cref="Start"/> to
        /// be able to do extra initialization in a subclass.
        /// </summary>
        protected virtual void PreSuperStart() { }

        internal void RemoveRoutee(Routee routee, bool stopChild)
        {
            RemoveRoutees(new List<Routee> { routee }, stopChild);
        }

        public override void SendMessage(IActorRef sender, object message)
        {
            if (_routerConfig.IsManagementMessage(message))
                base.SendMessage(sender, message);
            else
                _router.Route(message, sender);
        }
    }
}