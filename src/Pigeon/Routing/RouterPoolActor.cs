using Akka.Actor;

namespace Akka.Routing
{
    public class RouterPoolActor : RouterActor
    {
   //     private SupervisorStrategy supervisorStrategy;

        public RouterPoolActor(SupervisorStrategy supervisorStrategy)
        {
            this.supervisorStrategy = supervisorStrategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return supervisorStrategy;
        }
    }
}