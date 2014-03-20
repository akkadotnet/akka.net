using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Routing
{
    public interface ConsistentHashable
    {
        object ConsistentHashKey { get; }
    }

    public class ConsistentHashableEnvelope : ConsistentHashable
    {
        public object Message { get; set; }
        public object HashKey { get; set; }

        public object ConsistentHashKey
        {
            get { return HashKey; }
        }
    }

    public class ConsistentHashingRoutingLogic : RoutingLogic
    {
        public override Routee Select(object message, Routee[] routees)
        {
            if (message is ConsistentHashable)
            {
                var hashable = (ConsistentHashable) message;
                int hash = hashable.ConsistentHashKey.GetHashCode();
                return routees[hash%routees.Length];
            }

            throw new NotSupportedException("Only ConsistentHashable messages are supported right now");
        }
    }

    public class ConsistentHashingGroup : Group
    {
        [Obsolete("For serialization only",true)]
        public ConsistentHashingGroup()
        {
        }

        public ConsistentHashingGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        public ConsistentHashingGroup(params string[] paths)
            : base(paths)
        {
        }

        public ConsistentHashingGroup(IEnumerable<string> paths)
            : base(paths)
        {
        }

        public ConsistentHashingGroup(IEnumerable<ActorRef> routees) : base(routees)
        {
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ConsistentHashingRoutingLogic());
        }
    }

    public class ConsistentHashingPool : Pool
    {

                /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public ConsistentHashingPool(Config config) : base(config)
        {
            
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public ConsistentHashingPool(int nrOfInstances, Resizer resizer,SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            
        }

        /// <summary>
        /// Simple form of ConsistentHashingPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public ConsistentHashingPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ConsistentHashingRoutingLogic());
        }
    }
}