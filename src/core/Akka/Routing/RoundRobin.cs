using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    ///     Class RoundRobinRoutingLogic.
    /// </summary>
    public class RoundRobinRoutingLogic : RoutingLogic
    {
        /// <summary>
        ///     The next
        /// </summary>
        private int _next = -1;

        /// <summary>
        ///     Selects the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="routees">The routees.</param>
        /// <returns>Routee.</returns>
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            return routees[Interlocked.Increment(ref _next)%routees.Length];
        }
    }

    /// <summary>
    /// A router group that uses round-robin to select a routee. For concurrent calls,
    /// round robin is just a best effort.
    ///
    /// The configuration parameter trumps the constructor arguments. This means that
    /// if you provide `paths` during instantiation they will be ignored if
    /// the router is defined in the configuration file for the actor being used.
    /// </summary>
    public class RoundRobinGroup : Group
    {
        public class RoundRobinGroupSurrogate : ISurrogate
        {
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RandomGroup(Paths);
            }

            public string[] Paths { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RoundRobinGroupSurrogate
            {
                Paths = Paths,
            };
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RoundRobinGroup" /> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public RoundRobinGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RoundRobinGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public RoundRobinGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RoundRobinGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public RoundRobinGroup(IEnumerable<string> paths) : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RoundRobinGroup" /> class.
        /// </summary>
        /// <param name="routees">The routees.</param>
        public RoundRobinGroup(IEnumerable<ActorRef> routees) : base(routees)
        {
        }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RoundRobinRoutingLogic());
        }
    }

    /// <summary>
    ///     Class RoundRobinPool.
    /// </summary>
    public class RoundRobinPool : Pool
    {
        public class RoundRobinPoolSurrogate : ISurrogate
        {
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RandomPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            public int NrOfInstances { get; set; }
            public bool UsePoolDispatcher { get; set; }
            public Resizer Resizer { get; set; }
            public SupervisorStrategy SupervisorStrategy { get; set; }
            public string RouterDispatcher { get; set; }
        }

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

        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public RoundRobinPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }

        public RoundRobinPool(Config config) : base(config)
        {
            
        }

        /// <summary>
        /// Simple form of RoundRobin constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public RoundRobinPool(int nrOfInstances) : base(nrOfInstances, null, DefaultStrategy, null) { }

        /// <summary>
        /// Simple form of RoundRobin constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">A <see cref="Resizer"/> for specifying how to grow the pool of underlying routees based on pressure</param>
        public RoundRobinPool(int nrOfInstances, Resizer resizer) : base(nrOfInstances, resizer, DefaultStrategy, null) { }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RoundRobinRoutingLogic());
        }
    }
}