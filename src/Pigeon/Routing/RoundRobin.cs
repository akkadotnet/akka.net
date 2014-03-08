using System;
using System.Collections.Generic;
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
    ///     Class RoundRobinGroup.
    /// </summary>
    public class RoundRobinGroup : Group
    {
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
        public override Router CreateRouter()
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
        ///     Initializes a new instance of the <see cref="RoundRobinPool" /> class.
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

        /// <summary>
        ///     Gets the routees.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <returns>IEnumerable{Routee}.</returns>
        public override IEnumerable<Routee> GetRoutees(ActorSystem system)
        {
            throw new NotImplementedException();
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
        public override Router CreateRouter()
        {
            throw new NotImplementedException();
        }
    }
}