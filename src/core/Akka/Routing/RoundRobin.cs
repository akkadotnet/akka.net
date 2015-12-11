//-----------------------------------------------------------------------
// <copyright file="RoundRobin.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to a <see cref="Routee"/> determined using round-robin.
    /// This process has the router select from a list of routees in sequential order. When the list has been exhausted, the router iterates
    /// again from the beginning of the list.
    ///
    /// <note>
    /// For concurrent calls, round robin is just a best effort.
    /// </note>
    /// </summary>
    public class RoundRobinRoutingLogic : RoutingLogic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinRoutingLogic"/> class.
        /// </summary>
        public RoundRobinRoutingLogic() : this(-1) {}

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinRoutingLogic"/> class.
        /// </summary>
        /// <param name="next">The index to use when starting the selection process. Note that it will start at (next + 1).</param>
        public RoundRobinRoutingLogic(int next)
        {
            _next = next;
        }

        private int _next;

        /// <summary>
        /// Picks the next <see cref="Routee"/> in the collection to receive the <paramref name="message"/>.
        /// </summary>
        /// <param name="message">The message that is being routed.</param>
        /// <param name="routees">A collection of routees to choose from when receiving the <paramref name="message"/>.</param>
        /// <returns>A <see cref="Routee" /> that is receives the <paramref name="message"/>.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            return routees[(Interlocked.Increment(ref _next) & int.MaxValue) % routees.Length];
        }
    }

    /// <summary>
    /// This class represents a <see cref="Group"/> router that sends messages to a <see cref="Routee"/> determined using round-robin.
    /// This process has the router select from a list of routees in sequential order. When the list has been exhausted, the router
    /// iterates again from the beginning of the list.
    /// 
    /// <note>
    /// For concurrent calls, round robin is just a best effort.
    /// </note>
    /// 
    /// <note>
    /// The configuration parameter trumps the constructor arguments. This means that
    /// if you provide `paths` during instantiation they will be ignored if
    /// the router is defined in the configuration file for the actor being used.
    /// </note>
    /// </summary>
    public class RoundRobinGroup : Group
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="RoundRobinGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class RoundRobinGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="RoundRobinGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="RoundRobinGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RoundRobinGroup(Paths);
            }

            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public string[] Paths { get; set; }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="RoundRobinGroup"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="RoundRobinGroup"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RoundRobinGroupSurrogate
            {
                Paths = Paths,
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// </note>
        /// </param>
        public RoundRobinGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinGroup"/> class.
        /// </summary>
        /// <param name="paths">A list of paths used by the group router.</param>
        public RoundRobinGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinGroup"/> class.
        /// </summary>
        /// <param name="paths">An enumeration of actor paths used by the group router.</param>
        public RoundRobinGroup(IEnumerable<string> paths) : base(paths)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinGroup"/> class.
        /// </summary>
        /// <param name="routees">An enumeration of routees used by the group router.</param>
        public RoundRobinGroup(IEnumerable<IActorRef> routees) : base(routees)
        {
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RoundRobinRoutingLogic());
        }

        /// <summary>
        /// Creates a new <see cref="RoundRobinGroup"/> router with a given dispatcher id.
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Group WithDispatcher(string dispatcher)
        {
            return new RoundRobinGroup(Paths){ RouterDispatcher = dispatcher};
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages to a <see cref="Routee"/> determined using round-robin.
    /// This process has the router select from a list of routees in sequential order. When the list has been exhausted, the router
    /// iterates again from the beginning of the list.
    /// 
    /// <note>
    /// For concurrent calls, round robin is just a best effort.
    /// </note>
    /// </summary>
    public class RoundRobinPool : Pool
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="RoundRobinPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class RoundRobinPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="RoundRobinPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="RoundRobinPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RoundRobinPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            /// <summary>
            /// The number of routees associated with this pool.
            /// </summary>
            public int NrOfInstances { get; set; }
            /// <summary>
            /// Determine whether or not to use the pool dispatcher. The dispatcher is defined in the
            /// 'pool-dispatcher' configuration property in the deployment section of the router.
            /// </summary>
            public bool UsePoolDispatcher { get; set; }
            /// <summary>
            /// The resizer to use when dynamically allocating routees to the pool.
            /// </summary>
            public Resizer Resizer { get; set; }
            /// <summary>
            /// The strategy to use when supervising the pool.
            /// </summary>
            public SupervisorStrategy SupervisorStrategy { get; set; }
            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="RoundRobinPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="RoundRobinPool"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RoundRobinPoolSurrogate
            {
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        public RoundRobinPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinPool"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public RoundRobinPool(Config config) : base(config)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinPool"/> class.
        /// 
        /// <note>
        /// A <see cref="RoundRobinPool"/> configured in this way uses the <see cref="Pool.DefaultStrategy"/> supervisor strategy.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        public RoundRobinPool(int nrOfInstances) : base(nrOfInstances, null, DefaultStrategy, null) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRobinPool"/> class.
        /// 
        /// <note>
        /// A <see cref="RoundRobinPool"/> configured in this way uses the <see cref="Pool.DefaultStrategy"/> supervisor strategy.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        public RoundRobinPool(int nrOfInstances, Resizer resizer) : base(nrOfInstances, resizer, DefaultStrategy, null) { }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RoundRobinRoutingLogic());
        }

        /// <summary>
        /// Creates a new <see cref="RoundRobinPool"/> router with a given <see cref="SupervisorStrategy"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy"/>.</returns>
        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new RoundRobinPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="RoundRobinPool"/> router with a given <see cref="Resizer"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Resizer"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer"/>.</returns>
        public override Pool WithResizer(Resizer resizer)
        {
            return new RoundRobinPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="RoundRobinPool"/> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Pool WithDispatcher(string dispatcher)
        {
            return new RoundRobinPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return OverrideUnsetConfig(routerConfig);
        }
    }
}
