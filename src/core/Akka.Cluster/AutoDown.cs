//-----------------------------------------------------------------------
// <copyright file="AutoDown.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Configuration;
using static Akka.Cluster.MembershipState;

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
    internal sealed class AutoDown : AutoDownBase
    {
        /// <summary>
        /// Creates actor properties for the legacy automatic-downing actor.
        /// </summary>
        /// <param name="autoDownUnreachableAfter">How long a member must remain unreachable before it is considered for downing.</param>
        /// <param name="cluster">The cluster instance whose membership and leader state are observed.</param>
        /// <returns>Properties for creating an <see cref="AutoDown"/> actor.</returns>
        public static Props Props(TimeSpan autoDownUnreachableAfter, Cluster cluster)
        {
            return Actor.Props.Create(() => new AutoDown(autoDownUnreachableAfter, cluster));
        }

        /// <summary>
        /// Message sent to the actor when an unreachable member's configured delay expires.
        /// </summary>
        public sealed class UnreachableTimeout
        {
            /// <summary>
            /// The unique identity of the member whose unreachable timeout expired.
            /// </summary>
            public UniqueAddress Node { get; }

            /// <summary>
            /// Creates a timeout message for the specified member.
            /// </summary>
            /// <param name="node">The member whose timeout expired.</param>
            public UnreachableTimeout(UniqueAddress node)
            {
                Node = node;
            }

            private bool Equals(UnreachableTimeout other)
            {
                return Equals(Node, other.Node);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is UnreachableTimeout timeout && Equals(timeout);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return (Node != null ? Node.GetHashCode() : 0);
            }
        }

        private readonly Cluster _cluster;
        
        public AutoDown(TimeSpan autoDownUnreachableAfter, Cluster cluster) : base(autoDownUnreachableAfter)
        {
            _cluster = cluster;
        }

        /// <summary>
        /// The address of the local cluster node.
        /// </summary>
        public override Address SelfAddress
        {
            get { return _cluster.SelfAddress; }
        }

        /// <summary>
        /// The scheduler used to arrange unreachable-member timeouts.
        /// </summary>
        public override IScheduler Scheduler
        {
            get { return _cluster.Scheduler; }
        }

        /// <summary>
        /// Subscribes to cluster events before starting timeout processing.
        /// </summary>
        protected override void PreStart()
        {
            _cluster.Subscribe(Self,new []{ typeof(ClusterEvent.IClusterDomainEvent)});
            base.PreStart();
        }

        /// <summary>
        /// Unsubscribes from cluster events and cancels pending timeout tasks.
        /// </summary>
        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
            base.PostStop();
        }

        /// <summary>
        /// Down the specified cluster address through the cluster API.
        /// </summary>
        /// <param name="node">The address of the node to down.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown when a non-leader tries to down the specified <paramref name="node"/>.
        /// </exception>
        public override void Down(Address node)
        {
            if(!_leader) throw new InvalidOperationException("Must be leader to down node");
            _cluster.LogInfo("Leader is auto-downing unreachable node [{0}]", node);
            _cluster.Down(node);
        }
    }

    /// <summary>
    /// Tracks unreachable cluster members and asks the leader to down them after the configured delay.
    /// </summary>
    internal abstract class AutoDownBase : UntypedActor
    {
        private readonly ImmutableHashSet<MemberStatus> _skipMemberStatus =
           ConvergenceSkipUnreachableWithMemberStatus;

        private ImmutableDictionary<UniqueAddress, ICancelable> _scheduledUnreachable =
            ImmutableDictionary.Create<UniqueAddress, ICancelable>();
        private ImmutableHashSet<UniqueAddress> _pendingUnreachable = ImmutableHashSet.Create<UniqueAddress>();

        /// <summary>
        /// Whether this actor currently considers the local node the cluster leader.
        /// </summary>
        protected bool _leader = false;

        readonly TimeSpan _autoDownUnreachableAfter;

        /// <summary>
        /// Creates the testable automatic-downing logic with the configured unreachable delay.
        /// </summary>
        /// <param name="autoDownUnreachableAfter">How long a member must remain unreachable before it is considered for downing.</param>
        protected AutoDownBase(TimeSpan autoDownUnreachableAfter)
        {
            _autoDownUnreachableAfter = autoDownUnreachableAfter;
        }

        /// <summary>
        /// Cancels timeout tasks that are still scheduled when the actor stops.
        /// </summary>
        protected override void PostStop()
        {
            foreach (var tokenSource in _scheduledUnreachable.Values) tokenSource.Cancel();
        }

        /// <summary>
        /// The address of the node whose leadership and cluster events are being processed.
        /// </summary>
        public abstract Address SelfAddress { get; }

        /// <summary>
        /// The scheduler used to schedule unreachable-member timeouts.
        /// </summary>
        public abstract IScheduler Scheduler { get; }

        /// <summary>
        /// Requests that the current leader down the specified address.
        /// </summary>
        /// <param name="node">The address of the member to down.</param>
        public abstract void Down(Address node);

        /// <summary>
        /// Processes cluster state, reachability changes, leadership changes, and expired unreachable timers.
        /// </summary>
        /// <param name="message">The cluster event or timeout message to process.</param>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ClusterEvent.CurrentClusterState state:
                    _leader = state.Leader != null && state.Leader.Equals(SelfAddress);
                    foreach (var m in state.Unreachable) UnreachableMember(m);
                    return;
                case ClusterEvent.UnreachableMember unreachableMember:
                    UnreachableMember(unreachableMember.Member);
                    return;
                case ClusterEvent.ReachableMember reachableMember:
                    Remove(reachableMember.Member.UniqueAddress);
                    return;
                case ClusterEvent.MemberRemoved memberRemoved:
                    Remove(memberRemoved.Member.UniqueAddress);
                    return;
                case ClusterEvent.LeaderChanged leaderChanged:
                    _leader = leaderChanged.Leader != null && leaderChanged.Leader.Equals(SelfAddress);
                    if (_leader)
                    {
                        foreach(var node in _pendingUnreachable) Down(node.Address);
                        _pendingUnreachable = ImmutableHashSet.Create<UniqueAddress>();
                    }
                    return;
                case AutoDown.UnreachableTimeout unreachableTimeout:
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
            if(_scheduledUnreachable.TryGetValue(node, out var source))
                source.Cancel();
            _scheduledUnreachable = _scheduledUnreachable.Remove(node);
            _pendingUnreachable = _pendingUnreachable.Remove(node);
        }

        public ILoggingAdapter Log { get; private set; }
    }

    /// <summary>
    /// Used when no custom provider is configured and 'auto-down-unreachable-after' is enabled.
    /// </summary>
    [InternalApi] // really only used during MNTR for Akka.Cluster.Sharding
    public sealed class AutoDowning : IDowningProvider
    {
        private readonly ActorSystem _system;
        private readonly Cluster _cluster;
        
        public AutoDowning(ActorSystem system, Cluster cluster)
        {
            _system = system;
            _cluster = cluster;
        }

        /// <summary>
        /// The configured delay between downing an unreachable node and removing it from the cluster.
        /// </summary>
        public TimeSpan DownRemovalMargin => _cluster.Settings.DownRemovalMargin;

        /// <summary>
        /// Returns the auto-downing actor properties, requiring the legacy auto-down delay setting.
        /// </summary>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the <c>akka.cluster.auto-down-unreachable-after</c> configuration setting is not set.
        /// </exception>
        public Props DowningActorProps
        {
            get
            {
#pragma warning disable CS0618 // disable obsolete warning here because this entire class is obsolete
                var autoDownUnreachableAfter = _cluster.Settings.AutoDownUnreachableAfter;
#pragma warning restore CS0618
                if (!autoDownUnreachableAfter.HasValue)
                    throw new ConfigurationException("AutoDowning downing provider selected but 'akka.cluster.auto-down-unreachable-after' not set");

                return AutoDown.Props(autoDownUnreachableAfter.Value, _cluster);                    
            }
        }
    }
}
