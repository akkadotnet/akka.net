namespace Akka.Actor
{
    /// <summary>
    /// Implement this interface in order to configure the supervisorStrategy for
    /// the top-level guardian actor (`/user`). An instance of this class must be
    /// instantiable using a no-arg constructor.
    /// </summary>
    public interface ISupervisorStrategyConfigurator
    {
        /// <summary>
        /// Creates a new instance of <see cref="SupervisorStrategy"/>. 
        /// </summary>
        /// <returns>An instance of <see cref="SupervisorStrategy"/>.</returns>
        SupervisorStrategy Create();
    }
}