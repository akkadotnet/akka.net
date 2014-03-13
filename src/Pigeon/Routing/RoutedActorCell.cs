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
        public RoutedActorCell(ActorSystem system, InternalActorRef supervisor, Props routerProps,Props routeeProps, ActorPath path,
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
       
        private void AddRoutees(Routee[] routees)
        {
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

        
    }
}