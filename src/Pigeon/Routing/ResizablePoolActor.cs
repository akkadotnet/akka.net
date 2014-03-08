using Akka.Actor;

namespace Akka.Routing
{
    public class ResizablePoolActor : RouterActor
    {
        private SupervisorStrategy supervisorStrategy;

        public ResizablePoolActor(SupervisorStrategy supervisorStrategy)
        {
            this.supervisorStrategy = supervisorStrategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return supervisorStrategy;
        }
    }
}