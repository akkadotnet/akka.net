using System.Linq;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Routing
{
    public class RoutedActorCell : ActorCell
    {
        private readonly RouterConfig routerConfig;
        public Router Router { get; private set; }
        public RoutedActorCell(ActorSystem system, InternalActorRef supervisor, Props props, ActorPath path,
            Mailbox mailbox) : base(system, supervisor, props, path, mailbox)
        {
            routerConfig = props.RouterConfig;
            Router = routerConfig.CreateRouter(system);
            var routees = routerConfig.GetRoutees(this).ToArray();
            AddRoutees(routees);
            Self = new RoutedActorRef(Router, path, this);            
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