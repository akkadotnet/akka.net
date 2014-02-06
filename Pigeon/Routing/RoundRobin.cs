using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    public class RoundRobinLogic : RoutingLogic
    {
        private int next;
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
}
