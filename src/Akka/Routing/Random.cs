using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Utils;

namespace Akka.Routing
{
    /// <summary>
    /// Class RandomLogic.
    /// </summary>
    public class RandomLogic : RoutingLogic
    {
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
            return routees[ThreadLocalRandom.Current.Next(routees.Length - 1)%routees.Length];
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
            return new Router(new RandomLogic());
        }
    }
}