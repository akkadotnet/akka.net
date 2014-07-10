using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;

namespace Akka.Routing
{
    public class RoutedActorCell : ActorCell
    {
        private readonly RouterConfig _routerConfig;
        private Router _router;
        private readonly Props _routeeProps;


        public RoutedActorCell(ActorSystem system, InternalActorRef self, Props routerProps, MessageDispatcher dispatcher, Props routeeProps, InternalActorRef supervisor)
            : base(system, self, routerProps, dispatcher, supervisor)
        {
            _routeeProps = routeeProps;
            _routerConfig = routerProps.RouterConfig;
            _router = _routerConfig.CreateRouter(system);
            _routerConfig.Match()
                .With<Pool>(r =>
                {
                    var routees = new List<Routee>();
                    for(int i = 0; i < r.NrOfInstances; i++)
                    {
                        var routee = ActorOf(_routeeProps);
                        routees.Add(new ActorRefRoutee(routee));
                    }
                    AddRoutees(routees.ToArray());
                })
                .With<Group>(r =>
                {
                    var routees = _routerConfig.GetRoutees(this).ToArray();
                    AddRoutees(routees);
                });
        }

        public Router Router { get { return _router; } }

        public Props RouteeProps { get { return _routeeProps; } }


        protected void AddRoutees(Routee[] routees)
        {
            foreach(var routee in routees)
            {
                if(routee is ActorRefRoutee)
                {
                    var @ref = ((ActorRefRoutee)routee).Actor;
                    Watch(@ref);
                }
            }
            _router = _router.WithRoutees(routees);
        }

        protected override ActorBase CreateNewActorInstance()
        {
            RouterActor instance = _routerConfig.CreateRouterActor();
            return instance;
        }



        internal void RemoveRoutee(ActorRef actorRef, bool stopChild)
        {
            var routees = _router.Routees.ToList();
            routees.RemoveAll(r =>
            {
                var routee = r as ActorRefRoutee;
                if(routee != null)
                {
                    return routee.Actor == actorRef;
                }
                return false;
            });
            _router = _router.WithRoutees(routees.ToArray());
            if(stopChild)
            {

            }
        }

        public override void Post(ActorRef sender, object message)
        {
            if(message is SystemMessage) base.Post(sender, message);
            else SendMessage(sender, message);
        }

        private void SendMessage(ActorRef sender, object message)
        {
            //Route the message via the router to the selected destination.
            if(_routerConfig.IsManagementMessage(message))
                base.Post(sender, message);
            else
                _router.Route(message, sender);
        }
    }
}