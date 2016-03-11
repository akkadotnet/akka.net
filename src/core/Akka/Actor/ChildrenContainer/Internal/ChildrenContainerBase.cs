//-----------------------------------------------------------------------
// <copyright file="ChildrenContainerBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
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
        private class WrappedReadonlyCollection<T> : IReadOnlyCollection<T>
        {
            private readonly IEnumerable<T> _enumerable;

            public int Count { get; }

            public WrappedReadonlyCollection(IEnumerable<T> enumerable, int count)
            {
                _enumerable = enumerable;
                Count = count;
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
                var children = from stat in InternalChildren.Values
                               let childRestartStats = stat as ChildRestartStats
                               where childRestartStats != null
                               select childRestartStats.Child;

                return new WrappedReadonlyCollection<IInternalActorRef>(children, InternalChildren.Count);
            }
        }

        public IReadOnlyCollection<ChildRestartStats> Stats
        {
            get
            {
                var children = from stat in InternalChildren.Values
                        let childRestartStats = stat as ChildRestartStats
                        where childRestartStats != null
                        select childRestartStats;

                return new WrappedReadonlyCollection<ChildRestartStats>(children, InternalChildren.Count);
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

