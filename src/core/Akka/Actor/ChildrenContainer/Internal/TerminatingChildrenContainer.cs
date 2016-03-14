//-----------------------------------------------------------------------
// <copyright file="TerminatingChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Waiting state: there are outstanding termination requests (i.e. context.stop(child)
    /// was called but the corresponding ChildTerminated() system message has not yet been
    /// processed). There could be no specific reason (UserRequested), we could be Restarting
    /// or Terminating.
    /// Removing the last child which was supposed to be terminating will return a different
    /// type of container, depending on whether or not children are left and whether or not
    /// the reason was “Terminating”.
    /// </summary>
    public class TerminatingChildrenContainer : ChildrenContainerBase
    {
        private readonly ImmutableHashSet<IActorRef> _toDie;
        private readonly SuspendReason _reason;

        public TerminatingChildrenContainer(ConcurrentDictionary<string, IChildStats> children, IActorRef toDie, SuspendReason reason)
            : this(children, ImmutableHashSet<IActorRef>.Empty.Add(toDie), reason)
        {
            //Intentionally left blank
        }
        public TerminatingChildrenContainer(ConcurrentDictionary<string, IChildStats> children, ImmutableHashSet<IActorRef> toDie, SuspendReason reason)
            : base(children)
        {
            _toDie = toDie;
            _reason = reason;
        }

        public SuspendReason Reason { get { return _reason; } }

        public override IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var alreadyAdded = InternalChildren.TryAdd(name, stats);

            //System.Diagnostics.Debug.Assert(alreadyAdded, "If this occur, consider changing the above call to AddOrUpdate");
            return this;
        }

        public override IChildrenContainer Remove(IActorRef child)
        {
            IChildStats stats;
            var set = _toDie.Remove(child);
            if (set.IsEmpty)
            {
                if (_reason is SuspendReason.Termination) return TerminatedChildrenContainer.Instance;

                InternalChildren.TryRemove(child.Path.Name, out stats);
                return NormalChildrenContainer.Create(InternalChildren);
            }

            InternalChildren.TryRemove(child.Path.Name, out stats);

            return new TerminatingChildrenContainer(InternalChildren, set, _reason);
        }

        public override IChildrenContainer ShallDie(IActorRef actor)
        {
            return new TerminatingChildrenContainer(InternalChildren, _toDie.Add(actor), _reason);
        }

        public override IChildrenContainer Reserve(string name)
        {
            if (_reason is SuspendReason.Termination) throw new InvalidOperationException(string.Format("Cannot reserve actor name\"{0}\". Is terminating.", name));

            if (!InternalChildren.TryAdd(name, ChildNameReserved.Instance))
                throw new InvalidActorNameException(string.Format("Actor name \"{0}\" is not unique!", name));

            return this;
        }

        public override IChildrenContainer Unreserve(string name)
        {
            IChildStats stats;
            InternalChildren.TryRemove(name, out stats);

            return this;
        }

        public override bool IsTerminating
        {
            get { return _reason is SuspendReason.Termination; }
        }

        public override bool IsNormal
        {
            get { return _reason is SuspendReason.UserRequest; }
        }

        public override string ToString()
        {
            var numberOfChildren = InternalChildren.Count;
            var sb = new StringBuilder();

            if (numberOfChildren > 10)
                sb.Append(numberOfChildren).Append(" children\n");
            else
                sb.Append("Children:\n    ").AppendJoin("\n    ", InternalChildren, ChildStatsAppender).Append('\n');

            var numberToDie = _toDie.Count;
            sb.Append(numberToDie).Append(" children terminating:\n    ");
            sb.AppendJoin("\n    ", _toDie);

            return sb.ToString();
        }

        public IChildrenContainer CreateCopyWithReason(SuspendReason reason)
        {
            return new TerminatingChildrenContainer(InternalChildren, _toDie, reason);
        }
    }
}

