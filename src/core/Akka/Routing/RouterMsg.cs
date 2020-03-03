//-----------------------------------------------------------------------
// <copyright file="RouterMsg.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// This class contains convenience methods used to send messages to a <see cref="Router"/>.
    /// </summary>
    public static class RouterMessage
    {
        /// <summary>
        /// Sends a <see cref="RouterManagementMessage"/> to a <see cref="Router"/>
        /// to retrieve a list of routees that the router is currently using.
        /// </summary>
        public static readonly GetRoutees GetRoutees = GetRoutees.Instance;
    }

    /// <summary>
    /// This class represents a non-routed message that is processed by the <see cref="Router"/>.
    /// These types of messages are for managing the router itself, like adding routees, deleting
    /// routees, etc.
    /// </summary>
    public abstract class RouterManagementMessage
    {
    }

    /// <summary>
    /// This class represents a <see cref="RouterManagementMessage"/> sent to a <see cref="Router"/> instructing
    /// it to send a <see cref="Routees"/> message back to the requestor that lists the routees that the router
    /// is currently using.
    /// </summary>
    public sealed class GetRoutees : RouterManagementMessage
    {
        /// <summary>
        /// The singleton instance of GetRoutees.
        /// </summary>
        public static GetRoutees Instance { get; } = new GetRoutees();
    }

    /// <summary>
    /// This class represents a message used to carry information about what routees a <see cref="Router"/> is currently using.
    /// </summary>
    public sealed class Routees
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Routees"/> class.
        /// </summary>
        /// <param name="routees">The routees that a <see cref="Router"/> is currently using.</param>
        public Routees(IEnumerable<Routee> routees)
        {
            Members = routees.ToArray();
        }

        /// <summary>
        /// An enumeration of routees that a <see cref="Router"/> is currently using.
        /// </summary>
        public IEnumerable<Routee> Members { get; private set; }
    }

    /// <summary>
    /// This class represents a <see cref="RouterManagementMessage"/> sent to a <see cref="Router"/> instructing
    /// it to remove a specific routee from the router's collection of routees. It may be handled after other messages.
    /// 
    /// <note>
    /// For a pool with child routees the routee is stopped by sending a <see cref="PoisonPill"/>
    /// to the routee. Precautions are taken to reduce the risk of dropping messages that are concurrently
    /// being routed to the remove routee, but there are no guarantees. 
    /// </note>
    /// </summary>
    public sealed class RemoveRoutee : RouterManagementMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RemoveRoutee"/> class.
        /// </summary>
        /// <param name="routee">The routee to remove from the router's collection of routees.</param>
        public RemoveRoutee(Routee routee)
        {
            Routee = routee;
        }

        /// <summary>
        /// The routee removed from the router's collection of routees.
        /// </summary>
        public Routee Routee { get; private set; }
    }

    /// <summary>
    /// This class represents a <see cref="RouterManagementMessage"/> sent to a <see cref="Router"/> instructing
    /// it to add a specific routee to the router's collection of routees. It may be handled after other messages.
    /// </summary>
    public sealed class AddRoutee : RouterManagementMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AddRoutee"/> class.
        /// </summary>
        /// <param name="routee">The routee added to the router's collection of routees.</param>
        public AddRoutee(Routee routee)
        {
            Routee = routee;
        }

        /// <summary>
        /// The routee added to the router's collection of routees.
        /// </summary>
        public Routee Routee { get; private set; }
    }

    /// <summary>
    /// This class represents a <see cref="RouterManagementMessage"/> sent to a <see cref="Pool"/> router instructing
    /// it to increase or decrease the number of allotted routees the router can use. It may be handled after other messages.
    /// 
    /// <remarks>
    /// Positive <see cref="Change"/> will add that number of routees to the <see cref="Pool"/>.
    /// Negative <see cref="Change"/> will remove that number of routees from the <see cref="Pool"/>.
    /// </remarks>
    ///  <notes>
    /// Routees are stopped by sending a <see cref="PoisonPill"/> to the routee.
    /// Precautions are taken to reduce the risk of dropping messages that are concurrently
    /// being routed to the remove routee, but there are no guarantees. 
    /// </notes>
    /// </summary>
    public sealed class AdjustPoolSize : RouterManagementMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AdjustPoolSize"/> class.
        /// </summary>
        /// <param name="change">The number of routees to add or subtract from the <see cref="Pool"/>.</param>
        public AdjustPoolSize(int change)
        {
            Change = change;
        }

        /// <summary>
        /// The number of routees added or subtracted from the <see cref="Pool"/>.
        /// </summary>
        public int Change { get; private set; }
    }
}
