using Pigeon.Actor;
using Pigeon.Dispatch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RoutedActorCell : ActorCell
    {
        public RoutedActorCell(ActorSystem system,InternalActorRef supervisor,Props props,ActorPath path,Mailbox mailbox) :base(system,supervisor,props,path,mailbox)
        {
            var routerConfig = props.RouterConfig;
            var routees = routerConfig.GetRoutees(this.System).ToArray();
            this.Router = new Routing.Router(routerConfig.GetLogic(),routees);            
            Self = new RoutedActorRef(this.Router,path,this);
        }
        public Router Router { get; private set; }

        public override void NewActor()
        {
            //set the thread static context or things will break
            this.UseThreadContext(() =>
            {
                //TODO: where should deployment be handled?
                var deployPath = Self.Path.ToStringWithoutAddress();
                var deploy = System.Deployer.Lookup(deployPath);
                behaviorStack.Clear();
                var instance = Props.RouterConfig.CreateRouterActor();
                instance.supervisorStrategy = Props.SupervisorStrategy; //defaults to null - won't affect lazy instantion unless explicitly set in props
                instance.AroundPreStart();
            });
        }
    }
}
