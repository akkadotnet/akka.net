using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;

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
        private int next = -1;

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
            return routees[Interlocked.Increment(ref next)%routees.Length];
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
        [Obsolete("For serialization only",true)]
        public RoundRobinGroup()
        {
            
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
        ///     Creates the router actor.
        /// </summary>
        /// <returns>RouterActor.</returns>
        public override RouterActor CreateRouterActor()
        {
            throw new NotImplementedException();
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
}