using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Util.Internal;

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

        public virtual bool IsManagementMessage(object message)
        {
            return 
                message is AutoReceivedMessage || 
                // in akka.net this message is a subclass of AutoReceivedMessage - so removed condition that "message is Terminated ||"
                message is RouterManagementMesssage;
        }
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
            set { paths = value; } //should be private, fails for serialization atm, JSON.NET should be able to set private setters, right?
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

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal Routee RouteeFor(string path, IActorContext context)
        {
            return new ActorSelectionRoutee(context.ActorSelection(path));
        }

        public Props Props()
        {
            return Akka.Actor.Props.Empty.WithRouter(this);
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
            return paths.Select(((ActorSystemImpl) routedActorCell.System).ActorSelection).Select(actor => new ActorSelectionRoutee(actor));
        }
    }


    //TODO: ensure this can be serialized/deserialized fully
    public abstract class Pool : RouterConfig
    {
        protected Pool() //for serialization
        {            
        }

        protected Pool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher,
            bool usePoolDispatcher = false)
        {
            NrOfInstances = nrOfInstances;
            Resizer = resizer;
            SupervisorStrategy = supervisorStrategy ?? Pool.DefaultStrategy;
            UsePoolDispatcher = usePoolDispatcher;
            RouterDispatcher = routerDispatcher;
        }

        protected Pool(Configuration.Config config)
        {
            NrOfInstances = config.GetInt("nr-of-instances");
            Resizer = DefaultResizer.FromConfig(config);
            UsePoolDispatcher = config.HasPath("pool-dispatcher");
        }

        /// <summary>
        /// The number of instances in the pool.
        /// </summary>
        public int NrOfInstances { get; set; }

        /// <summary>
        /// Whether or not to use the pool dispatcher.
        /// </summary>
        public bool UsePoolDispatcher { get; set; }

        /// <summary>
        /// An instance of the resizer for this pool.
        /// </summary>
        public Resizer Resizer { get; set; }

        /// <summary>
        /// An instance of the supervisor strategy for this pool.
        /// </summary>
        public SupervisorStrategy SupervisorStrategy { get; set; }

        public virtual Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var routee = new ActorRefRoutee(context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context)));
            return routee;
        }

        protected Props EnrichWithPoolDispatcher(Props routeeProps, IActorContext context)
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

        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            for (int i = 0; i < NrOfInstances; i++)
            {
                //TODO: where do we get props?
                yield return NewRoutee(Akka.Actor.Props.Empty , routedActorCell);
            }
        }

        #region Static methods

        /// <summary>
        ///     When supervisorStrategy is not specified for an actor this
        ///     is used by default. OneForOneStrategy with a decider which escalates by default.
        /// </summary>
        public static SupervisorStrategy DefaultStrategy
        {
            get { return new OneForOneStrategy(10, TimeSpan.FromSeconds(10), (ex) => Directive.Escalate); }
        }

        #endregion
    }

    public class FromConfig : RouterConfig
    {
        public FromConfig() : base()
        {
            
        }
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotSupportedException();
        }

        public override RouterActor CreateRouterActor()
        {
            throw new NotSupportedException();
        }

        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            throw new NotSupportedException();
        }
    }
}