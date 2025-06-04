//-----------------------------------------------------------------------
// <copyright file="TerminatingChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
    /// the reason was "Terminating".
    /// </summary>
    public class TerminatingChildrenContainer : ChildrenContainerBase
    {
        public TerminatingChildrenContainer(IImmutableDictionary<string, IChildStats> children, IActorRef toDie, SuspendReason reason)
            : this(children, ImmutableHashSet<IActorRef>.Empty.Add(toDie), reason)
        {
            
        }
  
        public TerminatingChildrenContainer(IImmutableDictionary<string, IChildStats> children, ImmutableHashSet<IActorRef> toDie, SuspendReason reason)
            : base(children)
        {
            ToDie = toDie;
            Reason = reason;
        }

        public ImmutableHashSet<IActorRef> ToDie { get; }
        
        public SuspendReason Reason { get; }
        
        public override IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var newMap = InternalChildren.SetItem(name, stats);
            return new TerminatingChildrenContainer(newMap, ToDie, Reason);
        }
        
        public override IChildrenContainer Remove(IActorRef child)
        {
            var set = ToDie.Remove(child);
            if (set.IsEmpty)
            {
                if (Reason is SuspendReason.Termination) return TerminatedChildrenContainer.Instance;
                return NormalChildrenContainer.Create(InternalChildren.Remove(child.Path.Name));
            }
            return new TerminatingChildrenContainer(InternalChildren.Remove(child.Path.Name), set, Reason);
        }
        
        public override IChildrenContainer ShallDie(IActorRef actor)
        {
            return new TerminatingChildrenContainer(InternalChildren, ToDie.Add(actor), Reason);
        }
        
        public override IChildrenContainer Reserve(string name)
        {
            if (Reason is SuspendReason.Termination) throw new InvalidOperationException($@"Cannot reserve actor name ""{name}"". It is terminating.");
            if (InternalChildren.ContainsKey(name))
                throw new InvalidActorNameException($@"Actor name ""{name}"" is not unique!");
            else
                return new TerminatingChildrenContainer(InternalChildren.SetItem(name, ChildNameReserved.Instance), ToDie, Reason);
        }
        
        public override IChildrenContainer Unreserve(string name)
        {
            if (!InternalChildren.ContainsKey(name))
                return this;
            return new TerminatingChildrenContainer(InternalChildren.Remove(name), ToDie, Reason);
        }
        
        public override bool IsTerminating
        {
            get { return Reason is SuspendReason.Termination; }
        }
        
        public override bool IsNormal
        {
            get { return Reason is SuspendReason.UserRequest; }
        }
        
        public override string ToString()
        {
            var numberOfChildren = InternalChildren.Count;
            var sb = new StringBuilder();

            if (numberOfChildren > 10)
                sb.Append(numberOfChildren).Append(" children\n");
            else
            {
                sb.Append("Children:\n    ");
                foreach(var c in InternalChildren)
                {
                    ChildStatsAppender(sb, c);
                    sb.Append("\n    ");
                }
                sb.Append('\n');
            }
            
            var numberToDie = ToDie.Count;
            sb.Append(numberToDie).Append(" children terminating:\n    ");
            sb.AppendJoin("\n    ", ToDie);

            return sb.ToString();
        }
        
        public IChildrenContainer CreateCopyWithReason(SuspendReason reason)
        {
            return new TerminatingChildrenContainer(InternalChildren, ToDie, reason);
        }
    }
}

