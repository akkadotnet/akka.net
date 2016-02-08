namespace Akka.Actor
{
    /// <summary>
    /// Default guardian supervision strategy.
    /// </summary>
    public sealed class DefaultSupervisorStrategy : ISupervisorStrategyConfigurator
    {
        /// <summary>
        /// Creates a new instance of <see cref="SupervisorStrategy"/>. 
        /// </summary>
        /// <returns>An instance of <see cref="SupervisorStrategy"/>.</returns>
        public SupervisorStrategy Create()
        {
            return SupervisorStrategy.DefaultStrategy;
        }
    }
}