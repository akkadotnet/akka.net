using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

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

    /// <summary>
    /// Remove a specific routee by sending this message to the <see cref="Router"/>.
    /// It may be handled after other messages.
    /// 
    /// For a pool with child routees the routee is stopped by sending a <see cref="PoisonPill"/>
    /// to the routee. Precautions are taken to reduce the risk of dropping messages that are concurrently
    /// being routed to the remove routee, but there are no guarantees. 
    /// </summary>
    public sealed class RemoveRoutee : RouterManagementMesssage
    {
        public RemoveRoutee(Routee routee)
        {
            Routee = routee;
        }

        public Routee Routee { get; private set; }
    }

    /// <summary>
    /// Add a routee by sending this message to the router.
    /// It may be handled after other messages.
    /// </summary>
    public sealed class AddRoutee : RouterManagementMesssage
    {
        public AddRoutee(Routee routee)
        {
            Routee = routee;
        }

        public Routee Routee { get; private set; }
    }

    /// <summary>
    /// Increase or decrease the number of routees in a <see cref="Pool"/>.
    /// It may be handled after other messages.
    /// 
    /// Positive <see cref="Change"/> will add that number of routees to the <see cref="Pool"/>.
    /// Negative <see cref="Change"/> will remove that number of routees from the <see cref="Pool"/>.
    /// Routees are stopped by sending a <see cref="PoisonPill"/> to the routee.
    /// Precautions are taken to reduce the risk of dropping messages that are concurrently
    /// being routed to the remove routee, but there are no guarantees. 
    /// </summary>
    public sealed class AdjustPoolSize : RouterManagementMesssage
    {
        public AdjustPoolSize(int change)
        {
            Change = change;
        }

        public int Change { get; private set; }
    }
}