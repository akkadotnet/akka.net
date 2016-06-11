//-----------------------------------------------------------------------
// <copyright file="RouterConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// This class provides base functionality used in the creation and configuration of the various routers in the system.
    /// </summary>
    public abstract class RouterConfig : ISurrogated, IEquatable<RouterConfig>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RouterConfig"/> class.
        /// </summary>
        protected RouterConfig()
        {
            StopRouterWhenAllRouteesRemoved = true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RouterConfig"/> class.
        /// <note>
        /// This method defaults to setting the dispatcher to use the <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to routees.</param>
        protected RouterConfig(string routerDispatcher)
        {
            RouterDispatcher = routerDispatcher ?? Dispatchers.DefaultDispatcherId;
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        /// </summary>
        /// <param name="system">The ActorSystem this router belongs to.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public abstract Router CreateRouter(ActorSystem system);

        /// <summary>
        /// Dispatcher ID to use for running the “head” actor, which handles supervision, death watch and router management messages.
        /// </summary>
        public virtual string RouterDispatcher { get; }

        /// <summary>
        /// Possibility to define an actor for controlling the routing
        /// logic from external stimuli(e.g.monitoring metrics).
        /// This actor will be a child of the router "head" actor.
        /// Management messages not handled by the "head" actor are
        /// delegated to this controller actor.
        /// </summary>
        public virtual Props RoutingLogicController(RoutingLogic routingLogic)
        {
            return null;
        }

        /// <summary>
        /// Determines whether a provided message is handled by the router.
        /// </summary>
        /// <param name="message">The message to inspect.</param>
        /// <returns><c>true</c> if this message is handled by the router; otherwise <c>false</c>.</returns>
        public virtual bool IsManagementMessage(object message)
        {
            return message is IAutoReceivedMessage || message is RouterManagementMessage;
        }

        /// <summary>
        /// Specify that this router should stop itself when all routees have terminated (been removed).
        /// By Default it is `true`, unless a `resizer` is used.
        /// </summary>
        public virtual bool StopRouterWhenAllRouteesRemoved { get; }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        public virtual RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return this;
        }

        /// <summary>
        /// Check that everything is there which is needed. Called in constructor of RoutedActorRef to fail early.
        /// </summary>
        public virtual void VerifyConfig(ActorPath path)
        {
        }

        /// <summary>
        /// The router "head" actor.
        /// </summary>
        internal abstract RouterActor CreateRouterActor();

        /// <summary>
        /// Creates a surrogate representation of the current router.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current router.</returns>
        public abstract ISurrogate ToSurrogate(ActorSystem system);

        /// <summary>		
        /// Determines whether the specified router, is equal to this instance.		
        /// </summary>		
        /// <param name="other">The router to compare.</param>		
        /// <returns><c>true</c> if the specified router is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(RouterConfig other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return GetType() == other.GetType() && (GetType() == typeof(NoRouter) || string.Equals(RouterDispatcher, other.RouterDispatcher));
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RouterConfig);
        }
    }

    /// <summary>
    /// This class provides base functionality for all group routers in the system.
    /// Group routers are routers that use already created routees. These routees
    /// are supplied to the router and are addressed through <see cref="ActorSelection"/>
    /// paths.
    /// </summary>
    public abstract class Group : RouterConfig, IEquatable<Group>
    {
        protected Group(IEnumerable<string> paths, string routerDispatcher) : base(routerDispatcher)
        {
            Paths = paths;
        }

        public IEnumerable<string> Paths { get; }

        /// <summary>
        /// Retrieves the actor paths used by this router during routee selection.
        /// </summary>
        public abstract IEnumerable<string> GetPaths(ActorSystem system);

        /// <summary>
        /// Adds the current router to an empty <see cref="Actor.Props"/>.
        /// </summary>
        /// <returns>An empty <see cref="Actor.Props"/> configured to use the current router.</returns>
        public Props Props()
        {
            return Actor.Props.Empty.WithRouter(this);
        }

        internal Routee RouteeFor(string path, IActorContext context)
        {
            return new ActorSelectionRoutee(context.ActorSelection(path));
        }

        internal override RouterActor CreateRouterActor()
        {
            return new RouterActor();
        }

        /// <summary>
        /// Determines whether the specified <see cref="Group"/>, is equal to this instance.
        /// </summary>
        /// <param name="other">The group to compare.</param>
        /// <returns><c>true</c> if the specified <see cref="Group"/> is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(Group other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Paths.SequenceEqual(other.Paths);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object"/>, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object"/> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object"/> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Group)obj);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return Paths?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// This class provides base functionality for all pool routers in the system.
    /// Pool routers are routers that create their own routees based on the provided
    /// configuration.
    /// </summary>
    public abstract class Pool : RouterConfig, IEquatable<Pool>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Pool"/> class.
        /// <note>
        /// If a <paramref name="routerDispatcher"/> is not provided, this constructor sets up
        /// the pool to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        protected Pool(
            int nrOfInstances,
            Resizer resizer,
            SupervisorStrategy supervisorStrategy,
            string routerDispatcher,
            bool usePoolDispatcher) : base(routerDispatcher)
        {
            NrOfInstances = nrOfInstances;
            Resizer = resizer;
            SupervisorStrategy = supervisorStrategy;
            UsePoolDispatcher = usePoolDispatcher;
        }

        public int NrOfInstances { get; }

        /// <summary>
        /// Used by the <see cref="RoutedActorCell"/> to determine the initial number of routees.
        /// </summary>
        /// <param name="system"></param>
        /// <returns>The number of routees associated with this pool.</returns>
        public abstract int GetNrOfInstances(ActorSystem system);

        /// <summary>
        /// Retrieve whether or not to use the pool dispatcher. The dispatcher is defined in the
        /// 'pool-dispatcher' configuration property in the deployment section of the router.
        /// </summary>
        public virtual bool UsePoolDispatcher { get; } = false;

        /// <summary>
        /// Creates a new <see cref="Routee"/> configured to use the provided <paramref name="routeeProps"/>
        /// and the pool dispatcher if enabled.
        /// </summary>
        /// <param name="routeeProps">The <see cref="Actor.Props"/> to configure with the pool dispatcher.</param>
        /// <param name="context">The context for the provided <paramref name="routeeProps"/>.</param>
        /// <returns>
        /// A new <see cref="Routee"/> configured to use the provided <paramref name="routeeProps"/>
        /// and the pool dispatcher if enabled.
        /// </returns>
        internal virtual Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            return new ActorRefRoutee(context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context)));
        }

        internal Props EnrichWithPoolDispatcher(Props routeeProps, IActorContext context)
        {
            if (UsePoolDispatcher && routeeProps.Dispatcher == Dispatchers.DefaultDispatcherId)
            {
                return routeeProps
                    .WithDispatcher("akka.actor.deployment." + "/" +
                                               context.Self.Path.Elements.Drop(1).Join("/") +
                                               ".pool-dispatcher");
            }

            return routeeProps;
        }

        /// <summary>
        /// Retrieve the resizer to use when dynamically allocating routees to the pool.
        /// </summary>
        public virtual Resizer Resizer { get; }

        /// <summary>
        /// Retrieve the strategy to use when supervising the pool.
        /// </summary>
        public virtual SupervisorStrategy SupervisorStrategy { get; }

        /// <summary>
        /// Adds the current router to the provided <paramref name="routeeProps"/>.
        /// </summary>
        /// <param name="routeeProps">The <see cref="Actor.Props"/> to configure with the current router.</param>
        /// <returns>The provided <paramref name="routeeProps"/> configured to use the current router.</returns>
        public Props Props(Props routeeProps)
        {
            return routeeProps.WithRouter(this);
        }

        /// <summary>
        /// Specify that this router should stop itself when all routees have terminated (been removed).
        /// </summary>
        public override bool StopRouterWhenAllRouteesRemoved
        {
            get
            {
                return Resizer == null;
            }
        }

        internal override RouterActor CreateRouterActor()
        {
            if (Resizer == null)
                return new RouterPoolActor(SupervisorStrategy);

            return new ResizablePoolActor(SupervisorStrategy);
        }

        public static SupervisorStrategy DefaultSupervisorStrategy
        {
            get
            {
                return new OneForOneStrategy(Decider.From(Directive.Escalate));
            }
        }

        /// <summary>
        /// Determines whether the specified <see cref="Pool"/>, is equal to this instance.
        /// </summary>
        /// <param name="other">The pool to compare.</param>
        /// <returns><c>true</c> if the specified <see cref="Pool"/> is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(Pool other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Resizer, other.Resizer) && UsePoolDispatcher.Equals(other.UsePoolDispatcher) &&
                   NrOfInstances == other.NrOfInstances;
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object"/>, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object"/> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object"/> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Pool)obj);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Resizer?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ UsePoolDispatcher.GetHashCode();
                hashCode = (hashCode * 397) ^ NrOfInstances;
                return hashCode;
            }
        }
    }

    /// <summary>
    /// If a custom router implementation is not a <see cref="Group"/> nor 
    /// a <see cref="Pool"/> it may extend this base class.
    /// </summary>
    public abstract class CustomRouterConfig : RouterConfig
    {
        protected CustomRouterConfig() : base(Dispatchers.DefaultDispatcherId)
        {
        }

        protected CustomRouterConfig(string routerDispatcher) : base(routerDispatcher)
        {
        }

        internal override RouterActor CreateRouterActor()
        {
            return new RouterActor();
        }
    }

    /// <summary>
    /// Router configuration which has no default, i.e. external configuration is required.
    /// This can be used when the dispatcher to be used for the head Router needs to be configured
    /// </summary>
    public class FromConfig : Pool
    {
        protected FromConfig() : this(null, DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId)
        {
        }

        protected FromConfig(Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher)
            : base(0, resizer, supervisorStrategy, routerDispatcher, false)
        {
        }

        /// <summary>
        /// Retrieves a <see cref="RouterConfig"/> based on what's stored in the configuration.
        /// <note>
        /// This router is set to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        public static FromConfig Instance { get; } = new FromConfig();

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>
        /// The newly created router tied to the given system.
        /// </returns>
        /// <exception cref="NotSupportedException"></exception>
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotSupportedException("FromConfig must not create Router");
        }

        internal override RouterActor CreateRouterActor()
        {
            throw new NotSupportedException("FromConfig must not create RouterActor");
        }

        public override void VerifyConfig(ActorPath path)
        {
            throw new ConfigurationException($"Configuration missing for router [{path}] in 'akka.actor.deployment' section.");
        }

        /// <summary>
        /// Setting the supervisor strategy to be used for the “head” Router actor
        /// </summary>
        public FromConfig WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new FromConfig(Resizer, strategy, RouterDispatcher);
        }

        /// <summary>
        /// Setting the resizer to be used.
        /// </summary>
        public FromConfig WithResizer(Resizer resizer)
        {
            return new FromConfig(resizer, SupervisorStrategy, RouterDispatcher);
        }

        /// <summary>
        /// Setting the dispatcher to be used for the router head actor, which handles
        /// supervision, death watch and router management messages.
        /// </summary>
        public FromConfig WithDispatcher(string dispatcherId)
        {
            return new FromConfig(Resizer, SupervisorStrategy, dispatcherId);
        }

        public override int GetNrOfInstances(ActorSystem sys)
        {
            return 0;
        }

        /// <summary>
        /// Enriches a <see cref="Akka.Actor.Props"/> with what what's stored in the router configuration.
        /// </summary>
        /// <returns></returns>
        public Props Props()
        {
            return Actor.Props.Empty.WithRouter(Instance);
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="FromConfig"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class FromConfigSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="FromConfig"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="FromConfig"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return Instance;
            }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="FromConfig"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="FromConfig"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new FromConfigSurrogate();
        }
    }

    /// <summary>
    /// Routing configuration that indicates no routing; this is also the default
    /// value which hence overrides the merge strategy in order to accept values
    /// from lower-precedence sources. The decision whether or not to create a
    /// router is taken in the <see cref="LocalActorRefProvider"/> based on <see cref="Props"/>.
    /// </summary>
    public class NoRouter : RouterConfig
    {
        protected NoRouter()
        {
            
        }

        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotSupportedException("NoRouter has no Router");
        }

        internal override RouterActor CreateRouterActor()
        {
            throw new NotSupportedException("NoRouter must not create RouterActor");
        }

        public override string RouterDispatcher
        {
            get
            {
                throw new NotSupportedException("NoRouter has no dispatcher");
            }
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return routerConfig;
        }

        public Props Props(Props routeeProps)
        {
            return routeeProps.WithRouter(this);
        }

        public static NoRouter Instance { get; } = new NoRouter();

        /// <summary>
        /// This class represents a surrogate of a <see cref="NoRouter"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class NoRouterSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="NoRouter"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="NoRouter"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new NoRouter();
            }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="NoRouter"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="NoRouter"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new NoRouterSurrogate();
        }
    }
}