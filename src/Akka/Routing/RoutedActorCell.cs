using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Routing
{
    public class RoutedActorCell : ActorCell
    {
        private readonly RouterConfig routerConfig;
        public Router Router { get; private set; }
        public Props RouteeProps { get; private set; }
        public RoutedActorCell(ActorSystem system, InternalActorRef supervisor, Props routerProps, Props routeeProps, ActorPath path,
            Mailbox mailbox)
            : base(system, supervisor, routerProps, path, mailbox)
        {
            RouteeProps = routeeProps;
            routerConfig = routerProps.RouterConfig;
            Router = routerConfig.CreateRouter(system);
            routerConfig.Match()
                .With<Pool>(r =>
                {
                    var routees = new List<Routee>();
                    for (int i = 0; i < r.NrOfInstances; i++)
                    {
                        var routee = this.ActorOf(RouteeProps);
                        routees.Add(new ActorRefRoutee(routee));
                    }
                    AddRoutees(routees.ToArray());
                })
                .With<Group>(r =>
                {
                    var routees = routerConfig.GetRoutees(this).ToArray();
                    AddRoutees(routees);
                });



            Self = new RoutedActorRef(path, this);
        }

        protected void AddRoutees(Routee[] routees)
        {
            foreach (var routee in routees)
            {
                if (routee is ActorRefRoutee)
                {
                    var @ref = ((ActorRefRoutee)routee).Actor;
                    Watch(@ref);
                }
            }
            Router = Router.WithRoutees(routees);
        }

        public override void NewActor()
        {
            //set the thread static context or things will break
            UseThreadContext(() =>
            {
                behaviorStack.Clear();
                RouterActor instance = routerConfig.CreateRouterActor();
                instance.supervisorStrategy = Props.SupervisorStrategy;
                //defaults to null - won't affect lazy instantion unless explicitly set in props
                instance.AroundPreStart();
            });
        }



        internal void RemoveRoutee(ActorRef actorRef, bool stopChild)
        {
            var routees = this.Router.Routees.ToList();
            routees.RemoveAll(r =>
            {
                var routee = r as ActorRefRoutee;
                if (routee != null)
                {
                    return routee.Actor == actorRef;
                }
                return false;
            });
            Router = Router.WithRoutees(routees.ToArray());
            if (stopChild)
            {
                
            }
        }
    }
}