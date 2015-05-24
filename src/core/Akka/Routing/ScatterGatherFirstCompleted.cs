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
    public class ScatterGatherFirstCompletedRoutingLogic : RoutingLogic
    {
        private TimeSpan _within;
        public ScatterGatherFirstCompletedRoutingLogic(TimeSpan within)
        {
            _within = within;
        }
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            return new ScatterGatherFirstCompletedRoutees(routees,_within);
        }
    }

    public class ScatterGatherFirstCompletedRoutees : Routee
    {
        private Routee[] _routees;
        private TimeSpan _within;
        public ScatterGatherFirstCompletedRoutees(Routee[] routees, TimeSpan within)
        {
            _routees = routees;
            _within = within;
        }

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

    public class ScatterGatherFirstCompletedGroup : Group
    {
        public class ScatterGatherFirstCompletedGroupSurrogate : ISurrogate
        {
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ScatterGatherFirstCompletedGroup(Paths,Within);
            }

            public TimeSpan Within { get; set; }
            public string[] Paths { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new ScatterGatherFirstCompletedGroupSurrogate
            {
                Paths = Paths,
                Within = Within,
            };
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public ScatterGatherFirstCompletedGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
            Within = config.GetTimeSpan("within");
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="within">Expect a response within the given timespan</param>
        /// <param name="paths">The paths.</param>
        public ScatterGatherFirstCompletedGroup(TimeSpan within,params string[] paths)
            : base(paths)
        {
            Within = within;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <param name="within">Expect a response within the given timespan</param>
        public ScatterGatherFirstCompletedGroup(IEnumerable<string> paths,TimeSpan within) : base(paths)
        {
            Within = within;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="routees">The routees.</param>
        /// <param name="within">Expect a response within the given timespan</param>
        public ScatterGatherFirstCompletedGroup(IEnumerable<IActorRef> routees,TimeSpan within) : base(routees)
        {
            Within = within;
        }

        public TimeSpan Within { get;private set; }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ScatterGatherFirstCompletedRoutingLogic(Within));
        }

        public override Group WithDispatcher(string dispatcher)
        {
            return new ScatterGatherFirstCompletedGroup(Within, Paths){ RouterDispatcher = dispatcher};
        }
    }

    /// <summary>
    ///     Class RoundRobinPool.
    /// </summary>
    public class ScatterGatherFirstCompletedPool : Pool
    {
        public class ScatterGatherFirstCompletedPoolSurrogate : ISurrogate
        {
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ScatterGatherFirstCompletedPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher,Within, UsePoolDispatcher);
            }

            public TimeSpan Within { get; set; }
            public int NrOfInstances { get; set; }
            public bool UsePoolDispatcher { get; set; }
            public Resizer Resizer { get; set; }
            public SupervisorStrategy SupervisorStrategy { get; set; }
            public string RouterDispatcher { get; set; }
        }

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
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="within">Expect a response within the given timespan</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher,TimeSpan within, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            _within = within;
        }

        /// <summary>
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="within">Expect a response within the given timespan</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances, TimeSpan within) : this(nrOfInstances)
        {
            _within = within;
        }

        public ScatterGatherFirstCompletedPool(Config config) : base(config)
        {
            _within = config.GetTimeSpan("within");
        }

        /// <summary>
        /// Simple form of RoundRobin constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances) : base(nrOfInstances, null, DefaultStrategy, null) { }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ScatterGatherFirstCompletedRoutingLogic(_within));
        }

        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new ScatterGatherFirstCompletedPool(NrOfInstances, Resizer, strategy, RouterDispatcher, _within, UsePoolDispatcher);
        }

        public override Pool WithResizer(Resizer resizer)
        {
            return new ScatterGatherFirstCompletedPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, _within, UsePoolDispatcher);
        }

        public override Pool WithDispatcher(string dispatcher)
        {
            return new ScatterGatherFirstCompletedPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher, _within, UsePoolDispatcher);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            return OverrideUnsetConfig(routerConfig);
        }
    }
}

