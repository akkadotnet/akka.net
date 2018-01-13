//-----------------------------------------------------------------------
// <copyright file="TerminatedChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// This is the empty container which is installed after the last child has
    /// terminated while stopping; it is necessary to distinguish from the normal
    /// empty state while calling handleChildTerminated() for the last time.
    /// </summary>
    public sealed class TerminatedChildrenContainer : EmptyChildrenContainer
    {
        private TerminatedChildrenContainer()
        {
            //Intentionally left blank
        }
        /// <summary>
        /// TBD
        /// </summary>
        public new static IChildrenContainer Instance { get; } = new TerminatedChildrenContainer();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public override IChildrenContainer Add(string name, ChildRestartStats stats) => this;

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="name">N/A</param>
        /// <returns>N/A</returns>
        /// <exception cref="InvalidOperationException">This exception is automatically thrown since the name belongs to an actor that is already terminated.</exception>
        public override IChildrenContainer Reserve(string name) => throw new InvalidOperationException($"Cannot reserve actor name '{name}': already terminated");

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsTerminating => true;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsNormal => false;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Terminated";
    }
}

