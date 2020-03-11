//-----------------------------------------------------------------------
// <copyright file="ChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IChildrenContainer
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        IChildrenContainer Add(string name, ChildRestartStats stats);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        IChildrenContainer Remove(IActorRef child);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        bool TryGetByName(string name, out IChildStats stats);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        bool TryGetByRef(IActorRef actor, out ChildRestartStats stats);
        /// <summary>
        /// TBD
        /// </summary>
        IReadOnlyCollection<IInternalActorRef> Children { get; }
        /// <summary>
        /// TBD
        /// </summary>
        IReadOnlyCollection<ChildRestartStats> Stats { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        IChildrenContainer ShallDie(IActorRef actor);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        IChildrenContainer Reserve(string name);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        IChildrenContainer Unreserve(string name);
        /// <summary>
        /// TBD
        /// </summary>
        bool IsTerminating { get; }
        /// <summary>
        /// TBD
        /// </summary>
        bool IsNormal { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        bool Contains(IActorRef actor);
    }
}

