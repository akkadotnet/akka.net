namespace Akka.Actor
{
    public abstract partial class ActorBase
    {
        internal SupervisorStrategy supervisorStrategy = null;

        internal SupervisorStrategy SupervisorStrategyLazy()
        {
            if (supervisorStrategy == null)
                supervisorStrategy = SupervisorStrategy();

            return supervisorStrategy;
        }

        protected virtual SupervisorStrategy SupervisorStrategy()
        {
            return Actor.SupervisorStrategy.DefaultStrategy;
        }
    }
}