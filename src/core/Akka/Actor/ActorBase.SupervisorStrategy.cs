namespace Akka.Actor
{
    public abstract partial class ActorBase
    {
        private SupervisorStrategy _supervisorStrategy = null;

        /// <summary>
        /// Gets or sets a <see cref="SupervisorStrategy"/>.
        /// When getting, if a previously <see cref="SupervisorStrategy"/> has been set it's returned; otherwise calls
        /// <see cref="SupervisorStrategy">SupervisorStratregy()</see>, stores and returns it.
        /// </summary>
        internal SupervisorStrategy SupervisorStrategyInternal
        {
            get { return _supervisorStrategy ?? (_supervisorStrategy = SupervisorStrategy()); }
            set { _supervisorStrategy = value; }
        }

        protected virtual SupervisorStrategy SupervisorStrategy()
        {
            return Actor.SupervisorStrategy.DefaultStrategy;
        }
    }
}