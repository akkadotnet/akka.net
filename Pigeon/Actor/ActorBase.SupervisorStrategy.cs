using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract partial class ActorBase
    {
        private SupervisorStrategy supervisorStrategy = null;
        internal SupervisorStrategy SupervisorStrategyLazy()
        {
            if (supervisorStrategy == null)
                supervisorStrategy = SupervisorStrategy();

            return supervisorStrategy;
        }
        protected virtual SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(10, TimeSpan.FromSeconds(30), OneForOneStrategy.DefaultDecider);
        }
    }
}
