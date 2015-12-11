//-----------------------------------------------------------------------
// <copyright file="Broadcast.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to multiple <see cref="Routee">routees</see>.
    /// </summary>
    public class BroadcastRoutingLogic : RoutingLogic
    {
        /// <summary>
        /// Picks all the <see cref="Routee">routees</see> in <paramref name="routees"/> to receive the <paramref name="message"/>.
        /// </summary>
        /// <param name="message">The message that is being routed.</param>
        /// <param name="routees">A collection of routees that receives the <paramref name="message"/>.</param>
        /// <returns>A <see cref="Routee"/> that contains all the given <paramref name="routees"/> that receives the <paramref name="message"/>.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || !routees.Any())
                return Routee.NoRoutee;
            return new SeveralRoutees(routees);
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages it receives to all of its <see cref="Routee">routees</see>.
    /// </summary>
    public class BroadcastPool : Pool
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="BroadcastPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class BroadcastPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="BroadcastPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="BroadcastPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new BroadcastPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
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
        /// Creates a surrogate representation of the current <see cref="BroadcastPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="BroadcastPool"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new BroadcastPoolSurrogate
            {
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public BroadcastPool(Config config) : base(config)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        public BroadcastPool(int nrOfInstances, Resizer resizer,SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// 
        /// <note>
        /// A <see cref="BroadcastPool"/> configured in this way uses the <see cref="Pool.DefaultStrategy"/> supervisor strategy.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        public BroadcastPool(int nrOfInstances) : base(nrOfInstances, null, DefaultStrategy, null) { }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new BroadcastRoutingLogic());
        }

        /// <summary>
        /// Creates a new <see cref="BroadcastPool"/> router with a given <see cref="SupervisorStrategy"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy" />.</returns>
        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new BroadcastPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="BroadcastPool"/> router with a given <see cref="Resizer"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Resizer"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer" />.</returns>
        public override Pool WithResizer(Resizer resizer)
        {
            return new BroadcastPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="BroadcastPool"/> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Pool WithDispatcher(string dispatcher)
        {
            return new BroadcastPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, UsePoolDispatcher);
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

    /// <summary>
    /// This class represents a <see cref="Group"/> router that sends messages it receives to all of its routees.
    /// </summary>
    public class BroadcastGroup : Group
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="BroadcastGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class BroadcastGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="BroadcastGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="BroadcastGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new BroadcastGroup(Paths);
            }

            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public string[] Paths { get; set; }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="BroadcastGroup"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="BroadcastGroup"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new BroadcastGroupSurrogate
            {
                Paths = Paths,
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// </note>
        /// </param>
        public BroadcastGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastGroup"/> class.
        /// </summary>
        /// <param name="paths">A list of actor paths used by the group router.</param>
        public BroadcastGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastGroup"/> class.
        /// </summary>
        /// <param name="paths">An enumeration of actor paths used by the group router.</param>
        public BroadcastGroup(IEnumerable<string> paths) : base(paths)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastGroup"/> class.
        /// </summary>
        /// <param name="routees">An enumeration of routees used by the group router.</param>
        public BroadcastGroup(IEnumerable<IActorRef> routees)
            : base(routees)
        {
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new BroadcastRoutingLogic());
        }

        /// <summary>
        /// Creates a new <see cref="BroadcastGroup" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Group WithDispatcher(string dispatcher)
        {
            return new BroadcastGroup(Paths){ RouterDispatcher = dispatcher };
        }
    }
}
