using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

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

        public override void Send(object message, Actor.ActorRef sender)
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
        protected ScatterGatherFirstCompletedGroup()
        {
            
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
        public ScatterGatherFirstCompletedGroup(IEnumerable<string> paths,TimeSpan within) : base(paths)
        {
            Within = within;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScatterGatherFirstCompletedGroup" /> class.
        /// </summary>
        /// <param name="routees">The routees.</param>
        public ScatterGatherFirstCompletedGroup(IEnumerable<ActorRef> routees,TimeSpan within) : base(routees)
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
    }

    /// <summary>
    ///     Class RoundRobinPool.
    /// </summary>
    public class ScatterGatherFirstCompletedPool : Pool
    {
        private  TimeSpan _within;
        /// <summary>
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher,TimeSpan within, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            _within = within;
        }

        public ScatterGatherFirstCompletedPool(Config config) : base(config)
        {
            _within = config.GetTimeSpan("within");
        }

        protected ScatterGatherFirstCompletedPool()
        {
            
        }

        /// <summary>
        /// Simple form of RoundRobin constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public ScatterGatherFirstCompletedPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ScatterGatherFirstCompletedRoutingLogic(_within));
        }
    }
}
