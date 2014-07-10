using System.Collections.Generic;
using System.Linq;

namespace Akka.Routing
{
    /// <summary>
    /// Class RouterMessage.
    /// </summary>
    public static class RouterMessage
    {
        /// <summary>
        /// The get routees
        /// </summary>
        public static readonly GetRoutees GetRoutees = new GetRoutees();
    }

    /// <summary>
    /// Class RouterManagementMesssage.
    /// </summary>
    public abstract class RouterManagementMesssage
    {
    }

    /// <summary>
    /// Class GetRoutees. This class cannot be inherited.
    /// </summary>
    public sealed class GetRoutees : RouterManagementMesssage
    {
    }

    /// <summary>
    /// Class Routees. This class cannot be inherited.
    /// </summary>
    public sealed class Routees
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Routees"/> class.
        /// </summary>
        /// <param name="routees">The routees.</param>
        public Routees(IEnumerable<Routee> routees)
        {
            Members = routees.ToArray();
        }

        /// <summary>
        /// Gets the members.
        /// </summary>
        /// <value>The members.</value>
        public IEnumerable<Routee> Members { get; private set; }
    }
}