//-----------------------------------------------------------------------
// <copyright file="Random.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to a random <see cref="Routee"/>.
    /// </summary>
    public sealed class RandomLogic : RoutingLogic
    {
        /// <summary>
        /// Picks a random <see cref="Routee"/> to receive the <paramref name="message"/>.
        /// </summary>
        /// <param name="message">The message that is being routed.</param>
        /// <param name="routees">A collection of routees to randomly choose from when receiving the <paramref name="message"/>.</param>
        /// <returns>A <see cref="Routee" /> that receives the <paramref name="message"/>.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }

            return routees[ThreadLocalRandom.Current.Next(routees.Length)];
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages to a random <see cref="Routee"/>.
    /// </summary>
    public sealed class RandomPool : Pool
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RandomPool"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public RandomPool(Config config)
            : this(
                  nrOfInstances: config.GetInt("nr-of-instances", 0),
                  resizer: Resizer.FromConfig(config),
                  supervisorStrategy: Pool.DefaultSupervisorStrategy,
                  routerDispatcher: Dispatchers.DefaultDispatcherId,
                  usePoolDispatcher: config.HasPath("pool-dispatcher"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        public RandomPool(int nrOfInstances) 
            : this(nrOfInstances, null, DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        public RandomPool(
            int nrOfInstances,
            Resizer resizer,
            SupervisorStrategy supervisorStrategy,
            string routerDispatcher,
            bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RandomLogic());
        }

        /// <summary>
        /// Used by the <see cref="RoutedActorCell" /> to determine the initial number of routees.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The number of routees associated with this pool.</returns>
        public override int GetNrOfInstances(ActorSystem system)
        {
            return NrOfInstances;
        }

        /// <summary>
        /// Creates a new <see cref="RandomPool" /> router with a given <see cref="SupervisorStrategy" />.
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy" />.</returns>
        public RandomPool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new RandomPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="RandomPool" /> router with a given <see cref="Resizer" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Resizer" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer" />.</returns>
        public RandomPool WithResizer(Resizer resizer)
        {
            return new RandomPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="RandomPool" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public RandomPool WithDispatcher(string dispatcher)
        {
            return new RandomPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, UsePoolDispatcher);
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

        private RouterConfig OverrideUnsetConfig(RouterConfig other)
        {
            if (other is Pool pool)
            {
                RandomPool wssConf;

                if (SupervisorStrategy != null
                    && SupervisorStrategy.Equals(DefaultSupervisorStrategy)
                    && !pool.SupervisorStrategy.Equals(DefaultSupervisorStrategy))
                {
                    wssConf = WithSupervisorStrategy(pool.SupervisorStrategy);
                }
                else
                {
                    wssConf = this;
                }

                if (wssConf.Resizer == null && pool.Resizer != null)
                    return wssConf.WithResizer(pool.Resizer);

                return wssConf;
            }

            return this;
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="RandomPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="RandomPool"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RandomPoolSurrogate
            {
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher
            };
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="RandomPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class RandomPoolSurrogate : ISurrogate
        {
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

            /// <summary>
            /// Creates a <see cref="RandomPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="RandomPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RandomPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }
        }
    }

    /// <summary>
    /// This class represents a <see cref="Group"/> router that sends messages to a random <see cref="Routee"/>.
    /// </summary>
    public sealed class RandomGroup : Group 
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// </note>
        /// </param>
        public RandomGroup(Config config)
            : this(config.GetStringList("routees.paths", new string[] { }), Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="paths">>A list of actor paths used by the group router.</param>
        public RandomGroup(params string[] paths)
            : base(paths, Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="paths">An enumeration of paths used by the group router.</param>
        public RandomGroup(IEnumerable<string> paths)
            : base(paths, Dispatchers.DefaultDispatcherId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="paths">An enumeration of paths used by the group router.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        public RandomGroup(IEnumerable<string> paths, string routerDispatcher)
            : base(paths, routerDispatcher)
        {
        }

        /// <summary>
        /// Retrieves the actor paths used by this router during routee selection.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>An enumeration of actor paths used during routee selection</returns>
        public override IEnumerable<string> GetPaths(ActorSystem system)
        {
            return InternalPaths;
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RandomLogic());
        }

        /// <summary>
        /// Creates a new <see cref="RandomGroup" /> router with a given dispatcher id.
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public RandomGroup WithDispatcher(string dispatcher)
        {
            return new RandomGroup(InternalPaths, dispatcher);
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="RandomGroup"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="RandomGroup"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RandomGroupSurrogate
            {
                Paths = InternalPaths,
                RouterDispatcher = RouterDispatcher
            };
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="RandomGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class RandomGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="RandomGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="RandomGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RandomGroup(Paths, RouterDispatcher);
            }

            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public IEnumerable<string> Paths { get; set; }

            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }
        }
    }
}
