//-----------------------------------------------------------------------
// <copyright file="NormalChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Akka.Event;
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
        private NormalChildrenContainer(ConcurrentDictionary<string, IChildStats> children)
            : base(children)
        {
        }

        public static IChildrenContainer Create(ConcurrentDictionary<string, IChildStats> children)
        {
            if (children.IsEmpty) return EmptyChildrenContainer.Instance;
            return new NormalChildrenContainer(children);
        }

        public override IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var alreadyAdded = InternalChildren.TryAdd(name, stats);

            //TODO I think this is safe, validate...
            //System.Diagnostics.Debug.Assert(alreadyAdded, "If this occur, consider changing the above call to AddOrUpdate");

            return this;
        }

        public override IChildrenContainer Remove(IActorRef child)
        {
            IChildStats stats;
            InternalChildren.TryRemove(child.Path.Name, out stats);
            return this;
        }

        public override IChildrenContainer ShallDie(IActorRef actor)
        {
            return new TerminatingChildrenContainer(InternalChildren, actor, SuspendReason.UserRequest.Instance);
        }

        public override IChildrenContainer Reserve(string name)
        {
            if (!InternalChildren.TryAdd(name, ChildNameReserved.Instance))
                throw new InvalidActorNameException(string.Format("Actor name \"{0}\" is not unique!", name));

            return this;
        }

        public override IChildrenContainer Unreserve(string name)
        {
            IChildStats stats;
            if (InternalChildren.TryGetValue(name, out stats) && (stats is ChildNameReserved))
            {
                //TODO Same behavior as with the ImmutableDictionary, but validate if this is thread safe
                InternalChildren.TryRemove(name, out stats);
                return this;
            }
            return this;
        }

        public override string ToString()
        {
            var numberOfChildren = InternalChildren.Count;
            if (numberOfChildren > 20) return numberOfChildren + " children";
            var sb = new StringBuilder();

            sb.Append("Children:\n    ").AppendJoin("\n    ", InternalChildren, ChildStatsAppender);
            return sb.ToString();
        }


    }
}

