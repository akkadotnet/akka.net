using System.Text;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Normal children container: we do have at least one child, but none of our
    /// children are currently terminating (which is the time period betweencalling 
    /// context.stop(child) and processing the ChildTerminated() system message).
    /// </summary>
    public class NormalChildrenContainer : ChildrenContainerBase
    {
        private NormalChildrenContainer(IImmutableMap<string, ChildStats> children)
            : base(children)
        {
        }

        public static ChildrenContainer Create(IImmutableMap<string, ChildStats> children)
        {
            if (children.IsEmpty) return EmptyChildrenContainer.Instance;
            return new NormalChildrenContainer(children);
        }

        public override ChildrenContainer Add(string name, ChildRestartStats stats)
        {
            return Create(InternalChildren.AddOrUpdate(name, stats));
        }

        public override ChildrenContainer Remove(ActorRef child)
        {
            return Create(InternalChildren.Remove(child.Path.Name));
        }

        public override ChildrenContainer ShallDie(ActorRef actor)
        {
            return new TerminatingChildrenContainer(InternalChildren, actor, new SuspendReason.UserRequest());
        }

        public override ChildrenContainer Reserve(string name)
        {
            if (InternalChildren.Contains(name))
                throw new InvalidActorNameException(string.Format("Actor name \"{0}\" is not unique!", name));
            return new NormalChildrenContainer(InternalChildren.AddOrUpdate(name, ChildNameReserved.Instance));
        }

        public override ChildrenContainer Unreserve(string name)
        {
            ChildStats stats;
            if (InternalChildren.TryGet(name, out stats) && (stats is ChildNameReserved))
            {
                return Create(InternalChildren.Remove(name));
            }
            return this;
        }

        public override string ToString()
        {
            var numberOfChildren = InternalChildren.Count;
            if (numberOfChildren > 20) return numberOfChildren + " children";
            var sb = new StringBuilder();

            sb.Append("Children:\n    ").AppendJoin("\n    ", InternalChildren.AllMinToMax, ChildStatsAppender);
            return sb.ToString();
        }


    }
}