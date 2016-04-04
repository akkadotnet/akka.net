//-----------------------------------------------------------------------
// <copyright file="AutoDown.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// An unreachable member will be downed by this actor if it remains unreachable
    /// for the specified duration and this actor is running on the leader node in the
    /// cluster.
    /// 
    /// The implementation is split into two classes AutoDown and AutoDownBase to be
    /// able to unit test the logic without running cluster.
    /// </summary>
    public class AutoDown : AutoDownBase
    {
        public static Props Props(TimeSpan autoDownUnreachableAfter)
        {
            return new Props(typeof(AutoDown), new object[]{autoDownUnreachableAfter});
        }

        public sealed class UnreachableTimeout
        {
            readonly UniqueAddress _node;
            public UniqueAddress Node { get { return _node; } }

            public UnreachableTimeout(UniqueAddress node)
            {
                _node = node;
            }

            //TODO: Equals etc
        }

        readonly Cluster _cluster;

        public AutoDown(TimeSpan autoDownUnreachableAfter) : base(autoDownUnreachableAfter)
        {
            _cluster = Cluster.Get(Context.System);
        }

        public override Address SelfAddress
        {
            get { return _cluster.SelfAddress; }
        }

        public override IScheduler Scheduler
        {
            get { return _cluster.Scheduler; }
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self,new []{ typeof(ClusterEvent.IClusterDomainEvent)});
            base.PreStart();
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            base.PostStop();
        }

        public override void Down(Address node)
        {
            if(!_leader) throw new InvalidOperationException("Must be leader to down node");
            _cluster.LogInfo("Leader is auto-downing unreachable node [{0}]", node);
            _cluster.Down(node);
        }

    }

    public abstract class AutoDownBase : UntypedActor
    {
        readonly ImmutableHashSet<MemberStatus> _skipMemberStatus =
            Gossip.ConvergenceSkipUnreachableWithMemberStatus;

        ImmutableDictionary<UniqueAddress, ICancelable> _scheduledUnreachable =
            ImmutableDictionary.Create<UniqueAddress, ICancelable>();
        ImmutableHashSet<UniqueAddress> _pendingUnreachable = ImmutableHashSet.Create<UniqueAddress>();
        protected bool _leader = false;

        readonly TimeSpan _autoDownUnreachableAfter;

        protected AutoDownBase(TimeSpan autoDownUnreachableAfter)
        {
            _autoDownUnreachableAfter = autoDownUnreachableAfter;
        }

        protected override void PostStop()
        {
            foreach (var tokenSource in _scheduledUnreachable.Values) tokenSource.Cancel();
        }

        public abstract Address SelfAddress { get; }

        public abstract IScheduler Scheduler { get; }

        public abstract void Down(Address node);

        protected override void OnReceive(object message)
        {
            var state = message as ClusterEvent.CurrentClusterState;
            if (state != null)
            {
                _leader = state.Leader != null && state.Leader.Equals(SelfAddress);
                foreach (var m in state.Unreachable) UnreachableMember(m);
                return;
            }

            var unreachableMember = message as ClusterEvent.UnreachableMember;
            if (unreachableMember != null)
            {
                UnreachableMember(unreachableMember.Member);
                return;
            }

            var reachableMember = message as ClusterEvent.ReachableMember;
            if (reachableMember != null)
            {
                Remove(reachableMember.Member.UniqueAddress);
                return;
            }
            var memberRemoved = message as ClusterEvent.MemberRemoved;
            if (memberRemoved != null)
            {
                Remove(memberRemoved.Member.UniqueAddress);
                return;
            }

            var leaderChanged = message as ClusterEvent.LeaderChanged;
            if (leaderChanged != null)
            {
                _leader = leaderChanged.Leader != null && leaderChanged.Leader.Equals(SelfAddress);
                if (_leader)
                {
                    foreach(var node in _pendingUnreachable) Down(node.Address);
                    _pendingUnreachable = ImmutableHashSet.Create<UniqueAddress>();
                }
                return;
            }

            var unreachableTimeout = message as AutoDown.UnreachableTimeout;
            if (unreachableTimeout != null)
            {
                if (_scheduledUnreachable.ContainsKey(unreachableTimeout.Node))
                {
                    _scheduledUnreachable = _scheduledUnreachable.Remove(unreachableTimeout.Node);
                    DownOrAddPending(unreachableTimeout.Node);
                }
                return;
            }
        }

        private void UnreachableMember(Member m)
        {
            if(!_skipMemberStatus.Contains(m.Status) && !_scheduledUnreachable.ContainsKey(m.UniqueAddress))
                ScheduleUnreachable(m.UniqueAddress);
        }

        private void ScheduleUnreachable(UniqueAddress node)
        {
            if (_autoDownUnreachableAfter == TimeSpan.Zero)
            {
                DownOrAddPending(node);
            }
            else
            {
                var cancelable = Scheduler.ScheduleTellOnceCancelable(_autoDownUnreachableAfter, Self, new AutoDown.UnreachableTimeout(node), Self);
                _scheduledUnreachable = _scheduledUnreachable.Add(node, cancelable);
            }
        }

        private void DownOrAddPending(UniqueAddress node)
        {
            if (_leader)
            {
                Down(node.Address);
            }
            else
            {
                // it's supposed to be downed by another node, current leader, but if that crash
                // a new leader must pick up these
                _pendingUnreachable = _pendingUnreachable.Add(node);
            }
        }

        private void Remove(UniqueAddress node)
        {
            ICancelable source;
            if(_scheduledUnreachable.TryGetValue(node, out source)) source.Cancel();
            _scheduledUnreachable = _scheduledUnreachable.Remove(node);
            _pendingUnreachable = _pendingUnreachable.Remove(node);
        }

        public ILoggingAdapter Log { get; private set; }
    }
}

