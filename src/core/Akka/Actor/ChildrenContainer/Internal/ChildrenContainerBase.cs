//-----------------------------------------------------------------------
// <copyright file="ChildrenContainerBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Akka.Annotations;

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

        protected ChildrenContainerBase(IImmutableDictionary<string, IChildStats> children)
        {
            InternalChildren = children;
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
        
        protected IImmutableDictionary<string, IChildStats> InternalChildren { get; }
        
        public bool TryGetByName(string name, out IChildStats stats)
        {
            return InternalChildren.TryGetValue(name, out stats);
        }
        
        #nullable enable
        public bool TryGetByRef(IActorRef actor, [NotNullWhen(true)] out ChildRestartStats? childRestartStats)
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
        #nullable restore
        
        public bool Contains(IActorRef actor)
        {
            return TryGetByRef(actor, out _);
        }
        
        internal static void ChildStatsAppender(StringBuilder sb, KeyValuePair<string, IChildStats> kvp)
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
                sb.Append(kvp.Key).Append(':').Append(childStats).Append('>');
            }
        }
    }
}

