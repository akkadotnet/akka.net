using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// Class RouterPoolActor.
    /// </summary>
    public class RouterPoolActor : RouterActor
    {
   //     private SupervisorStrategy supervisorStrategy;

        /// <summary>
        /// Initializes a new instance of the <see cref="RouterPoolActor"/> class.
        /// </summary>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        public RouterPoolActor(SupervisorStrategy supervisorStrategy)
        {
            SupervisorStrategyInternal = supervisorStrategy;
        }

        /// <summary>
        /// Called when [receive].
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
            var terminated = message as Terminated;
            if (terminated != null)
            {
                var t = terminated;
                Cell.RemoveRoutee(new ActorRefRoutee(t.ActorRef), false);
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