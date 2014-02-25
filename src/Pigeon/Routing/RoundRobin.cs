using Pigeon.Actor;
using Pigeon.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RoundRobinRoutingLogic : RoutingLogic
    {
        private int next=-1;
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            else
            {
                return routees[Interlocked.Increment(ref next) % routees.Length];
            }
        }
    }

    public class RoundRobinGroup : Group
    {
        public RoundRobinGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {

        }
        public RoundRobinGroup(params string[] paths)
            : base(paths)
        {

        }
        public RoundRobinGroup(IEnumerable<string> paths) : base(paths)
        {

        }

        public RoundRobinGroup(IEnumerable<ActorRef> routees) : base(routees) { }

        public override RoutingLogic GetLogic()
        {
            return new RoundRobinRoutingLogic();
        }
    }
}
