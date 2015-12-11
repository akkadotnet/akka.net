//-----------------------------------------------------------------------
// <copyright file="ScatterGatherFirstCompleted.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to a <see cref="Routee"/> determined
    /// using scatter-gather-first-completed. This process has the router send a message to all of its routees. The first
    /// response is used and the remaining are discarded. If the none of the routees respond within a specified time
    /// limit, a timeout failure occurs.
    /// </summary>
    public class ScatterGatherFirstCompletedRoutingLogic : RoutingLogic
    {
        private TimeSpan _within;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedRoutingLogic"/> class.
        /// </summary>
        /// <param name="within">The amount of time to wait for a response.</param>
        public ScatterGatherFirstCompletedRoutingLogic(TimeSpan within)
        {
            _within = within;
        }

        /// <summary>
        /// Picks all the provided <paramref name="routees"/> to receive the <paramref name="message" />.
        /// </summary>
        /// <param name="message">The message that is being routed</param>
        /// <param name="routees">A collection of routees to choose from when receiving the <paramref name="message" />.</param>
        /// <returns>A <see cref="ScatterGatherFirstCompletedRoutees" /> that receives the <paramref name="message" />.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            return new ScatterGatherFirstCompletedRoutees(routees,_within);
        }
    }

    /// <summary>
    /// This class represents a single point <see cref="Routee"/> that sends messages to a <see cref="Routee"/> determined
    /// using scatter-gather-first-completed. This process has the router send a message to all of its routees. The first
    /// response is used and the remaining are discarded. If the none of the routees respond within a specified time limit,
    /// a timeout failure occurs.
    /// </summary>
    public class ScatterGatherFirstCompletedRoutees : Routee
    {
        private Routee[] _routees;
        private TimeSpan _within;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedRoutees"/> class.
        /// </summary>
        /// <param name="routees">The list of routees that the router uses to send messages.</param>
        /// <param name="within">The time within which at least one response is expected.</param>
        public ScatterGatherFirstCompletedRoutees(Routee[] routees, TimeSpan within)
        {
            _routees = routees;
            _within = within;
        }

        /// <summary>
        /// Sends a message to the collection of routees.
        /// </summary>
        /// <param name="message">The message that is being sent.</param>
        /// <param name="sender">The actor sending the message.</param>
        public override void Send(object message, IActorRef sender)
        {
            var tasks = new List<Task>();
            foreach(var routee in _routees)
            {
                var ask = routee.Ask(message, _within);
                tasks.Add(ask);
            }
            var any = Task.WhenAny(tasks);
            any.PipeTo(sender);
        }
    }

    /// <summary>
    /// This class represents a <see cref="Group"/> router that sends messages to a <see cref="Routee"/> determined using scatter-gather-first-completed.
    /// This process has the router send a message to all of its routees. The first response is used and the remaining are discarded. If the none of the
    /// routees respond within a specified time limit, a timeout failure occurs.
    /// </summary>
    public class ScatterGatherFirstCompletedGroup : Group
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="ScatterGatherFirstCompletedGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class ScatterGatherFirstCompletedGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="ScatterGatherFirstCompletedGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="ScatterGatherFirstCompletedGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ScatterGatherFirstCompletedGroup(Paths,Within);
            }

            /// <summary>
            /// The amount of time to wait for a response.
            /// </summary>
            public TimeSpan Within { get; set; }
            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public string[] Paths { get; set; }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="ScatterGatherFirstCompletedGroup"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="ScatterGatherFirstCompletedGroup"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new ScatterGatherFirstCompletedGroupSurrogate
            {
                Paths = Paths,
                Within = Within,
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// If 'within' is defined in the provided configuration then that will be used as the interval.
        /// </note>
        /// </param>
        public ScatterGatherFirstCompletedGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
            Within = config.GetTimeSpan("within");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="paths">A list of actor paths used by the group router.</param>
        public ScatterGatherFirstCompletedGroup(TimeSpan within,params string[] paths)
            : base(paths)
        {
            Within = within;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="paths">An enumeration of actor paths used by the group router.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        public ScatterGatherFirstCompletedGroup(IEnumerable<string> paths,TimeSpan within) : base(paths)
        {
            Within = within;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup"/> class.
        /// </summary>
        /// <param name="routees">An enumeration of routees used by the group router.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        public ScatterGatherFirstCompletedGroup(IEnumerable<IActorRef> routees,TimeSpan within) : base(routees)
        {
            Within = within;
        }

        /// <summary>
        /// The amount of time to wait for a response.
        /// </summary>
        public TimeSpan Within { get;private set; }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ScatterGatherFirstCompletedRoutingLogic(Within));
        }

        /// <summary>
        /// Creates a new <see cref="ScatterGatherFirstCompletedGroup" /> router with a given dispatcher id.
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Group WithDispatcher(string dispatcher)
        {
            return new ScatterGatherFirstCompletedGroup(Within, Paths){ RouterDispatcher = dispatcher};
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages to a <see cref="Routee"/> determined using scatter-gather-first-completed.
    /// This process has the router send a message to all of its routees. The first response is used and the remaining are discarded. If the none of the
    /// routees respond within a specified time limit, a timeout failure occurs.
    /// </summary>
    public class ScatterGatherFirstCompletedPool : Pool
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="ScatterGatherFirstCompletedPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class ScatterGatherFirstCompletedPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="ScatterGatherFirstCompletedPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="ScatterGatherFirstCompletedPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ScatterGatherFirstCompletedPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher,Within, UsePoolDispatcher);
            }

            /// <summary>
            /// The amount of time to wait for a response.
            /// </summary>
            public TimeSpan Within { get; set; }
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
        /// Creeates a surrogate representation of the current <see cref="ScatterGatherFirstCompletedPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="ScatterGatherFirstCompletedPool"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new ScatterGatherFirstCompletedPoolSurrogate
            {
                Within = _within,
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }

        private  TimeSpan _within;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher,TimeSpan within, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            _within = within;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="within">The amount of time to wait for a response.</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances, TimeSpan within) : this(nrOfInstances)
        {
            _within = within;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedPool"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// 'within' must be defined in the provided configuration.
        /// </note>
        /// </param>
        public ScatterGatherFirstCompletedPool(Config config) : base(config)
        {
            _within = config.GetTimeSpan("within");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterGatherFirstCompletedPool"/> class.
        /// 
        /// <note>
        /// A <see cref="ScatterGatherFirstCompletedPool"/> configured in this way uses the <see cref="Pool.DefaultStrategy"/> supervisor strategy.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances) : base(nrOfInstances, null, DefaultStrategy, null) { }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ScatterGatherFirstCompletedRoutingLogic(_within));
        }

        /// <summary>
        /// Creates a new <see cref="ScatterGatherFirstCompletedPool" /> router with a given <see cref="SupervisorStrategy" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy" />.</returns>
        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new ScatterGatherFirstCompletedPool(NrOfInstances, Resizer, strategy, RouterDispatcher, _within, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="ScatterGatherFirstCompletedPool" /> router with a given <see cref="Resizer" />.
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Resizer" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer" />.</returns>
        public override Pool WithResizer(Resizer resizer)
        {
            return new ScatterGatherFirstCompletedPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, _within, UsePoolDispatcher);
        }

        /// <summary>
        /// Creates a new <see cref="ScatterGatherFirstCompletedPool" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Pool WithDispatcher(string dispatcher)
        {
            return new ScatterGatherFirstCompletedPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, _within, UsePoolDispatcher);
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
