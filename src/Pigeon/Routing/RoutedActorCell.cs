using System.Linq;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Routing
{
    public class RoutedActorCell : ActorCell
    {
        public RoutedActorCell(ActorSystem system, InternalActorRef supervisor, Props props, ActorPath path,
            Mailbox mailbox) : base(system, supervisor, props, path, mailbox)
        {
            RouterConfig routerConfig = props.RouterConfig;
            Routee[] routees = routerConfig.GetRoutees(System).ToArray();
            Router = routerConfig.CreateRouter().WithRoutees(routees);
            Self = new RoutedActorRef(Router, path, this);
        }

        public Router Router { get; private set; }

        public override void NewActor()
        {
            //set the thread static context or things will break
            UseThreadContext(() =>
            {
                //TODO: where should deployment be handled?
                Deploy deploy = System.Provider.Deployer.Lookup(Self.Path);
                behaviorStack.Clear();
                RouterActor instance = Props.RouterConfig.CreateRouterActor();
                instance.supervisorStrategy = Props.SupervisorStrategy;
                    //defaults to null - won't affect lazy instantion unless explicitly set in props
                instance.AroundPreStart();
            });
        }
    }
}