//-----------------------------------------------------------------------
// <copyright file="RouterConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
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
        /// A configuration that specifies that no router is to be used.
        /// </summary>
        [Obsolete("Use NoRouter.Instance instead [1.1.0]")]
        public static RouterConfig NoRouter => Routing.NoRouter.Instance;

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        /// </summary>
        /// <param name="system">The ActorSystem this router belongs to.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public abstract Router CreateRouter(ActorSystem system);

        /// <summary>
        /// Dispatcher ID to use for running the "head" actor, which handles supervision, death watch and router management messages.
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
        /// <param name="path">TBD</param>
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

        /// <inheritdoc/>
        public bool Equals(RouterConfig other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return GetType() == other.GetType() && (GetType() == typeof(NoRouter) || string.Equals(RouterDispatcher, other.RouterDispatcher));
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as RouterConfig);
    }

    /// <summary>
    /// This class provides base functionality for all group routers in the system.
    /// Group routers are routers that use already created routees. These routees
    /// are supplied to the router and are addressed through <see cref="ActorSelection"/>
    /// paths.
    /// </summary>
    public abstract class Group : RouterConfig, IEquatable<Group>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="paths">TBD</param>
        /// <param name="routerDispatcher">TBD</param>
        protected Group(IEnumerable<string> paths, string routerDispatcher) : base(routerDispatcher)
        {
            // equivalent of turning the paths into an immutable sequence
            InternalPaths = paths?.ToArray() ?? new string[0];
        }

        /// <summary>
        /// Internal property for holding the supplied paths
        /// </summary>
        protected readonly string[] InternalPaths;

        /// <summary>
        /// Retrieves the paths of all routees declared on this router.
        /// </summary>
        [Obsolete("Deprecated since Akka.NET v1.1. Use Paths(ActorSystem) instead.")]
        public IEnumerable<string> Paths => null;

        /// <summary>
        /// Retrieves the actor paths used by this router during routee selection.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>An enumeration of actor paths used during routee selection</returns>
        public abstract IEnumerable<string> GetPaths(ActorSystem system);

        /// <summary>
        /// Adds the current router to an empty <see cref="Actor.Props"/>.
        /// </summary>
        /// <returns>An empty <see cref="Actor.Props"/> configured to use the current router.</returns>
        public Props Props()
        {
            return Actor.Props.Empty.WithRouter(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        internal Routee RouteeFor(string path, IActorContext context)
        {
            return new ActorSelectionRoutee(context.ActorSelection(path));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        internal override RouterActor CreateRouterActor()
        {
            return new RouterActor();
        }

        /// <inheritdoc/>
        public bool Equals(Group other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return InternalPaths.SequenceEqual(other.InternalPaths);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Group)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => InternalPaths?.GetHashCode() ?? 0;
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

        /// <summary>
        /// Retrieves the current number of routees in the pool.
        /// </summary>
        public int NrOfInstances { get; }

        /// <summary>
        /// Used by the <see cref="RoutedActorCell"/> to determine the initial number of routees.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routeeProps">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        internal override RouterActor CreateRouterActor()
        {
            if (Resizer == null)
                return new RouterPoolActor(SupervisorStrategy);

            return new ResizablePoolActor(SupervisorStrategy);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static SupervisorStrategy DefaultSupervisorStrategy
        {
            get
            {
                return new OneForOneStrategy(Decider.From(Directive.Escalate));
            }
        }

        /// <inheritdoc/>
        public bool Equals(Pool other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Resizer, other.Resizer) && UsePoolDispatcher.Equals(other.UsePoolDispatcher) &&
                   NrOfInstances == other.NrOfInstances;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Pool)obj);
        }

        /// <inheritdoc/>
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
        /// <summary>
        /// TBD
        /// </summary>
        protected CustomRouterConfig() : base(Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routerDispatcher">TBD</param>
        protected CustomRouterConfig(string routerDispatcher) : base(routerDispatcher)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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
        /// <summary>
        /// Initializes a new instance of the <see cref="FromConfig" /> class.
        /// </summary>
        public FromConfig() : this(null, DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FromConfig" /> class.
        /// </summary>
        /// <param name="resizer">TBD</param>
        /// <param name="supervisorStrategy">TBD</param>
        /// <param name="routerDispatcher">TBD</param>
        public FromConfig(Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher)
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
        /// N/A
        /// </summary>
        /// <param name="system">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="FromConfig"/> cannot create routers.
        /// </exception>
        /// <returns>N/A</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotSupportedException("FromConfig must not create Router");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="FromConfig"/> cannot create router actors.
        /// </exception>
        /// <returns>N/A</returns>
        internal override RouterActor CreateRouterActor()
        {
            throw new NotSupportedException("FromConfig must not create RouterActor");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="path">N/A</param>
        /// <exception cref="ConfigurationException">
        /// This exception is automatically thrown since 'akka.actor.dispatch' is missing router configuration for <paramref name="path"/>.
        /// </exception>
        /// <returns>N/A</returns>
        public override void VerifyConfig(ActorPath path)
        {
            throw new ConfigurationException($"Configuration missing for router [{path}] in 'akka.actor.deployment' section.");
        }

        /// <summary>
        /// Setting the supervisor strategy to be used for the "head" Router actor
        /// </summary>
        /// <param name="strategy">TBD</param>
        /// <returns>TBD</returns>
        public FromConfig WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new FromConfig(Resizer, strategy, RouterDispatcher);
        }

        /// <summary>
        /// Setting the resizer to be used.
        /// </summary>
        /// <param name="resizer">TBD</param>
        /// <returns>TBD</returns>
        public FromConfig WithResizer(Resizer resizer)
        {
            return new FromConfig(resizer, SupervisorStrategy, RouterDispatcher);
        }

        /// <summary>
        /// Setting the dispatcher to be used for the router head actor, which handles
        /// supervision, death watch and router management messages.
        /// </summary>
        /// <param name="dispatcherId">TBD</param>
        /// <returns>TBD</returns>
        public FromConfig WithDispatcher(string dispatcherId)
        {
            return new FromConfig(Resizer, SupervisorStrategy, dispatcherId);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sys">TBD</param>
        /// <returns>TBD</returns>
        public override int GetNrOfInstances(ActorSystem sys)
        {
            return 0;
        }

        /// <summary>
        /// Enriches a <see cref="Akka.Actor.Props"/> with what what's stored in the router configuration.
        /// </summary>
        /// <returns>TBD</returns>
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
        /// <summary>
        /// TBD
        /// </summary>
        protected NoRouter()
        {
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="system">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoRouter"/> cannot create routers.
        /// </exception>
        /// <returns>N/A</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotSupportedException("NoRouter has no Router");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoRouter"/> cannot create router actors.
        /// </exception>
        /// <returns>N/A</returns>
        internal override RouterActor CreateRouterActor()
        {
            throw new NotSupportedException("NoRouter must not create RouterActor");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoRouter"/> does not have a dispatcher.
        /// </exception>
        public override string RouterDispatcher
        {
            get
            {
                throw new NotSupportedException("NoRouter has no dispatcher");
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routerConfig">TBD</param>
        /// <returns>TBD</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return routerConfig;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routeeProps">TBD</param>
        /// <returns>TBD</returns>
        public Props Props(Props routeeProps)
        {
            return routeeProps.WithRouter(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
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
