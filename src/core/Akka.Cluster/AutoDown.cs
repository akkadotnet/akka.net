//-----------------------------------------------------------------------
// <copyright file="AutoDown.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
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
    internal class AutoDown : AutoDownBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="autoDownUnreachableAfter">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(TimeSpan autoDownUnreachableAfter)
        {
            return new Props(typeof(AutoDown), new object[]{autoDownUnreachableAfter});
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class UnreachableTimeout
        {
            readonly UniqueAddress _node;
            /// <summary>
            /// TBD
            /// </summary>
            public UniqueAddress Node { get { return _node; } }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="node">TBD</param>
            public UnreachableTimeout(UniqueAddress node)
            {
                _node = node;
            }

            //TODO: Equals etc
        }

        readonly Cluster _cluster;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="autoDownUnreachableAfter">TBD</param>
        public AutoDown(TimeSpan autoDownUnreachableAfter) : base(autoDownUnreachableAfter)
        {
            _cluster = Cluster.Get(Context.System);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Address SelfAddress
        {
            get { return _cluster.SelfAddress; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IScheduler Scheduler
        {
            get { return _cluster.Scheduler; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self,new []{ typeof(ClusterEvent.IClusterDomainEvent)});
            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            base.PostStop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <exception cref="InvalidOperationException">TBD</exception>
        public override void Down(Address node)
        {
            if(!_leader) throw new InvalidOperationException("Must be leader to down node");
            _cluster.LogInfo("Leader is auto-downing unreachable node [{0}]", node);
            _cluster.Down(node);
        }

    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class AutoDownBase : UntypedActor
    {
        readonly ImmutableHashSet<MemberStatus> _skipMemberStatus =
            Gossip.ConvergenceSkipUnreachableWithMemberStatus;

        ImmutableDictionary<UniqueAddress, ICancelable> _scheduledUnreachable =
            ImmutableDictionary.Create<UniqueAddress, ICancelable>();
        ImmutableHashSet<UniqueAddress> _pendingUnreachable = ImmutableHashSet.Create<UniqueAddress>();
        /// <summary>
        /// TBD
        /// </summary>
        protected bool _leader = false;

        readonly TimeSpan _autoDownUnreachableAfter;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="autoDownUnreachableAfter">TBD</param>
        protected AutoDownBase(TimeSpan autoDownUnreachableAfter)
        {
            _autoDownUnreachableAfter = autoDownUnreachableAfter;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            foreach (var tokenSource in _scheduledUnreachable.Values) tokenSource.Cancel();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract Address SelfAddress { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract IScheduler Scheduler { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        public abstract void Down(Address node);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
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

    /// <summary>
    /// Used when no custom provider is configured and 'auto-down-unreachable-after' is enabled.
    /// </summary>
    internal sealed class AutoDowning : IDowningProvider
    {
        private readonly ClusterSettings _clusterSettings;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public AutoDowning(ActorSystem system)
        {
            _clusterSettings = Cluster.Get(system).Settings;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan DownRemovalMargin => _clusterSettings.DownRemovalMargin;

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="ConfigurationException">TBD</exception>
        public Props DowningActorProps
        {
            get
            {
                if (_clusterSettings.AutoDownUnreachableAfter.HasValue)
                    return AutoDown.Props(_clusterSettings.AutoDownUnreachableAfter.Value);
                else 
                    throw new ConfigurationException("AutoDowning downing provider selected but 'akka.cluster.auto-down-unreachable-after' not set");
            }
        }
    }
}

