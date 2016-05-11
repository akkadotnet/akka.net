//-----------------------------------------------------------------------
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

            public IEnumerator<T> GetEnumerator()
            {
                return _enumerable.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private readonly IImmutableDictionary<string, IChildStats> _children;

        protected ChildrenContainerBase(IImmutableDictionary<string, IChildStats> children)
        {
            _children = children;
        }

        public virtual bool IsTerminating { get { return false; } }
        public virtual bool IsNormal { get { return true; } }
        public abstract IChildrenContainer Add(string name, ChildRestartStats stats);
        public abstract IChildrenContainer Remove(IActorRef child);
        public abstract IChildrenContainer Reserve(string name);
        public abstract IChildrenContainer ShallDie(IActorRef actor);
        public abstract IChildrenContainer Unreserve(string name);

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

        public IReadOnlyCollection<ChildRestartStats> Stats
        {
            get
            {
                var children = InternalChildren.Values.OfType<ChildRestartStats>();

                return new LazyReadOnlyCollection<ChildRestartStats>(children);
            }
        }

        protected IImmutableDictionary<string, IChildStats> InternalChildren { get { return _children; } }

        public bool TryGetByName(string name, out IChildStats stats)
        {
            if (InternalChildren.TryGetValue(name, out stats)) return true;
            stats = null;
            return false;
        }

        public bool TryGetByRef(IActorRef actor, out ChildRestartStats childRestartStats)
        {
            IChildStats stats;
            if (InternalChildren.TryGetValue(actor.Path.Name, out stats))
            {
                //Since the actor exists, ChildRestartStats is the only valid ChildStats.
                var crStats = stats as ChildRestartStats;
                if (crStats != null && actor.Equals(crStats.Child))
                {
                    childRestartStats = crStats;
                    return true;
                }
            }
            childRestartStats = null;
            return false;
        }

        public bool Contains(IActorRef actor)
        {
            ChildRestartStats stats;
            return TryGetByRef(actor, out stats);
        }

        protected void ChildStatsAppender(StringBuilder sb, KeyValuePair<string, IChildStats> kvp, int index)
        {
            sb.Append('<');
            var childStats = kvp.Value;
            var childRestartStats = childStats as ChildRestartStats;
            if (childRestartStats != null)
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

