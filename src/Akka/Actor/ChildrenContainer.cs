namespace Akka.Actor
{
    /// <summary>
    /// Class ChildrenContainer.
    /// </summary>
    public abstract class ChildrenContainer
    {
        /// <summary>
        /// The is normal
        /// </summary>
        public bool isNormal = true;
        /// <summary>
        /// The is terminating
        /// </summary>
        public bool isTerminating = false;
        /// <summary>
        /// Gets the children.
        /// </summary>
        /// <value>The children.</value>
        public abstract ActorRef[] children { get; }
        /// <summary>
        /// Gets the stats.
        /// </summary>
        /// <value>The stats.</value>
        public abstract ChildRestartStats[] stats { get; }
        /// <summary>
        /// Adds the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="stats">The stats.</param>
        public abstract void add(string name, ChildRestartStats stats);
        /// <summary>
        /// Removes the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        public abstract void remove(ActorRef child);

        /// <summary>
        /// Gets the name of the by.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ChildStats.</returns>
        public abstract ChildStats getByName(string name);
        /// <summary>
        /// Gets the by reference.
        /// </summary>
        /// <param name="actor">The actor.</param>
        /// <returns>ChildRestartStats.</returns>
        public abstract ChildRestartStats getByRef(ActorRef actor);

        /// <summary>
        /// Shalls the die.
        /// </summary>
        /// <param name="actor">The actor.</param>
        public abstract void shallDie(ActorRef actor);

        // reserve that name or throw an exception
        /// <summary>
        /// Reserves the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        public abstract void reserve(string name);
        // cancel a reservation
        /// <summary>
        /// Unreserves the specified name.
        /// </summary>
        /// <param name="name">The name.</param>
        public abstract void unreserve(string name);
    }

    /// <summary>
    /// Class ChildRestartStats.
    /// </summary>
    public class ChildRestartStats
    {
    }

    /// <summary>
    /// Class ChildStats.
    /// </summary>
    public class ChildStats
    {
    }
}