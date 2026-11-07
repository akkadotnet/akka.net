//-----------------------------------------------------------------------
// <copyright file="EmptyChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// This is the empty container, shared among all leaf actors.
    /// </summary>
    public class EmptyChildrenContainer : IChildrenContainer
    {
        private static readonly ImmutableDictionary<string, IChildStats> _emptyStats = ImmutableDictionary<string, IChildStats>.Empty;

        /// <summary>
        /// Initializes the shared empty children container.
        /// </summary>
        protected EmptyChildrenContainer()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// The shared container representing an actor with no children.
        /// </summary>
        public static IChildrenContainer Instance { get; } = new EmptyChildrenContainer();

        /// <summary>
        /// Adds the first child and returns a populated container.
        /// </summary>
        /// <param name="name">The child name to add.</param>
        /// <param name="stats">The child's restart statistics.</param>
        /// <returns>A normal container containing the child.</returns>
        public virtual IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var newMap = _emptyStats.Add(name, stats);
            return NormalChildrenContainer.Create(newMap);
        }

        /// <summary>
        /// Leaves this empty container unchanged because it has no child references.
        /// </summary>
        /// <param name="child">The child reference to remove.</param>
        /// <returns>This empty container.</returns>
        public IChildrenContainer Remove(IActorRef child)
        {
            return this;
        }

        /// <summary>
        /// Reports that no entry exists for the supplied name.
        /// </summary>
        /// <param name="name">The child name to find.</param>
        /// <param name="stats">Set to <c>null</c>.</param>
        /// <returns>Always <c>false</c>.</returns>
        public bool TryGetByName(string name, out IChildStats stats)
        {
            stats = null;
            return false;
        }

        /// <summary>
        /// Reports that no child reference is present.
        /// </summary>
        /// <param name="actor">The child reference to find.</param>
        /// <param name="childRestartStats">Set to <c>null</c>.</param>
        /// <returns>Always <c>false</c>.</returns>
        public bool TryGetByRef(IActorRef actor, out ChildRestartStats childRestartStats)
        {
            childRestartStats = null;
            return false;
        }

        /// <summary>
        /// Reports that this container has no children.
        /// </summary>
        /// <param name="actor">The actor reference to check.</param>
        /// <returns>Always <c>false</c>.</returns>
        public bool Contains(IActorRef actor)
        {
            return false;
        }

        /// <summary>
        /// An empty collection of child references.
        /// </summary>
        public IReadOnlyCollection<IInternalActorRef> Children { get { return ImmutableList<IInternalActorRef>.Empty; } }

        /// <summary>
        /// An empty collection of child restart statistics.
        /// </summary>
        public IReadOnlyCollection<ChildRestartStats> Stats { get { return ImmutableList<ChildRestartStats>.Empty; } }

        /// <summary>
        /// Leaves this empty container unchanged because it has no child to terminate.
        /// </summary>
        /// <param name="actor">The child reference that terminated.</param>
        /// <returns>This empty container.</returns>
        public IChildrenContainer ShallDie(IActorRef actor)
        {
            return this;
        }

        /// <summary>
        /// Reserves a name and returns a populated container.
        /// </summary>
        /// <param name="name">The child name to reserve.</param>
        /// <returns>A normal container with the name marked as reserved.</returns>
        public virtual IChildrenContainer Reserve(string name)
        {
            return NormalChildrenContainer.Create(_emptyStats.Add(name, ChildNameReserved.Instance));
        }

        /// <summary>
        /// Leaves this container unchanged because it has no reservation to remove.
        /// </summary>
        /// <param name="name">The reserved child name to release.</param>
        /// <returns>This empty container.</returns>
        public IChildrenContainer Unreserve(string name)
        {
            return this;
        }

        /// <summary>
        /// A description of this empty container.
        /// </summary>
        /// <returns>The text <c>No children</c>.</returns>
        public override string ToString()
        {
            return "No children";
        }

        /// <summary>
        /// Always <c>false</c> because this container has no children and is not terminating.
        /// </summary>
        public virtual bool IsTerminating { get { return false; } }
        /// <summary>
        /// Always <c>true</c> because this container is in its normal state.
        /// </summary>
        public virtual bool IsNormal { get { return true; } }
    }
}
