using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    public abstract class ChildrenContainerBase : ChildrenContainer
    {
        private readonly IImmutableMap<string, ChildStats> _children;

        protected ChildrenContainerBase(IImmutableMap<string, ChildStats> children)
        {
            _children = children;
        }

        public virtual bool IsTerminating { get { return false; } }
        public virtual bool IsNormal { get { return true; } }
        public abstract ChildrenContainer Add(string name, ChildRestartStats stats);
        public abstract ChildrenContainer Remove(ActorRef child);
        public abstract ChildrenContainer Reserve(string name);
        public abstract ChildrenContainer ShallDie(ActorRef actor);
        public abstract ChildrenContainer Unreserve(string name);

        public IReadOnlyList<InternalActorRef> Children
        {
            get
            {
                return (from stat in InternalChildren.AllValuesMinToMax
                        let childRestartStats = stat as ChildRestartStats
                        where childRestartStats != null
                        select childRestartStats.Child).ToList();
            }
        }

        public IReadOnlyList<ChildRestartStats> Stats
        {
            get
            {
                return (from stat in InternalChildren.AllValuesMinToMax
                        let childRestartStats = stat as ChildRestartStats
                        where childRestartStats != null
                        select childRestartStats).ToList();
            }
        }

        protected IImmutableMap<string, ChildStats> InternalChildren { get { return _children; } }

        public bool TryGetByName(string name, out ChildStats stats)
        {
            if (InternalChildren.TryGet(name, out stats)) return true;
            stats = null;
            return false;
        }

        public bool TryGetByRef(ActorRef actor, out ChildRestartStats childRestartStats)
        {
            ChildStats stats;
            if (InternalChildren.TryGet(actor.Path.Name, out stats))
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

        public bool Contains(ActorRef actor)
        {
            ChildRestartStats stats;
            return TryGetByRef(actor, out stats);
        }

        protected void ChildStatsAppender(StringBuilder sb, IKeyValuePair<string, ChildStats> kvp, int index)
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