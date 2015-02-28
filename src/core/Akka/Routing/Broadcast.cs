using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Routing
{
    public class BroadcastRoutingLogic : RoutingLogic
    {
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || !routees.Any())
                return Routee.NoRoutee;
            return new SeveralRoutees(routees);
        }
    }

    /// <summary>
    /// Class BroadcastPool.
    /// </summary>
    public class BroadcastPool : Pool
    {
        public class BroadcastPoolSurrogate : ISurrogate
        {
            public object FromSurrogate(ActorSystem system)
            {
                return new BroadcastPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            public int NrOfInstances { get; set; }
            public bool UsePoolDispatcher { get; set; }
            public Resizer Resizer { get; set; }
            public SupervisorStrategy SupervisorStrategy { get; set; }
            public string RouterDispatcher { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new BroadcastPoolSurrogate
            {
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }

        protected BroadcastPool()
        {           
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public BroadcastPool(Config config) : base(config)
        {
            
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="BroadcastPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public BroadcastPool(int nrOfInstances, Resizer resizer,SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            
        }

        /// <summary>
        /// Simple form of BroadcastPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public BroadcastPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        /// <summary>
        /// Creates the router.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new BroadcastRoutingLogic());
        }
    }

    public class BroadcastGroup : Group
    {
        public class BroadcastGroupSurrogate : ISurrogate
        {
            public object FromSurrogate(ActorSystem system)
            {
                return new BroadcastGroup(Paths);
            }

            public string[] Paths { get; set; }
        }

        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new BroadcastGroupSurrogate
            {
                Paths = Paths,
            };
        }

        protected BroadcastGroup()
        {
            
        }
        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public BroadcastGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public BroadcastGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="paths">The paths.</param>
        public BroadcastGroup(IEnumerable<string> paths) : base(paths)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="BroadcastGroup" /> class.
        /// </summary>
        /// <param name="routees">The routees.</param>
        public BroadcastGroup(IEnumerable<ActorRef> routees)
            : base(routees)
        {
        }

        /// <summary>
        ///     Creates the router.
        /// </summary>
        /// <returns>Router.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new BroadcastRoutingLogic());
        }
    }
}
