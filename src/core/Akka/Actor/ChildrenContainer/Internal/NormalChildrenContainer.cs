using System.Text;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Normal children container: we do have at least one child, but none of our
    /// children are currently terminating (which is the time period between calling
    /// context.stop(child) and processing the ChildTerminated() system message).
    /// </summary>
    public class NormalChildrenContainer : ChildrenContainerBase
    {
        private NormalChildrenContainer(IImmutableMap<string, IChildStats> children)
            : base(children)
        {
        }

        public static IChildrenContainer Create(IImmutableMap<string, IChildStats> children)
        {
            if (children.IsEmpty) return EmptyChildrenContainer.Instance;
            return new NormalChildrenContainer(children);
        }

        public override IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            return Create(InternalChildren.AddOrUpdate(name, stats));
        }

        public override IChildrenContainer Remove(IActorRef child)
        {
            return Create(InternalChildren.Remove(child.Path.Name));
        }

        public override IChildrenContainer ShallDie(IActorRef actor)
        {
            return new TerminatingChildrenContainer(InternalChildren, actor, SuspendReason.UserRequest.Instance);
        }

        public override IChildrenContainer Reserve(string name)
        {
            if (InternalChildren.Contains(name))
                throw new InvalidActorNameException(string.Format("Actor name \"{0}\" is not unique!", name));
            return new NormalChildrenContainer(InternalChildren.AddOrUpdate(name, ChildNameReserved.Instance));
        }

        public override IChildrenContainer Unreserve(string name)
        {
            IChildStats stats;
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