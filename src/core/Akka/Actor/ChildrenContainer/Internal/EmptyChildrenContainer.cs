//-----------------------------------------------------------------------
// <copyright file="EmptyChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private static readonly IChildrenContainer _instance = new EmptyChildrenContainer();

        /// <summary>
        /// TBD
        /// </summary>
        protected EmptyChildrenContainer()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static IChildrenContainer Instance { get { return _instance; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public virtual IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var newMap = _emptyStats.Add(name, stats);
            return NormalChildrenContainer.Create(newMap);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public IChildrenContainer Remove(IActorRef child)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetByName(string name, out IChildStats stats)
        {
            stats = null;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="childRestartStats">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetByRef(IActorRef actor, out ChildRestartStats childRestartStats)
        {
            childRestartStats = null;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains(IActorRef actor)
        {
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<IInternalActorRef> Children { get { return ImmutableList<IInternalActorRef>.Empty; } }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<ChildRestartStats> Stats { get { return ImmutableList<ChildRestartStats>.Empty; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IChildrenContainer ShallDie(IActorRef actor)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public virtual IChildrenContainer Reserve(string name)
        {
            return NormalChildrenContainer.Create(_emptyStats.Add(name, ChildNameReserved.Instance));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IChildrenContainer Unreserve(string name)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "No children";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsTerminating { get { return false; } }
        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsNormal { get { return true; } }
    }
}

