//-----------------------------------------------------------------------
// <copyright file="RouterConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public abstract class RouterConfig : IEquatable<RouterConfig>, ISurrogated
    {
        //  public abstract RoutingLogic GetLogic();

        /// <summary>
        /// A configuration that specifies that no router is to be used.
        /// </summary>
        public static readonly RouterConfig NoRouter = new NoRouter();

        /// <summary>
        /// The id of the dispatcher that the router uses to pass messages to its routees.
        /// </summary>
        public virtual string RouterDispatcher { get; protected set; }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// 
        /// <note>
        /// This method defaults to ignoring the supplied router and returning itself.
        /// </note>
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        public virtual RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return this;
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public abstract Router CreateRouter(ActorSystem system);
        internal abstract RouterActor CreateRouterActor();

        /// <summary>
        /// Retrieves an enumeration of <see cref="Routee">routees</see> that belong to the provided <paramref name="routedActorCell"/>.
        /// </summary>
        /// <param name="routedActorCell">The router to query for a list of its routees.</param>
        /// <returns>The enumeration of routees that belong to the provided <paramref name="routedActorCell"/>.</returns>
        public abstract IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell);

        /// <summary>
        /// Determines whether a provided message is handled by the router.
        /// </summary>
        /// <param name="message">The message to inspect.</param>
        /// <returns><c>true</c> if this message is handled by the router; otherwise <c>false</c>.</returns>
        public virtual bool IsManagementMessage(object message)
        {
            return
                message is IAutoReceivedMessage ||
                // in akka.net this message is a subclass of AutoReceivedMessage - so removed condition that "message is Terminated ||"
                message is RouterManagementMessage;
        }

        /// <summary>
        /// Determines whether the specified router, is equal to this instance.
        /// </summary>
        /// <param name="other">The router to compare.</param>
        /// <returns><c>true</c> if the specified router is equal to this instance; otherwise, <c>false</c>.</returns>
        public virtual bool Equals(RouterConfig other)
        {
            if (other == null) return false;
            return GetType() == other.GetType()
                   && (GetType() == typeof (NoRouter)
                       || String.Equals(RouterDispatcher, other.RouterDispatcher));
        }

        /// <summary>
        /// Creates a surrogate representation of the current router.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current router.</returns>
        public abstract ISurrogate ToSurrogate(ActorSystem system);

        /// <summary>
        /// Initializes a new instance of the <see cref="RouterConfig"/> class.
        /// </summary>
        protected RouterConfig()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RouterConfig"/> class.
        /// 
        /// <note>
        /// This method defaults to setting the dispatcher to use the <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to routees.</param>
        protected RouterConfig(string routerDispatcher)
        {
// ReSharper disable once DoNotCallOverridableMethodsInConstructor
            RouterDispatcher = routerDispatcher ?? Dispatchers.DefaultDispatcherId;
        }
    }

    /// <summary>
    /// This class contains extension methods used by <see cref="RouterConfig"/>s.
    /// </summary>
    public static class RouterConfigExtensions
    {
        /// <summary>
        /// Determines whether or not the provided router is a <see cref="Routing.NoRouter"/>.
        /// </summary>
        /// <param name="config">The router to check.</param>
        /// <returns><c>true</c> if the provided router is a <see cref="Routing.NoRouter"/>; otherwise, <c>false</c>.</returns>
        public static bool NoRouter(this RouterConfig config)
        {
            return config == null || config is NoRouter;
        }
    }

    /// <summary>
    /// This class represents a router that does not route messages.
    /// </summary>
    public class NoRouter : RouterConfig
    {
        /// <summary>
        /// The id of the dispatcher that the router uses to pass messages to its routees.
        /// 
        /// <note>
        /// THIS METHOD IS NOT IMPLEMENTED.
        /// </note>
        /// </summary>
        /// <exception cref="NotSupportedException">NoRouter has no router</exception>
        public override string RouterDispatcher
        {
            get { throw new NotSupportedException("NoRouter has no router"); }
        }

        internal override RouterActor CreateRouterActor()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        ///
        /// <note>
        /// THIS METHOD IS NOT IMPLEMENTED.
        /// </note>
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>
        /// The newly created router tied to the given system.
        /// </returns>
        /// <exception cref="NotImplementedException"></exception>
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Retrieves an enumeration of <see cref="Routee">routees</see> that belong to the provided <paramref name="routedActorCell"/>.
        /// 
        /// <note>
        /// THIS METHOD IS NOT IMPLEMENTED.
        /// </note>
        /// </summary>
        /// <param name="routedActorCell">The router to query for a list of its routees.</param>
        /// <returns>
        /// The enumeration of routees that belong to the provided <paramref name="routedActorCell"/>.
        /// </returns>
        /// <exception cref="NotImplementedException"></exception>
        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            throw new NotImplementedException();
        }

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

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// 
        /// <note>
        /// This method returns the provided <paramref name="routerConfig"/>.
        /// </note>
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return routerConfig;
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
        /// <summary>
        /// Determines whether the specified <see cref="Group"/>, is equal to this instance.
        /// </summary>
        /// <param name="other">The group to compare.</param>
        /// <returns><c>true</c> if the specified <see cref="Group"/> is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(Group other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return _paths.SequenceEqual(other._paths);
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
            return Equals((Group) obj);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return (_paths != null ? _paths.GetHashCode() : 0);
        }

        private readonly string[] _paths;

        /// <summary>
        /// Retrieves the actor paths used by this router during routee selection.
        /// </summary>
        public string[] Paths
        {
            get { return _paths; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Group"/> class.
        /// 
        /// <note>
        /// This constructor sets up the group to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="paths">An enumeration of actor paths used by the group router.</param>
        protected Group(IEnumerable<string> paths) : base(Dispatchers.DefaultDispatcherId)
        {
            _paths = paths.ToArray();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Group"/> class.
        /// 
        /// <note>
        /// If a <paramref name="routerDispatcher"/> is not provided, this constructor sets up
        /// the pool to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="paths">An enumeration of actor paths used by the group router.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        protected Group(IEnumerable<string> paths, string routerDispatcher)
            : base(routerDispatcher ?? Dispatchers.DefaultDispatcherId)
        {
            _paths = paths.ToArray();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Group"/> class.
        ///
        /// <note>
        /// This constructor sets up the group to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="routees">An enumeration of routees used by the group router.</param>
        protected Group(IEnumerable<IActorRef> routees)
            : base(Dispatchers.DefaultDispatcherId)
        {
            _paths = routees.Select(x => x.Path.ToStringWithAddress()).ToArray();
        }

        internal Routee RouteeFor(string path, IActorContext context)
        {
            return new ActorSelectionRoutee(context.ActorSelection(path));
        }

        /// <summary>
        /// Adds the current router to an empty <see cref="Actor.Props"/>.
        /// </summary>
        /// <returns>An empty <see cref="Actor.Props"/> configured to use the current router.</returns>
        public Props Props()
        {
            return Actor.Props.Empty.WithRouter(this);
        }

        internal override RouterActor CreateRouterActor()
        {
            return new RouterActor();
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        /// 
        /// <note>
        /// THIS METHOD IS NOT IMPLEMENTED.
        /// </note>
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>
        /// The newly created router tied to the given system.
        /// </returns>
        /// <exception cref="NotImplementedException"></exception>
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a new <see cref="Group"/> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public abstract Group WithDispatcher(string dispatcher);

        /// <summary>
        /// Retrieves an enumeration of <see cref="Routee">routees</see> that belong to the provided <paramref name="routedActorCell"/>.
        /// </summary>
        /// <param name="routedActorCell">The router to query for a list of its routees.</param>
        /// <returns>
        /// The enumeration of routees that belong to the provided <paramref name="routedActorCell"/>.
        /// </returns>
        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            if (_paths == null) return new Routee[0];
            return
                _paths.Select(((ActorSystemImpl) routedActorCell.System).ActorSelection)
                    .Select(actor => new ActorSelectionRoutee(actor));
        }

        /// <summary>
        /// Determines whether the specified router, is equal to this instance.
        /// </summary>
        /// <param name="other">The router to compare.</param>
        /// <returns>
        ///   <c>true</c> if the specified router is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(RouterConfig other)
        {
            if (!base.Equals(other)) return false;
            var otherGroup = other as Group;
            if (otherGroup == null) return false; //should never be true due to the previous check
            return Paths.Intersect(otherGroup.Paths).Count() == Paths.Length;
        }
    }


    /// <summary>
    /// This class provides base functionality for all pool routers in the system.
    /// Pool routers are routers that create their own routees based on the provided
    /// configuration.
    /// </summary>
    public abstract class Pool : RouterConfig, IEquatable<Pool>
    {
        private readonly int _nrOfInstances;
        private readonly bool _usePoolDispatcher;
        private readonly Resizer _resizer;
        private readonly SupervisorStrategy _supervisorStrategy;
        //TODO: add supervisor strategy to the equality compare

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
            return Equals((Pool) obj);
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
                int hashCode = (Resizer != null ? Resizer.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ UsePoolDispatcher.GetHashCode();
                hashCode = (hashCode*397) ^ NrOfInstances;
                return hashCode;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Pool"/> class.
        /// 
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
        protected Pool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher,
            bool usePoolDispatcher = false) : base(routerDispatcher ?? Dispatchers.DefaultDispatcherId)
        {
            // OMG, if every member in Java is virtual - you must never call any members in a constructor!!1!
            // In all seriousness, without making these members virtual RemoteRouterConfig won't work
            // ReSharper disable DoNotCallOverridableMethodsInConstructor
            _nrOfInstances = nrOfInstances;

            _resizer = resizer;
            _supervisorStrategy = supervisorStrategy ?? DefaultStrategy;
            _usePoolDispatcher = usePoolDispatcher;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Pool"/> class.
        /// 
        /// <note>
        /// This constructor sets up the pool to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        protected Pool(Config config) : base(Dispatchers.DefaultDispatcherId)
        {
            _nrOfInstances = config.GetInt("nr-of-instances");
            _resizer = DefaultResizer.FromConfig(config);
            _usePoolDispatcher = config.HasPath("pool-dispatcher");
            _supervisorStrategy = DefaultStrategy;
            // ReSharper restore DoNotCallOverridableMethodsInConstructor
        }

        /// <summary>
        /// Retrieves the number of routees associated with this pool.
        /// </summary>
        public virtual int NrOfInstances
        {
            get { return _nrOfInstances; }
        }

        /// <summary>
        /// Used by the <see cref="RoutedActorCell"/> to determine the initial number of routees.
        /// 
        /// <note>
        /// Needs to be connected to an <see cref="ActorSystem"/> for clustered deployment scenarios.
        /// </note>
        /// </summary>
        /// <param name="system"></param>
        /// <returns>The number of routees associated with this pool.</returns>
        public virtual int GetNrOfInstances(ActorSystem system)
        {
            return NrOfInstances;
        }

        /// <summary>
        /// Retrieve whether or not to use the pool dispatcher. The dispatcher is defined in the
        /// 'pool-dispatcher' configuration property in the deployment section of the router.
        /// </summary>
        public virtual bool UsePoolDispatcher
        {
            get { return _usePoolDispatcher; }
        }

        /// <summary>
        /// Retrieve the resizer to use when dynamically allocating routees to the pool.
        /// </summary>
        public virtual Resizer Resizer
        {
            get { return _resizer; }
        }

        /// <summary>
        /// Retrieve the strategy to use when supervising the pool.
        /// </summary>
        public virtual SupervisorStrategy SupervisorStrategy
        {
            get { return _supervisorStrategy; }
        }

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
        public virtual Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var routee = new ActorRefRoutee(context.ActorOf(EnrichWithPoolDispatcher(routeeProps, context)));
            return routee;
        }

        internal Props EnrichWithPoolDispatcher(Props routeeProps, IActorContext context)
        {
            //        if (usePoolDispatcher && routeeProps.dispatcher == Dispatchers.DefaultDispatcherId)
            //  routeeProps.withDispatcher("akka.actor.deployment." + context.self.path.elements.drop(1).mkString("/", "/", "")
            //    + ".pool-dispatcher")
            //else
            //  routeeProps
            if (UsePoolDispatcher && routeeProps.Dispatcher == Dispatchers.DefaultDispatcherId)
            {
                return
                    routeeProps.WithDispatcher("akka.actor.deployment." + "/" +
                                               context.Self.Path.Elements.Drop(1).Join("/") +
                                               ".pool-dispatcher");
            }
            return routeeProps;
        }

        /// <summary>
        /// Adds the current router to the provided <paramref name="routeeProps"/>.
        /// </summary>
        /// <param name="routeeProps">The <see cref="Actor.Props"/> to configure with the current router.</param>
        /// <returns>The provided <paramref name="routeeProps"/> configured to use the current router.</returns>
        public Props Props(Props routeeProps)
        {
            return routeeProps.WithRouter(this);
        }

        internal override RouterActor CreateRouterActor()
        {
            if (Resizer == null)
                return new RouterPoolActor(SupervisorStrategy);
            return new ResizablePoolActor(SupervisorStrategy);
        }

        /// <summary>
        /// Retrieves an enumeration of <see cref="Routee">routees</see> that belong to the provided <paramref name="routedActorCell"/>.
        /// </summary>
        /// <param name="routedActorCell">The router to query for a list of its routees.</param>
        /// <returns>
        /// The enumeration of routees that belong to the provided <paramref name="routedActorCell"/>.
        /// </returns>
        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            for (var i = 0; i < NrOfInstances; i++)
            {
                //TODO: where do we get props?
                yield return NewRoutee(Actor.Props.Empty, routedActorCell);
            }
        }

        /// <summary>
        /// Overrides the settings of the current router with those in the provided configuration.
        /// </summary>
        /// <param name="other">The configuration whose settings are used to overwrite the current router.</param>
        /// <returns>The current router whose settings have been overwritten.</returns>
        protected RouterConfig OverrideUnsetConfig(RouterConfig other)
        {
            if (other is NoRouter) return this; // NoRouter is the default, hence "neutral"
            if (other is Pool)
            {
                Pool wssConf;
                var p = other as Pool;
                if (SupervisorStrategy == null || (SupervisorStrategy.Equals(Pool.DefaultStrategy) &&
                                                   !p.SupervisorStrategy.Equals(Pool.DefaultStrategy)))
                    wssConf = this.WithSupervisorStrategy(p.SupervisorStrategy);
                else
                    wssConf = this;

                if (wssConf.Resizer == null && p.Resizer != null)
                    return wssConf.WithResizer(p.Resizer);
                return wssConf;
            }
            return this;
        }

        /// <summary>
        /// Creates a new <see cref="Pool"/> router with a given <see cref="Actor.SupervisorStrategy"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="Actor.SupervisorStrategy"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy"/>.</returns>
        public abstract Pool WithSupervisorStrategy(SupervisorStrategy strategy);

        /// <summary>
        /// Creates a new <see cref="Pool"/> router with a given <see cref="Routing.Resizer"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Routing.Resizer"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer"/>.</returns>
        public abstract Pool WithResizer(Resizer resizer);

        /// <summary>
        /// Creates a new <see cref="Pool"/> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public abstract Pool WithDispatcher(string dispatcher);

        #region Static methods

        /// <summary>
        /// Retrieves the default <see cref="Actor.SupervisorStrategy"/> used by this router when one has not been specified.
        /// When supervisorStrategy is not specified for an actor this
        /// is used by default.
        /// 
        /// <note>
        /// The default strategy used is <see cref="OneForOneStrategy"/> with an <see cref="Directive.Escalate"/> decider.
        /// </note>
        /// </summary>
        public static SupervisorStrategy DefaultStrategy
        {
            get { return new OneForOneStrategy(10, TimeSpan.FromSeconds(10), Decider.From(Directive.Escalate)); }
        }

        #endregion

        #region Overrides

        //public override bool Equals(RouterConfig other)
        //{
        //    if (!base.Equals(other)) return false;
        //    var otherPool = other as Pool;
        //    if (otherPool == null) return false; //should never be true due to the previous check
        //    return NrOfInstances == otherPool.NrOfInstances &&
        //           UsePoolDispatcher == otherPool.UsePoolDispatcher &&
        //           (Resizer == null && otherPool.Resizer == null || Resizer != null && otherPool.Resizer != null) &&
        //           SupervisorStrategy.GetType() == otherPool.SupervisorStrategy.GetType();
        //}

        #endregion
    }

    /// <summary>
    /// This class represents a router that gets it's configuration from the system.
    /// 
    /// For example:
    /// <code>
    /// IActorRef router1 = Sys.ActorOf(Props.Create{Echo}().WithRouter(FromConfig.Instance), "router1");
    /// </code>
    /// </summary>
    public class FromConfig : RouterConfig
    {
        private static readonly FromConfig _instance = new FromConfig(Dispatchers.DefaultDispatcherId);

        private FromConfig(string routerDispatcher) : base(routerDispatcher)
        {
        }

        /// <summary>
        /// Retrieves a <see cref="RouterConfig"/> based on what's stored in the configuration.
        /// 
        /// <note>
        /// This router is set to use the default dispatcher <see cref="Dispatchers.DefaultDispatcherId"/>.
        /// </note>
        /// </summary>
        public static FromConfig Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system"/>.
        /// 
        /// <note>
        /// THIS METHOD IS NOT SUPPORTED.
        /// </note>
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>
        /// The newly created router tied to the given system.
        /// </returns>
        /// <exception cref="NotSupportedException"></exception>
        public override Router CreateRouter(ActorSystem system)
        {
            throw new NotSupportedException();
        }

        internal override RouterActor CreateRouterActor()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Retrieves an enumeration of routees that belong to a provided <paramref name="routedActorCell"/>.
        /// 
        /// <note>
        /// THIS METHOD IS NOT SUPPORTED.
        /// </note>
        /// </summary>
        /// <param name="routedActorCell">The router to query for a list of its routees.</param>
        /// <returns>
        /// The enumeration of routees that belong to the provided <paramref name="routedActorCell"/>.
        /// </returns>
        /// <exception cref="NotSupportedException"></exception>
        public override IEnumerable<Routee> GetRoutees(RoutedActorCell routedActorCell)
        {
            throw new NotSupportedException();
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
}