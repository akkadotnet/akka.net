//-----------------------------------------------------------------------
// <copyright file="ChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public interface IChildrenContainer
    {
        /// <summary>
        /// Adds a child restart entry under a name.
        /// </summary>
        /// <param name="name">The child name or reserved name to add.</param>
        /// <param name="stats">The child's restart statistics or a name-reservation marker.</param>
        /// <returns>A container that includes the entry.</returns>
        IChildrenContainer Add(string name, ChildRestartStats stats);
        /// <summary>
        /// Removes the entry for a child reference.
        /// </summary>
        /// <param name="child">The child reference to remove.</param>
        /// <returns>A container with that child's entry removed.</returns>
        IChildrenContainer Remove(IActorRef child);
        /// <summary>
        /// Looks up an entry by child name.
        /// </summary>
        /// <param name="name">The name to find.</param>
        /// <param name="stats">Receives the child statistics or name-reservation marker when found.</param>
        /// <returns><c>true</c> if an entry exists under the name; otherwise, <c>false</c>.</returns>
        bool TryGetByName(string name, out IChildStats stats);
        /// <summary>
        /// Looks up the restart statistics for a child reference.
        /// </summary>
        /// <param name="actor">The child reference to find.</param>
        /// <param name="stats">Receives the child's restart statistics when found.</param>
        /// <returns><c>true</c> if the reference is a registered child; otherwise, <c>false</c>.</returns>
        #nullable enable
        bool TryGetByRef(IActorRef actor, [NotNullWhen(true)] out ChildRestartStats? stats);
        #nullable restore
        /// <summary>
        /// The live child references in this container.
        /// </summary>
        IReadOnlyCollection<IInternalActorRef> Children { get; }
        /// <summary>
        /// The restart statistics for live children in this container.
        /// </summary>
        IReadOnlyCollection<ChildRestartStats> Stats { get; }
        /// <summary>
        /// Marks a child as terminated while the parent is shutting down.
        /// </summary>
        /// <param name="actor">The child that terminated.</param>
        /// <returns>A container reflecting the termination state.</returns>
        IChildrenContainer ShallDie(IActorRef actor);
        /// <summary>
        /// Reserves a child name before the child reference is created.
        /// </summary>
        /// <param name="name">The name to reserve.</param>
        /// <returns>A container with the name marked as reserved.</returns>
        IChildrenContainer Reserve(string name);
        /// <summary>
        /// Releases a previously reserved child name.
        /// </summary>
        /// <param name="name">The reserved name to release.</param>
        /// <returns>A container with that reservation removed.</returns>
        IChildrenContainer Unreserve(string name);
        /// <summary>
        /// Whether the parent has begun terminating its children.
        /// </summary>
        bool IsTerminating { get; }
        /// <summary>
        /// Whether this container is in its normal, non-terminating state.
        /// </summary>
        bool IsNormal { get; }
        /// <summary>
        /// Checks whether the specified actor reference is registered as a child.
        /// </summary>
        /// <param name="actor">The actor reference to find.</param>
        /// <returns><c>true</c> if the actor is a child; otherwise, <c>false</c>.</returns>
        bool Contains(IActorRef actor);
    }
}
