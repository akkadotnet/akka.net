// -----------------------------------------------------------------------
//  <copyright file="ClusterReadView.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster;

/// <summary>
///     INTERNAL API
///     Read view of the cluster's state, updated via subscription of
///     cluster events published on the <see cref="EventBus{TEvent,TClassifier,TSubscriber}" />.
/// </summary>
internal class ClusterReadView : IDisposable
{
    private readonly Cluster _cluster;

    private readonly IActorRef _eventBusListener;

    /// <summary>
    ///     Current internal cluster stats, updated periodically via event bus.
    /// </summary>
    internal volatile ClusterEvent.CurrentInternalStats _latestStats;


    /// <summary>
    ///     TBD
    /// </summary>
    internal volatile Reachability _reachability;

    /// <summary>
    ///     Current state
    /// </summary>
    internal volatile ClusterEvent.CurrentClusterState _state;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="cluster">TBD</param>
    public ClusterReadView(Cluster cluster)
    {
        _cluster = cluster;
        _state = new ClusterEvent.CurrentClusterState();
        _reachability = Reachability.Empty;
        _latestStats = new ClusterEvent.CurrentInternalStats(new GossipStats(), new VectorClockStats());
        SelfAddress = cluster.SelfAddress;

        _eventBusListener =
            cluster.System.SystemActorOf(
                Props.Create(() => new EventBusListener(cluster, this))
                    .WithDispatcher(cluster.Settings.UseDispatcher)
                    .WithDeploy(Deploy.Local), "clusterEventBusListener");
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public ClusterEvent.CurrentClusterState State => _state;

    /// <summary>
    ///     TBD
    /// </summary>
    internal Reachability Reachability => _reachability;

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    internal ClusterEvent.CurrentInternalStats LatestStats => _latestStats;

    /// <summary>
    ///     TBD
    /// </summary>
    public Address SelfAddress { get; }

    /// <summary>
    ///     TBD
    /// </summary>
    public Member Self =>
        _state.Members.SingleOrDefault(member => member.UniqueAddress == _cluster.SelfUniqueAddress)
        ?? Member.Create(_cluster.SelfUniqueAddress, _cluster.SelfRoles, _cluster.Settings.AppVersion)
            .Copy(MemberStatus.Removed);

    /// <summary>
    ///     Returns true if this cluster instance has been shutdown.
    /// </summary>
    public bool IsTerminated => _cluster.IsTerminated;

    /// <summary>
    ///     Current cluster members, sorted by address
    /// </summary>
    public ImmutableSortedSet<Member> Members => State.Members;

    /// <summary>
    ///     Members that have been detected as unreachable
    /// </summary>
    public ImmutableHashSet<Member> UnreachableMembers => State.Unreachable;

    /// <summary>
    ///     <see cref="MemberStatus" /> for this node.
    ///     NOTE: If the node has been removed from the cluster (and shut down) then it's status is set to the 'REMOVED'
    ///     tombstone state
    ///     and is no longer present in the node ring or any other part of the gossiping state. However in order to maintain
    ///     the
    ///     model and the semantics the user would expect, this method will in this situation return
    ///     <see cref="MemberStatus.Removed" />.
    /// </summary>
    public MemberStatus Status => Self.Status;

    /// <summary>
    ///     Get the address of the current leader.
    /// </summary>
    public Address Leader => State.Leader;

    /// <summary>
    ///     Is this node the leader?
    /// </summary>
    public bool IsLeader => Leader == SelfAddress;

    /// <summary>
    ///     Does the cluster consist of only one member?
    /// </summary>
    public bool IsSingletonCluster => Members.Count == 1;

    /// <summary>
    ///     Returns true if the node is no reachable and not <see cref="MemberStatus.Down" />
    ///     and not <see cref="MemberStatus.Removed" />
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
    ///     INTERNAL API
    ///     The nodes that have seen current version of the <see cref="Gossip" />
    /// </summary>
    internal ImmutableHashSet<Address> SeenBy => State.SeenBy;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    internal void RefreshCurrentState()
    {
        _cluster.SendCurrentClusterState(_eventBusListener);
    }

    /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
    /// <param name="disposing">
    ///     if set to <c>true</c> the method has been called directly or indirectly by a
    ///     user's code. Managed and unmanaged resources will be disposed.<br />
    ///     if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only
    ///     unmanaged resources can be disposed.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            //shutdown
            _eventBusListener.Tell(PoisonPill.Instance);
    }

    /// <summary>
    ///     actor that subscribers to cluster eventBus to update current read view state
    /// </summary>
    private class EventBusListener : ReceiveActor
    {
        private readonly Cluster _cluster;
        private readonly ClusterReadView _readView;

        public EventBusListener(Cluster cluster, ClusterReadView readView)
        {
            _cluster = cluster;
            _readView = readView;

            Receive<ClusterEvent.IClusterDomainEvent>(clusterDomainEvent =>
            {
                switch (clusterDomainEvent)
                {
                    case ClusterEvent.SeenChanged changed:
                        State = State.Copy(seenBy: changed.SeenBy);
                        break;

                    case ClusterEvent.ReachabilityChanged changed:
                        _readView._reachability = changed.Reachability;
                        break;

                    case ClusterEvent.MemberRemoved removed:
                        State = State.Copy(State.Members.Remove(removed.Member),
                            State.Unreachable.Remove(removed.Member));
                        break;

                    case ClusterEvent.UnreachableMember member:
                        // replace current member with new member (might have different status, only address is used in == comparison)
                        State = State.Copy(unreachable: State.Unreachable.Remove(member.Member).Add(member.Member));
                        break;

                    case ClusterEvent.ReachableMember member:
                        State = State.Copy(unreachable: State.Unreachable.Remove(member.Member));
                        break;

                    case ClusterEvent.IMemberEvent memberEvent:
                        var newUnreachable = State.Unreachable;
                        // replace current member with new member (might have different status, only address is used in == comparison)
                        if (State.Unreachable.Contains(memberEvent.Member))
                            newUnreachable = State.Unreachable.Remove(memberEvent.Member).Add(memberEvent.Member);
                        State = State.Copy(
                            State.Members.Remove(memberEvent.Member).Add(memberEvent.Member),
                            newUnreachable);
                        break;

                    case ClusterEvent.LeaderChanged changed:
                        State = State.Copy(leader: changed.Leader);
                        break;

                    case ClusterEvent.RoleLeaderChanged changed:
                        State = State.Copy(roleLeaderMap: State.RoleLeaderMap.SetItem(changed.Role, changed.Leader));
                        break;

                    case ClusterEvent.CurrentInternalStats stats:
                        readView._latestStats = stats;
                        break;

                    case ClusterEvent.ClusterShuttingDown _:
                        // no-op
                        break;
                }

                // once captured, optional verbose logging of event
                if (!(clusterDomainEvent is ClusterEvent.SeenChanged) && _cluster.Settings.LogInfoVerbose)
                    _cluster.LogInfo("event {0}", clusterDomainEvent.GetType().Name);
            });

            Receive<ClusterEvent.CurrentClusterState>(state => { State = state; });
        }

        private ClusterEvent.CurrentClusterState State
        {
            get => _readView._state;
            set => _readView._state = value;
        }

        protected override void PreStart()
        {
            //subscribe to all cluster domain events
            _cluster.Subscribe(Self, typeof(ClusterEvent.IClusterDomainEvent));
        }

        protected override void PostStop()
        {
            //unsubscribe from all cluster domain events
            _cluster.Unsubscribe(Self);
        }
    }
}