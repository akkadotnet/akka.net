//-----------------------------------------------------------------------
// <copyright file="NormalChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
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
        private NormalChildrenContainer(IImmutableDictionary<string, IChildStats> children)
            : base(children)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="children">TBD</param>
        /// <returns>TBD</returns>
        public static IChildrenContainer Create(IImmutableDictionary<string, IChildStats> children)
        {
            if (children.Count == 0) return EmptyChildrenContainer.Instance;
            return new NormalChildrenContainer(children);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public override IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            return Create(InternalChildren.SetItem(name, stats));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public override IChildrenContainer Remove(IActorRef child)
        {
            return Create(InternalChildren.Remove(child.Path.Name));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public override IChildrenContainer ShallDie(IActorRef actor)
        {
            return new TerminatingChildrenContainer(InternalChildren, actor, SuspendReason.UserRequest.Instance);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        ///  <exception cref="InvalidActorNameException">This exception is thrown if the given <paramref name="name"/> is not unique in the container.</exception>
        /// <returns>TBD</returns>
        public override IChildrenContainer Reserve(string name)
        {
            if (InternalChildren.ContainsKey(name))
                throw new InvalidActorNameException($@"Actor name ""{name}"" is not unique!");
            return new NormalChildrenContainer(InternalChildren.SetItem(name, ChildNameReserved.Instance));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IChildrenContainer Unreserve(string name)
        {
            if (InternalChildren.TryGetValue(name, out var stats) && (stats is ChildNameReserved))
                return Create(InternalChildren.Remove(name));
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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

