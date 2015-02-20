using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;

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


        public RoutedActorCell(ActorSystemImpl system, InternalActorRef self, Props routerProps, MessageDispatcher dispatcher, Props routeeProps, InternalActorRef supervisor)
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

        internal void AddRoutees(Routee[] routees)
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
        internal void RemoveRoutees(IEnumerable<Routee> affectedRoutees, bool stopChild)
        {
            var oldRouter = _router;
            var routees = _router.Routees.ToList();
            routees.RemoveAll(r =>
            {
                var routee = r as ActorRefRoutee;
                if (routee != null)
                {
                    return affectedRoutees.Contains(routee);
                }
                return false;
            });
            _router = oldRouter.WithRoutees(routees.ToArray());
            foreach (var affectedRoutee in affectedRoutees)
            {
                Unwatch(affectedRoutee);
                if(stopChild)
                    StopIfChild(affectedRoutee);
            }

        }

        private void Unwatch(Routee routee)
        {
            var actorRef = routee as ActorRefRoutee;
            if (actorRef != null) base.Unwatch(actorRef.Actor);
        }

        private void Watch(Routee routee)
        {
            var actorRef = routee as ActorRefRoutee;
            if (actorRef != null) base.Watch(actorRef.Actor);
        }

        /// <summary>
        /// Used to stop child routees - typically used in resizable <see cref="Pool"/> routers
        /// </summary>
        /// <param name="routee"></param>
        private void StopIfChild(Routee routee)
        {
            var actorRefRoutee = routee as ActorRefRoutee;
            ChildStats childActorStats;
            if (actorRefRoutee != null && TryGetChildStatsByName(actorRefRoutee.Actor.Path.Name, out childActorStats))
            {
                // The reason for the delay is to give concurrent
                // messages a chance to be placed in mailbox before sending PoisonPill,
                // best effort.
                System.Scheduler.ScheduleOnce(TimeSpan.FromMilliseconds(100), actorRefRoutee.Actor, PoisonPill.Instance);
            }
        }

        public override void Start()
        {
            // create the initial routees before scheduling the Router actor
            _router = _routerConfig.CreateRouter(System);
            _routerConfig.Match()
                .With<Pool>(r =>
                {
                    var routees = new List<Routee>();
                    for (var i = 0; i < r.NrOfInstances; i++)
                    {
                        var routee = r.NewRoutee(_routeeProps, this);
                        routees.Add(routee);
                    }
                    AddRoutees(routees.ToArray());
                })
                .With<Group>(r =>
                {
                    var routees = _routerConfig.GetRoutees(this).ToArray();
                    AddRoutees(routees);
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
            RemoveRoutees(new List<Routee>() { routee }, stopChild);
        }

        public override void Post(ActorRef sender, object message)
        {
            if (message is SystemMessage) base.Post(sender, message);
            else SendMessage(sender, message);
        }

        private void SendMessage(ActorRef sender, object message)
        {
            //Route the message via the router to the selected destination.
            if (_routerConfig.IsManagementMessage(message))
                base.Post(sender, message);
            else
                _router.Route(message, sender);
        }
    }
}