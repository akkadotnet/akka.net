namespace Akka.Actor
{
    /// <summary>
    /// Stopping guardian supervision strategy.
    /// </summary>
    public sealed class StoppingSupervisorStrategy : ISupervisorStrategyConfigurator
    {
        /// <summary>
        /// Creates a new instance of <see cref="SupervisorStrategy"/>. 
        /// </summary>
        /// <returns>An instance of <see cref="SupervisorStrategy"/>.</returns>
        public SupervisorStrategy Create()
        {
            return SupervisorStrategy.StoppingStrategy;
        }
    }
}