using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    /**
     * For testing Supervisor behavior, normally you don't supply the strategy
     * from the outside like this.
     */
    public class Supervisor : UntypedActor
    {
        private SupervisorStrategy supervisorStrategy;
        public Supervisor(SupervisorStrategy supervisorStrategy)
        {
            this.supervisorStrategy = supervisorStrategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return supervisorStrategy;
        }

        protected override void OnReceive(object message)
        {
            if (message is Props)
            {
                Sender.Tell(Context.ActorOf((Props)message));
            }
        }

        protected override void PreRestart(Exception cause, object message)
        {
            // need to override the default of stopping all children upon restart, tests rely on keeping them around
        }
    }
}
