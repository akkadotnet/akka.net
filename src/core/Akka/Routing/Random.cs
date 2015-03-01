using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    /// <summary>
    ///     Class RandomLogic.
    /// </summary>
    public class RandomLogic : RoutingLogic
    {
        /// <summary>
        ///     Selects the routee for the given message.
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
            return routees[ThreadLocalRandom.Current.Next(routees.Length - 1)%routees.Length];
        }
    }

    /// <summary>
    ///     Class RandomGroup.
    /// </summary>
    public class RandomGroup : Group
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RandomGroup" /> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public RandomGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RandomGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public RandomGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RandomGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public RandomGroup(IEnumerable<string> paths)
            : base(paths)
        {
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RandomGroupSurrogate
            {
                Paths = Paths
            };
        }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RandomLogic());
        }

        public class RandomGroupSurrogate : ISurrogate
        {
            public string[] Paths { get; set; }

            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RandomGroup(Paths);
            }
        }
    }

    public class RandomPool : Pool
    {
        /// <summary>
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public RandomPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
        }

        public RandomPool(Config config)
            : base(config)
        {
        }

        /// <summary>
        ///     Simple form of RandomPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public RandomPool(int nrOfInstances) : base(nrOfInstances, null, DefaultStrategy, null)
        {
        }

        /// <summary>
        ///     Simple form of RandomPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">
        ///     A <see cref="Resizer" /> for specifying how to grow the pool of underlying routees based on
        ///     pressure
        /// </param>
        public RandomPool(int nrOfInstances, Resizer resizer) : base(nrOfInstances, resizer, DefaultStrategy, null)
        {
        }

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
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RoundRobinRoutingLogic());
        }

        public class RandomPoolSurrogate : ISurrogate
        {
            public int NrOfInstances { get; set; }
            public bool UsePoolDispatcher { get; set; }
            public Resizer Resizer { get; set; }
            public SupervisorStrategy SupervisorStrategy { get; set; }
            public string RouterDispatcher { get; set; }

            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RandomPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }
        }
    }
}