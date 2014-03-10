using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Routing
{
    /// <summary>
    /// Class ThreadSafeRandom.
    /// </summary>
    public class ThreadSafeRandom
    {
        /// <summary>
        /// The random
        /// </summary>
        private static readonly Random random = new Random();
        /// <summary>
        /// The local
        /// </summary>
        [ThreadStatic] private static Random local;

        /// <summary>
        /// Nexts the specified maximum value.
        /// </summary>
        /// <param name="maxValue">The maximum value.</param>
        /// <returns>System.Int32.</returns>
        public int Next(int maxValue)
        {
            if (local == null)
            {
                int seed;
                lock (random)
                {
                    seed = random.Next();
                }
                local = new Random(seed);
            }

            return local.Next(maxValue);
        }
    }

    /// <summary>
    /// Class RandomLogic.
    /// </summary>
    public class RandomLogic : RoutingLogic
    {
        /// <summary>
        /// The random
        /// </summary>
        private readonly ThreadSafeRandom rnd = new ThreadSafeRandom();

        /// <summary>
        /// Selects the routee for the given message.
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
            return routees[rnd.Next(routees.Length - 1)%routees.Length];
        }
    }

    /// <summary>
    /// Class RandomGroup.
    /// </summary>
    public class RandomGroup : Group
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public RandomGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public RandomGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomGroup"/> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public RandomGroup(IEnumerable<string> paths)
            : base(paths)
        {
        }

        /// <summary>
        /// Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new RandomLogic(),GetRoutees(system).ToArray());
        }
    }
}