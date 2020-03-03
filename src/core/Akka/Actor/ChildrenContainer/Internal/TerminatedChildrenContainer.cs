//-----------------------------------------------------------------------
// <copyright file="TerminatedChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private static readonly IChildrenContainer _instance = new TerminatedChildrenContainer();

        private TerminatedChildrenContainer()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// </summary>
        public new static IChildrenContainer Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public override bool IsTerminating { get { return true; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsNormal { get { return false; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "Terminated";
        }
    }
}

