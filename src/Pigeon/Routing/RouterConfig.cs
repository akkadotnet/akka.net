using Pigeon.Actor;
using Pigeon.Dispatch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public abstract class RouterConfig
    {
        public abstract RoutingLogic GetLogic();

        public abstract IEnumerable<Routee> GetRoutees(ActorSystem system);

        public static readonly RouterConfig NoRouter = new NoRouter();

        public virtual RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return this;
        }

        public abstract RouterActor CreateRouterActor();
    }

    public class NoRouter : RouterConfig
    {
        public override RoutingLogic GetLogic()
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Routee> GetRoutees(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override RouterActor CreateRouterActor()
        {
            throw new NotImplementedException();
        }
    }

    public abstract class Group : RouterConfig
    {
        private string[] paths;

        public Group(IEnumerable<string> paths)
        {
            this.paths = paths.ToArray();
        }

        protected Group(IEnumerable<ActorRef> routees)
        {
            this.paths = routees.Select(x => x.Path.ToStringWithAddress()).ToArray();
        }

        public override IEnumerable<Routee> GetRoutees(ActorSystem system)
        {
            foreach(var path in paths)
            {
                var actor = system.ActorSelection(path);
                yield return new ActorSelectionRoutee(actor);
            }
        }

        public override RouterActor CreateRouterActor()
        {
            return new RouterActor();
        }
    }

    public abstract class Pool : RouterConfig
    {
        public int NrOfInstances { get;private set; }

        public bool UsePoolDispatcher { get; private set; }

 //        private[akka] def newRoutee(routeeProps: Props, context: ActorContext): Routee =
 //   ActorRefRoutee(context.actorOf(enrichWithPoolDispatcher(routeeProps, context)))


        public Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var routee = new ActorRefRoutee(context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context)));
            return routee;
        }

        private Props EnrichWithPoolDispatcher(Props routeeProps,IActorContext context)
        {
    //        if (usePoolDispatcher && routeeProps.dispatcher == Dispatchers.DefaultDispatcherId)
    //  routeeProps.withDispatcher("akka.actor.deployment." + context.self.path.elements.drop(1).mkString("/", "/", "")
    //    + ".pool-dispatcher")
    //else
    //  routeeProps
            if (UsePoolDispatcher && routeeProps.Dispatcher == Dispatchers.DefaultDispatcherId)
            {
                return routeeProps.WithDispatcher("akka.actor.deployment." + string.Join("/", context.Self.Path.Elements.Skip(1)) + ".pool-dispatcher");
            }
            return routeeProps;
        }

        public Resizer Resizer { get;private set; }
        public SupervisorStrategy SupervisorStrategy { get;private set; }

        public Props Props(Props routeeProps)
        {
            return routeeProps.WithRouter(this);
        }
               
    }
}
