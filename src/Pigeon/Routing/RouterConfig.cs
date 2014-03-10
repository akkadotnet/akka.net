using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Routing
{
    public abstract class RouterConfig
    {
        //  public abstract RoutingLogic GetLogic();

        public static readonly RouterConfig NoRouter = new NoRouter();
        public string RouterDispatcher { get; protected set; }

        public virtual RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return this;
        }

        public abstract Router CreateRouter(ActorSystem system);
        public abstract RouterActor CreateRouterActor();

        public abstract IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell);

    }

    public class NoRouter : RouterConfig
    {
        public override RouterActor CreateRouterActor()
        {
            throw new NotImplementedException();
        }

        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            throw new NotImplementedException();
        }
    }

    public abstract class Group : RouterConfig
    {
        private string[] paths;

        public string[] Paths
        {
            get { return paths; }
            set { paths = value; }
        }

        protected Group()
        {
        }

        protected Group(IEnumerable<string> paths)
        {
            this.paths = paths.ToArray();
        }

        protected Group(IEnumerable<ActorRef> routees)
        {
            paths = routees.Select(x => x.Path.ToStringWithAddress()).ToArray();
        }

        public override RouterActor CreateRouterActor()
        {
            return new RouterActor();
        }

        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            return paths.Select(routedActorCell.System.ActorSelection).Select(actor => new ActorSelectionRoutee(actor));
        }
    }

    public abstract class Pool : RouterConfig
    {
        protected Pool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher,
            bool usePoolDispatcher = false)
        {
            NrOfInstances = nrOfInstances;
            Resizer = resizer;
            SupervisorStrategy = supervisorStrategy;
            UsePoolDispatcher = usePoolDispatcher;
            RouterDispatcher = routerDispatcher;
        }

        protected Pool(Configuration.Config config)
        {
            NrOfInstances = config.GetInt("nr-of-instances");

        }

        public int NrOfInstances { get; protected set; }

        public bool UsePoolDispatcher { get; protected set; }

        public Resizer Resizer { get; protected set; }
        public SupervisorStrategy SupervisorStrategy { get; protected set; }

        public Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var routee = new ActorRefRoutee(context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context)));
            return routee;
        }

        private Props EnrichWithPoolDispatcher(Props routeeProps, IActorContext context)
        {
            //        if (usePoolDispatcher && routeeProps.dispatcher == Dispatchers.DefaultDispatcherId)
            //  routeeProps.withDispatcher("akka.actor.deployment." + context.self.path.elements.drop(1).mkString("/", "/", "")
            //    + ".pool-dispatcher")
            //else
            //  routeeProps
            if (UsePoolDispatcher && routeeProps.Dispatcher == Dispatchers.DefaultDispatcherId)
            {
                return
                    routeeProps.WithDispatcher("akka.actor.deployment." + context.Self.Path.Elements.Drop(1).Join("/") +
                                               ".pool-dispatcher");
            }
            return routeeProps;
        }

        public Props Props(Props routeeProps)
        {
            return routeeProps.WithRouter(this);
        }

        public override RouterActor CreateRouterActor()
        {
            if (Resizer == null)
                return new RouterPoolActor(SupervisorStrategy);
            return new ResizablePoolActor(SupervisorStrategy);
        }

        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            for (int i = 0; i < NrOfInstances; i++)
            {
                //TODO: where do we get props?
                yield return NewRoutee(Akka.Actor.Props.Empty , routedActorCell);
            }
        }
    }
}