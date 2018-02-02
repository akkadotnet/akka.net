﻿//-----------------------------------------------------------------------
// <copyright file="ChildrenContainerBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class ChildrenContainerBase : IChildrenContainer
    {
        private class LazyReadOnlyCollection<T> : IReadOnlyCollection<T>
        {
            private readonly IEnumerable<T> _enumerable;
            private int _lazyCount;

            public int Count
            {
                get
                {
                    int count = _lazyCount;

                    if (count == -1)
                        _lazyCount = count = _enumerable.Count();

                    return count;
                }
            }

            public LazyReadOnlyCollection(IEnumerable<T> enumerable)
            {
                _enumerable = enumerable;
                _lazyCount = -1;
            }

            /// <inheritdoc/>
            public IEnumerator<T> GetEnumerator()
            {
                return _enumerable.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="children">TBD</param>
        protected ChildrenContainerBase(ImmutableDictionary<string, IChildStats> children)
        {
            InternalChildren = children;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsTerminating => false;

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsNormal => true;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public abstract IChildrenContainer Add(string name, ChildRestartStats stats);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public abstract IChildrenContainer Remove(IActorRef child);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public abstract IChildrenContainer Reserve(string name);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public abstract IChildrenContainer ShallDie(IActorRef actor);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public abstract IChildrenContainer Unreserve(string name);

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<IInternalActorRef> Children
        {
            get
            {
                var children = InternalChildren.Values
                    .OfType<ChildRestartStats>()
                    .Select(item => item.Child);

                // The children collection must stay lazy evaluated
                return new LazyReadOnlyCollection<IInternalActorRef>(children);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<ChildRestartStats> Stats
        {
            get
            {
                var children = InternalChildren.Values.OfType<ChildRestartStats>();

                return new LazyReadOnlyCollection<ChildRestartStats>(children);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ImmutableDictionary<string, IChildStats> InternalChildren { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetByName(string name, out IChildStats stats)
        {
            if (InternalChildren.TryGetValue(name, out stats))
                return true;
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
            if (InternalChildren.TryGetValue(actor.Path.Name, out var stats))
            {
                //Since the actor exists, ChildRestartStats is the only valid ChildStats.
                if (stats is ChildRestartStats crStats && actor.Equals(crStats.Child))
                {
                    childRestartStats = crStats;
                    return true;
                }
            }
            childRestartStats = null;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains(IActorRef actor) => TryGetByRef(actor, out _);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sb">TBD</param>
        /// <param name="kvp">TBD</param>
        /// <param name="index">TBD</param>
        protected void ChildStatsAppender(StringBuilder sb, KeyValuePair<string, IChildStats> kvp, int index)
        {
            sb.Append('<');
            var childStats = kvp.Value;
            if (childStats is ChildRestartStats childRestartStats)
            {
                sb.Append(childRestartStats.Child.Path.ToStringWithUid()).Append(':');
                sb.Append(childRestartStats.MaxNrOfRetriesCount).Append(" retries>");
            }
            else
            {
                sb.Append(kvp.Key).Append(":").Append(childStats).Append('>');
            }
        }
    }
}

