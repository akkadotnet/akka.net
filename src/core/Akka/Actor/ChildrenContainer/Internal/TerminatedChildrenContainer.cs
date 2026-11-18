//-----------------------------------------------------------------------
// <copyright file="TerminatedChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// This is the empty container which is installed after the last child has
    /// terminated while stopping; it is necessary to distinguish from the normal
    /// empty state while calling handleChildTerminated() for the last time.
    /// </summary>
    public class TerminatedChildrenContainer : EmptyChildrenContainer
    {
        private TerminatedChildrenContainer()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// The shared container installed after the last child terminates while the parent is shutting down.
        /// </summary>
        public new static IChildrenContainer Instance { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }
            = new TerminatedChildrenContainer();

        /// <summary>
        /// Ignores child additions because this container represents a completed termination state.
        /// </summary>
        /// <param name="name">The child name that was requested.</param>
        /// <param name="stats">The child statistics that would have been stored.</param>
        /// <returns>This terminated container.</returns>
        public override IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            return this;
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="name">N/A</param>
        /// <returns>N/A</returns>
        /// <exception cref="InvalidOperationException">This exception is automatically thrown since the name belongs to an actor that is already terminated.</exception>
        public override IChildrenContainer Reserve(string name)
        {
            throw new InvalidOperationException($"Cannot reserve actor name '{name}': already terminated");
        }

        /// <summary>
        /// Always <c>true</c> because this container represents a parent that is terminating.
        /// </summary>
        public override bool IsTerminating { get { return true; } }

        /// <summary>
        /// Always <c>false</c> because this container is no longer in its normal state.
        /// </summary>
        public override bool IsNormal { get { return false; } }

        /// <summary>
        /// A description of this terminated container.
        /// </summary>
        /// <returns>The text <c>Terminated</c>.</returns>
        public override string ToString()
        {
            return "Terminated";
        }
    }
}
