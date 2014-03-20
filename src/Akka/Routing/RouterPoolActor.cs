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

        protected override void OnReceive(object message)
        {
            var terminated = message as Terminated;
            if (terminated != null)
            {
                var t = terminated;
                Cell.RemoveRoutee(t.ActorRef, false);
                StopIfAllRouteesRemoved();
            }
                //if (message is AdjustPoolSize)
                //{
                
                //}
            else
            {
                base.OnReceive(message);
            }
        }


    }
}