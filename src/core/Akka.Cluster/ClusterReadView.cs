//-----------------------------------------------------------------------
// <copyright file="ClusterReadView.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Read view of the cluster's state, updated via subscription of
    /// cluster events published on the <see cref="EventBus{TEvent,TClassifier,TSubscriber}"/>.
    /// </summary>
    public class ClusterReadView : IDisposable
    {
        public ClusterEvent.CurrentClusterState State { get { return _state; } }

        /// <summary>
        /// Current state
        /// </summary>
        internal volatile ClusterEvent.CurrentClusterState _state;

        public Reachability Reachability { get { return _reachability; } }


        internal volatile Reachability _reachability;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal ClusterEvent.CurrentInternalStats LatestStats { get { return _latestStats; } }

        /// <summary>
        /// Current internal cluster stats, updated periodically via event bus.
        /// </summary>
        internal volatile ClusterEvent.CurrentInternalStats _latestStats;

        public ImmutableHashSet<NodeMetrics> ClusterMetrics { get { return _clusterMetrics; } }

        /// <summary>
        /// Current cluster metrics, updated periodically via event bus.
        /// </summary>
        internal volatile ImmutableHashSet<NodeMetrics> _clusterMetrics;

        readonly Address _selfAddress;

        public Address SelfAddress
        {
            get { return _selfAddress; }
        }

        readonly IActorRef _eventBusListener;

        private readonly Cluster _cluster;

        public ClusterReadView(Cluster cluster)
        {
            _cluster = cluster;
            _state = new ClusterEvent.CurrentClusterState();
            _reachability = Reachability.Empty;
            _latestStats = new ClusterEvent.CurrentInternalStats(new GossipStats(), new VectorClockStats());
            _clusterMetrics = ImmutableHashSet.Create<NodeMetrics>();
            _selfAddress = cluster.SelfAddress;

            _eventBusListener =
                cluster.System.SystemActorOf(
                    Props.Create(() => new EventBusListener(cluster, this))
                        .WithDispatcher(cluster.Settings.UseDispatcher)
                        .WithDeploy(Deploy.Local), "clusterEventBusListener");
        }

        /// <summary>
        /// actor that subscribers to cluster eventBus to update current read view state
        /// </summary>
        private class EventBusListener : ReceiveActor
        {
            readonly Cluster _cluster;
            private readonly ClusterReadView _readView;

            private ClusterEvent.CurrentClusterState State
            {
                get { return _readView._state; }
                set { _readView._state = value; }
            }

            public EventBusListener(Cluster cluster, ClusterReadView readView)
            {
                _cluster = cluster;
                _readView = readView;

                Receive<ClusterEvent.IClusterDomainEvent>(clusterDomainEvent =>
                {
                    clusterDomainEvent.Match()
                        .With<ClusterEvent.SeenChanged>(changed =>
                        {
                            State = State.Copy(seenBy: changed.SeenBy);
                        })
                        .With<ClusterEvent.ReachabilityChanged>(changed =>
                        {
                            _readView._reachability = changed.Reachability;
                        })
                        .With<ClusterEvent.MemberRemoved>(removed =>
                        {
                            State = State.Copy(members: State.Members.Remove(removed.Member),
                                unreachable: State.Unreachable.Remove(removed.Member));
                        })
                        .With<ClusterEvent.UnreachableMember>(member =>
                        {
                            // replace current member with new member (might have different status, only address is used in == comparison)
                            State = State.Copy(unreachable: State.Unreachable.Remove(member.Member).Add(member.Member));
                        })
                        .With<ClusterEvent.ReachableMember>(member =>
                        {
                            State = State.Copy(unreachable: State.Unreachable.Remove(member.Member));
                        })
                        .With<ClusterEvent.IMemberEvent>(memberEvent =>
                        {
                            var newUnreachable = State.Unreachable;
                            // replace current member with new member (might have different status, only address is used in == comparison)
                            if (State.Unreachable.Contains(memberEvent.Member))
                                newUnreachable = State.Unreachable.Remove(memberEvent.Member).Add(memberEvent.Member);
                            State = State.Copy(
                                members: State.Members.Remove(memberEvent.Member).Add(memberEvent.Member),
                                unreachable: newUnreachable);
                        })
                        .With<ClusterEvent.LeaderChanged>(changed =>
                        {
                            State = State.Copy(leader: changed.Leader);
                        })
                        .With<ClusterEvent.RoleLeaderChanged>(changed =>
                        {
                            State = State.Copy(roleLeaderMap: State.RoleLeaderMap.SetItem(changed.Role, changed.Leader));
                        })
                        .With<ClusterEvent.CurrentInternalStats>(stats =>
                        {
                            readView._latestStats = stats;
                        })
                        .With<ClusterEvent.ClusterMetricsChanged>(changed =>
                        {
                            readView._clusterMetrics = changed.NodeMetrics;
                        });
                });

                Receive<ClusterEvent.CurrentClusterState>(state =>
                {
                    State = state;
                });
            }

            protected override void PreStart()
            {
                //subscribe to all cluster domain events
                _cluster.Subscribe(Self, new []{ typeof(ClusterEvent.IClusterDomainEvent) });
            }

            protected override void PostStop()
            {
                //unsubscribe from all cluster domain events
                _cluster.Unsubscribe(Self);
            }
        }

        public Member Self
        {
            get
            {
                return _state.Members.SingleOrDefault(member => member.UniqueAddress == _cluster.SelfUniqueAddress)
                        .GetOrElse(
                            Member.Create(_cluster.SelfUniqueAddress, _cluster.SelfRoles).Copy(MemberStatus.Removed));
            }
        }

        /// <summary>
        /// Returns true if this cluster instance has been shutdown.
        /// </summary>
        public bool IsTerminated
        {
            get { return _cluster.IsTerminated; }
        }

        /// <summary>
        /// Current cluster members, sorted by address
        /// </summary>
        public ImmutableSortedSet<Member> Members
        {
            get { return State.Members; }
        }

        /// <summary>
        /// Members that have been detected as unreachable
        /// </summary>
        public ImmutableHashSet<Member> UnreachableMembers { get { return State.Unreachable; } }

        /// <summary>
        /// <see cref="MemberStatus"/> for this node.
        /// 
        /// NOTE: If the node has been removed from the cluster (and shut down) then it's status is set to the 'REMOVED' tombstone state
        /// and is no longer present in the node ring or any other part of the gossiping state. However in order to maintain the
        /// model and the semantics the user would expect, this method will in this situation return <see cref="MemberStatus.Removed"/>.
        /// </summary>
        public MemberStatus Status
        {
            get { return Self.Status; }
        }

        /// <summary>
        /// Get the address of the current leader.
        /// </summary>
        public Address Leader
        {
            get { return State.Leader; }
        }

        /// <summary>
        /// Is this node the leader?
        /// </summary>
        public bool IsLeader
        {
            get { return Leader == SelfAddress; }
        }

        /// <summary>
        /// Does the cluster consist of only one member?
        /// </summary>
        public bool IsSingletonCluster
        {
            get { return Members.Count == 1; }
        }

        /// <summary>
        /// Returns true if the node is no reachable and not <see cref="MemberStatus.Down"/>
        /// and not <see cref="MemberStatus.Removed"/>
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                var myself = Self;
                return !UnreachableMembers.Contains(myself) && myself.Status != MemberStatus.Down
                       && myself.Status != MemberStatus.Removed;
            }
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal void RefreshCurrentState()
        {
            _cluster.SendCurrentClusterState(_eventBusListener);
        }

        /// <summary>
        /// INTERNAL API
        /// The nodes that have seen current version of the <see cref="Gossip"/>
        /// </summary>
        internal ImmutableHashSet<Address> SeenBy { get { return State.SeenBy; } }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                //shutdown
                _eventBusListener.Tell(PoisonPill.Instance);
            }
        }
    }
}
