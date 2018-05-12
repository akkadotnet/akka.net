//-----------------------------------------------------------------------
// <copyright file="ClusterEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// Domain events published to the event bus.
    /// Subscribe with:
    /// <code>
    /// var cluster = new Cluster(system);
    /// cluster.Subscribe(actorRef, typeof(IClusterDomainEvent));
    /// </code>
    /// </summary>
    public static class ClusterEvent
    {
        #region messages

        /// <summary>
        /// The mode for getting the current state of the cluster upon first subscribing.
        /// </summary>
        public enum SubscriptionInitialStateMode
        {
            /// <summary>
            /// When using this subscription mode a snapshot of
            /// <see cref="CurrentClusterState"/> will be sent to the
            /// subscriber as the first message.
            /// </summary>
            InitialStateAsSnapshot,
            /// <summary>
            /// When using this subscription mode the events corresponding
            /// to the current state will be sent to the subscriber to mimic what you would
            /// have seen if you were listening to the events when they occurred in the past.
            /// </summary>
            InitialStateAsEvents
        }

        /// <summary>
        /// Get the initial state as a <see cref="CurrentClusterState"/> message.
        /// </summary>
        public static readonly SubscriptionInitialStateMode InitialStateAsSnapshot =
            SubscriptionInitialStateMode.InitialStateAsSnapshot;

        /// <summary>
        /// Get the current state of the cluster played back as a series of <see cref="IMemberEvent"/>
        /// and <see cref="IReachabilityEvent"/> messages.
        /// </summary>
        public static readonly SubscriptionInitialStateMode InitialStateAsEvents =
            SubscriptionInitialStateMode.InitialStateAsEvents;

        /// <summary>
        /// Marker interface for cluster domain events
        /// </summary>
        public interface IClusterDomainEvent { }

        /// <summary>
        /// A snapshot of the current state of the <see cref="Cluster"/>
        /// </summary>
        public sealed class CurrentClusterState
        {
            /// <summary>
            /// Creates a new instance of the current cluster state.
            /// </summary>
            public CurrentClusterState() : this(
                ImmutableSortedSet<Member>.Empty,
                ImmutableHashSet<Member>.Empty,
                ImmutableHashSet<Address>.Empty,
                null,
                ImmutableDictionary<string, Address>.Empty,
                ImmutableHashSet<string>.Empty)
            { }

            /// <summary>
            /// Creates a new instance of the current cluster state.
            /// </summary>
            /// <param name="members">The current members of the cluster.</param>
            /// <param name="unreachable">The unreachable members of the cluster.</param>
            /// <param name="seenBy">The set of nodes who have seen us.</param>
            /// <param name="leader">The leader of the cluster.</param>
            /// <param name="roleLeaderMap">The list of role leaders.</param>
            public CurrentClusterState(
                ImmutableSortedSet<Member> members,
                ImmutableHashSet<Member> unreachable,
                ImmutableHashSet<Address> seenBy,
                Address leader,
                ImmutableDictionary<string, Address> roleLeaderMap,
                ImmutableHashSet<string> unreachableDataCenters)
            {
                Members = members;
                Unreachable = unreachable;
                SeenBy = seenBy;
                Leader = leader;
                RoleLeaderMap = roleLeaderMap;
                UnreachableDataCenters = unreachableDataCenters;
            }

            /// <summary>
            /// Get current member list
            /// </summary>
            public ImmutableSortedSet<Member> Members { get; }

            /// <summary>
            /// Get current unreachable set
            /// </summary>
            public ImmutableHashSet<Member> Unreachable { get; }

            /// <summary>
            /// Get current "seen-by" set
            /// </summary>
            public ImmutableHashSet<Address> SeenBy { get; }

            /// <summary>
            /// Get address of current leader, or null if noe
            /// </summary>
            public Address Leader { get; }

            /// <summary>
            /// All node roles in the cluster
            /// </summary>
            public ImmutableHashSet<string> AllRoles => RoleLeaderMap.Keys.ToImmutableHashSet();

            public ImmutableHashSet<string> UnreachableDataCenters { get; }

            /// <summary>
            /// Needed internally inside the <see cref="ClusterReadView"/>
            /// </summary>
            internal ImmutableDictionary<string, Address> RoleLeaderMap { get; }

            /// <summary>
            /// Get address of current leader, if any, within the role set
            /// </summary>
            /// <param name="role">The role we wish to check.</param>
            /// <returns>The address of the node who is the real leader, if any. Otherwise <c>null</c>.</returns>
            public Address RoleLeader(string role) => RoleLeaderMap.GetOrElse(role, null);

            /// <summary>
            /// Creates a deep copy of the <see cref="CurrentClusterState"/> and optionally allows you
            /// to specify different values for the outgoing objects
            /// </summary>
            /// <param name="members">TBD</param>
            /// <param name="unreachable">TBD</param>
            /// <param name="seenBy">TBD</param>
            /// <param name="leader">TBD</param>
            /// <param name="roleLeaderMap">TBD</param>
            /// <returns>TBD</returns>
            public CurrentClusterState Copy(
                ImmutableSortedSet<Member> members = null,
                ImmutableHashSet<Member> unreachable = null,
                ImmutableHashSet<Address> seenBy = null,
                Address leader = null,
                ImmutableDictionary<string, Address> roleLeaderMap = null,
                ImmutableHashSet<string> unreachableDataCenters = null)
            {
                return new CurrentClusterState(
                    members ?? Members,
                    unreachable ?? Unreachable,
                    seenBy ?? SeenBy,
                    leader ?? (Leader != null ? (Address)Leader.Clone() : null),
                    roleLeaderMap ?? RoleLeaderMap,
                    unreachableDataCenters ?? UnreachableDataCenters);
            }
        }

        /// <summary>
        /// This interface marks a given class as a membership event.
        /// The event is published when the state change is first seen on a node.
        /// The state change was performed by the leader when there was
        /// convergence on the leader node, i.e. all members had seen previous
        /// state.
        /// </summary>
        public interface IMemberEvent : IClusterDomainEvent
        {
            /// <summary>
            /// The node where the event occurred.
            /// </summary>
            Member Member { get; }
        }

        /// <summary>
        /// This class provides base functionality for defining state change events for cluster member nodes.
        /// </summary>
        public abstract class MemberStatusChange : IMemberEvent
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberStatusChange"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            /// <param name="validStatus">The state that the node changed towards.</param>
            /// <exception cref="ArgumentException">
            /// This exception is thrown if the node's current status doesn't match the given status, <paramref name="validStatus"/>.
            /// </exception>
            protected MemberStatusChange(Member member, MemberStatus validStatus)
            {
                if (member.Status != validStatus)
                    throw new ArgumentException($"Expected {validStatus} state, got: {member}");
                Member = member;
            }

            /// <summary>
            /// The cluster member node that changed status.
            /// </summary>
            public Member Member { get; }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is MemberStatusChange other && Member.Equals(other.Member);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + Member.GetHashCode();
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"{GetType()}(Member={Member})";
            }
        }

        /// <summary>
        /// This class represents a <see cref="MemberStatusChange"/> event where the
        /// cluster node changed its status to <see cref="MemberStatus.Joining"/>.
        /// </summary>
        public sealed class MemberJoined : MemberStatusChange
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberJoined"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            public MemberJoined(Member member)
                : base(member, MemberStatus.Joining) { }
        }

        /// <summary>
        /// This class represents a <see cref="MemberStatusChange"/> event where the
        /// cluster node changed its status to <see cref="MemberStatus.Up"/>.
        /// </summary>
        public sealed class MemberUp : MemberStatusChange
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberUp"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            public MemberUp(Member member)
                : base(member, MemberStatus.Up) { }
        }

        /// <summary>
        /// Member status changed to WeaklyUp.
        /// A joining member can be moved to <see cref="MemberStatus.WeaklyUp"/> if convergence
        /// cannot be reached, i.e. there are unreachable nodes.
        /// It will be moved to <see cref="MemberStatus.Up"/> when convergence is reached.
        /// </summary>
        public sealed class MemberWeaklyUp : MemberStatusChange
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberWeaklyUp"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            public MemberWeaklyUp(Member member)
                : base(member, MemberStatus.WeaklyUp) { }
        }

        /// <summary>
        /// This class represents a <see cref="MemberStatusChange"/> event where the
        /// cluster node changed its status to <see cref="MemberStatus.Leaving"/>.
        /// </summary>
        public sealed class MemberLeft : MemberStatusChange
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberJoined"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            public MemberLeft(Member member)
                : base(member, MemberStatus.Leaving) { }
        }

        /// <summary>
        /// This class represents a <see cref="MemberStatusChange"/> event where the
        /// cluster node changed its status to <see cref="MemberStatus.Exiting"/>.
        /// The node is removed when all members have seen the <see cref="MemberStatus.Exiting"/> status.
        /// </summary>
        public sealed class MemberExited : MemberStatusChange
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberJoined"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            public MemberExited(Member member)
                : base(member, MemberStatus.Exiting)
            {
            }
        }

        /// <summary>
        /// <para>
        /// This class represents a <see cref="MemberStatusChange"/> event where the
        /// cluster node changed its status to <see cref="MemberStatus.Removed"/>.
        /// </para>
        /// <para>
        /// When <see cref="MemberRemoved.PreviousStatus"/> is <see cref="MemberStatus.Down"/>
        /// the node was removed after being detected as unreachable and downed.
        /// </para>
        /// <para>
        /// When <see cref="MemberRemoved.PreviousStatus"/> is <see cref="MemberStatus.Exiting"/>
        /// the node was removed after graceful leaving and exiting.
        /// </para>
        /// </summary>
        public sealed class MemberRemoved : MemberStatusChange
        {
            /// <summary>
            /// The status of the node before the state change event.
            /// </summary>
            public MemberStatus PreviousStatus { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="MemberRemoved"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            /// <param name="previousStatus">The state that the node changed from.</param>
            /// <exception cref="ArgumentException">
            /// This exception is thrown if the node's current status doesn't match the <see cref="MemberStatus.Removed"/> status.
            /// </exception>
            public MemberRemoved(Member member, MemberStatus previousStatus)
                : base(member, MemberStatus.Removed)
            {
                if (member.Status != MemberStatus.Removed)
                    throw new ArgumentException($"Expected Removed status, got {member}");
                PreviousStatus = previousStatus;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is MemberRemoved other && (Member.Equals(other.Member) && PreviousStatus == other.PreviousStatus);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * +base.GetHashCode();
                    hash = hash * 23 + PreviousStatus.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// Leader of the cluster members changed. Published when the state change
        /// is first seen on a node.
        /// </summary>
        public sealed class LeaderChanged : IClusterDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="leader">TBD</param>
            public LeaderChanged(Address leader)
            {
                Leader = leader;
            }

            /// <summary>
            /// Address of current leader, or null if none
            /// </summary>
            public Address Leader { get; }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is LeaderChanged other && ((Leader == null && other.Leader == null) ||
                                                                                      (Leader != null && Leader.Equals(other.Leader)));

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + (Leader == null ? 0 : Leader.GetHashCode());
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"LeaderChanged(NewLeader={Leader})";
        }

        /// <summary>
        /// First member (leader) of the members within a role set changed.
        /// Published when the state change is first seen on a node.
        /// </summary>
        public sealed class RoleLeaderChanged : IClusterDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="role">TBD</param>
            /// <param name="leader">TBD</param>
            public RoleLeaderChanged(string role, Address leader)
            {
                Role = role;
                Leader = leader;
            }

            /// <summary>
            /// Address of current leader, or null if none
            /// </summary>
            public Address Leader { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public string Role { get; }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + Role.GetHashCode();
                    hash = hash * 23 + (Leader == null ? 0 : Leader.GetHashCode());
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return obj is RoleLeaderChanged other && (Role.Equals(other.Role)
                                                          && ((Leader == null && other.Leader == null) ||
                                                              (Leader != null && Leader.Equals(other.Leader))));
            }

            /// <inheritdoc/>
            public override string ToString() => $"RoleLeaderChanged(Leader={Leader}, Role={Role})";
        }

        /// <summary>
        /// Indicates that the <see cref="Cluster"/> plugin is shutting down.
        /// </summary>
        public sealed class ClusterShuttingDown : IClusterDomainEvent, IDeadLetterSuppression
        {
            private ClusterShuttingDown()
            {
            }

            /// <summary>
            /// Singleton instance.
            /// </summary>
            public static readonly IClusterDomainEvent Instance = new ClusterShuttingDown();

            /// <inheritdoc/>
            public override string ToString() => "ClusterShuttingDown";
        }

        /// <summary>
        /// A marker interface to facilitate the subscription of
        /// both <see cref="Akka.Cluster.ClusterEvent.UnreachableMember"/> and <see cref="Akka.Cluster.ClusterEvent.ReachableMember"/>.
        /// </summary>
        public interface IReachabilityEvent : IClusterDomainEvent
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class ReachabilityEvent : IReachabilityEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="member">TBD</param>
            protected ReachabilityEvent(Member member)
            {
                Member = member;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Member Member { get; }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is ReachabilityEvent other && Member.Equals(other.Member);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + Member.GetHashCode();
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"{GetType()}(Member={Member})";
        }

        /// <summary>
        /// A member is considered as unreachable by the failure detector.
        /// </summary>
        public sealed class UnreachableMember : ReachabilityEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="member">TBD</param>
            public UnreachableMember(Member member)
                : base(member)
            {
            }
        }

        /// <summary>
        /// A member is considered as reachable by the failure detector
        /// after having been unreachable.
        /// <see cref="Akka.Cluster.ClusterEvent.UnreachableMember"/>
        /// </summary>
        public sealed class ReachableMember : ReachabilityEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="member">TBD</param>
            public ReachableMember(Member member)
                : base(member)
            {
            }
        }

        /// <summary>
        /// Marker interface to facilitate subscription of
        /// both <see cref="UnreachableDataCenter"/> and <see cref="ReachableDataCenter"/>.
        /// </summary>
        public abstract class DataCenterReachabilityEvent : IClusterDomainEvent
        {
            public string DataCenter { get; }

            protected DataCenterReachabilityEvent(string dataCenter)
            {
                DataCenter = dataCenter;
            }

            protected bool Equals(DataCenterReachabilityEvent other)
            {
                return string.Equals(DataCenter, other.DataCenter);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((DataCenterReachabilityEvent)obj);
            }

            public override int GetHashCode()
            {
                return (DataCenter != null ? DataCenter.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// A data center is considered as unreachable when any members from the data center are unreachable.
        /// </summary>
        public sealed class UnreachableDataCenter : DataCenterReachabilityEvent
        {
            public UnreachableDataCenter(string dataCenter) : base(dataCenter) { }
        }

        /// <summary>
        /// A data center is considered reachable when all members from the data center are reachable.
        /// </summary>
        public sealed class ReachableDataCenter : DataCenterReachabilityEvent
        {
            public ReachableDataCenter(string dataCenter) : base(dataCenter) { }
        }

        /// <summary>
        /// The nodes that have seen current version of the Gossip.
        /// </summary>
        internal sealed class SeenChanged : IClusterDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="convergence">TBD</param>
            /// <param name="seenBy">TBD</param>
            public SeenChanged(bool convergence, ImmutableHashSet<Address> seenBy)
            {
                Convergence = convergence;
                SeenBy = seenBy;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public bool Convergence { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<Address> SeenBy { get; }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is SeenChanged other &&
                                                       (Convergence.Equals(other.Convergence) && SeenBy.SequenceEqual(other.SeenBy));

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + Convergence.GetHashCode();

                    foreach (var t in SeenBy)
                    {
                        hash = hash * 23 + t.GetHashCode();
                    }

                    return hash;
                }
            }

            public override string ToString() => $"SeenChanged(convergence: {Convergence}, seenBy: [{string.Join(",", SeenBy)}])";
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class ReachabilityChanged : IClusterDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="reachability">TBD</param>
            public ReachabilityChanged(Reachability reachability)
            {
                Reachability = reachability;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Reachability Reachability { get; }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is ReachabilityChanged other && Reachability.Equals(other.Reachability);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + Reachability.GetHashCode();
                    return hash;
                }
            }

            public override string ToString() => $"ReachabilityChanged({Reachability})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class CurrentInternalStats : IClusterDomainEvent
        {
            public CurrentInternalStats(GossipStats gossipStats, VectorClockStats vclockStats)
            {
                GossipStats = gossipStats;
                SeenBy = vclockStats;
            }

            public GossipStats GossipStats { get; }
            public VectorClockStats SeenBy { get; }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + GossipStats.GetHashCode();
                    hash = hash * 23 + SeenBy.GetHashCode();
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is CurrentInternalStats other &&
                                                       (GossipStats.Equals(other.GossipStats) && SeenBy.Equals(other.SeenBy));

            public override string ToString() => $"CurrentInternalStats(gossipStats: {GossipStats}, seenBy: {SeenBy})";
        }

        #endregion

        internal static IEnumerable<UnreachableMember> DiffUnreachable(MembershipState oldState, MembershipState newState)
        {
            if (ReferenceEquals(oldState, newState)) yield break;

            var newGossip = newState.LatestGossip;
            var oldUnreachableNodes = oldState.DcReachabilityNoOutsideNodes.AllUnreachableOrTerminated;
            foreach (var node in newState.DcReachabilityNoOutsideNodes.AllUnreachableOrTerminated)
            {
                if (!oldUnreachableNodes.Contains(node) && !node.Equals(newState.SelfUniqueAddress))
                    yield return new UnreachableMember(newGossip.GetMember(node));
            }
        }

        internal static IEnumerable<ReachableMember> DiffReachable(MembershipState oldState, MembershipState newState)
        {
            if (ReferenceEquals(oldState, newState)) yield break;

            var newGossip = newState.LatestGossip;
            foreach (var node in oldState.DcReachabilityNoOutsideNodes.AllUnreachable)
            {
                if (newGossip.HasMember(node) && newState.DcReachabilityNoOutsideNodes.IsReachable(node) && node != newState.SelfUniqueAddress)
                    yield return new ReachableMember(newGossip.GetMember(node));
            }
        }

        internal static bool IsReachable(MembershipState state, ImmutableHashSet<UniqueAddress> oldUnreachableNodes, string otherDc)
        {
            IEnumerable<UniqueAddress> UnrelatedDcNodes(MembershipState s)
            {
                foreach (var member in s.LatestGossip.Members)
                {
                    if (member.DataCenter != otherDc && member.DataCenter != s.SelfDataCenter)
                        yield return member.UniqueAddress;
                }
            }

            var unrelatedDcNodes = UnrelatedDcNodes(state);
            var reachabilityForOtherDc = state.DcReachabilityWithoutObservationsWithin.Remove(unrelatedDcNodes);
            return reachabilityForOtherDc.AllUnreachable.IsSubsetOf(oldUnreachableNodes);
        }

        internal static IEnumerable<UnreachableDataCenter> DiffUnreachableDataCenters(MembershipState oldState, MembershipState newState)
        {
            if (ReferenceEquals(oldState, newState)) yield break;

            var otherDcs = oldState.LatestGossip.AllDataCenters.Union(newState.LatestGossip.AllDataCenters).Remove(newState.SelfDataCenter);
            foreach (var dc in otherDcs)
            {
                if (!IsReachable(newState, oldState.DcReachability.AllUnreachableOrTerminated, dc))
                    yield return new UnreachableDataCenter(dc);
            }
        }

        internal static IEnumerable<ReachableDataCenter> DiffReachableDataCenters(MembershipState oldState, MembershipState newState)
        {
            if (ReferenceEquals(oldState, newState)) yield break;

            var otherDcs = oldState.LatestGossip.AllDataCenters.Union(newState.LatestGossip.AllDataCenters).Remove(newState.SelfDataCenter);
            foreach (var dc in otherDcs)
            {
                if (!IsReachable(oldState, ImmutableHashSet<UniqueAddress>.Empty, dc) && IsReachable(newState, ImmutableHashSet<UniqueAddress>.Empty, dc))
                    yield return new ReachableDataCenter(dc);
            }
        }

        internal static IEnumerable<IMemberEvent> DiffMemberEvents(MembershipState oldState, MembershipState newState)
        {
            if (ReferenceEquals(oldState, newState)) yield break;

            var newMembers = newState.LatestGossip.Members;
            var oldMembersMap = oldState.LatestGossip.Members.ToDictionary(m => m.UniqueAddress, m => m);

            foreach (var newMember in newMembers)
            {
                if (oldMembersMap.TryGetValue(newMember.UniqueAddress, out var oldMember))
                {
                    // check if member has changed
                    if (newMember.Status != oldMember.Status || newMember.UpNumber != oldMember.UpNumber)
                    {
                        var e = MemberToEvent(newMember);
                        if (e != null)
                            yield return e;
                    }
                }
                else
                {
                    var e = MemberToEvent(newMember);
                    if (e != null)
                        yield return e; // if member didn't exists
                }
            }

            var oldMembers = oldState.LatestGossip.Members;
            foreach (var oldMember in oldMembers)
            {
                if (!newMembers.Contains(oldMember))
                    yield return new MemberRemoved(oldMember.Copy(status: MemberStatus.Removed), oldMember.Status);
            }
        }

        private static IMemberEvent MemberToEvent(Member member)
        {
            switch (member.Status)
            {
                case MemberStatus.Joining: return new MemberJoined(member);
                case MemberStatus.WeaklyUp: return new MemberWeaklyUp(member);
                case MemberStatus.Up: return new MemberUp(member);
                case MemberStatus.Leaving: return new MemberLeft(member);
                case MemberStatus.Exiting: return new MemberExited(member);
                // no events for other transitions
                default: return null;
            }
        }

        internal static LeaderChanged DiffLeader(MembershipState oldState, MembershipState newState)
        {
            var newLeader = newState.Leader;
            if (newLeader != oldState.Leader)
            {
                return new LeaderChanged(newLeader?.Address);
            }
            else
            {
                return null;
            }
        }

        internal static IEnumerable<RoleLeaderChanged> DiffRolesLeader(MembershipState oldState, MembershipState newState)
        {
            foreach (var role in oldState.LatestGossip.AllRoles.Union(newState.LatestGossip.AllRoles))
            {
                var newLeader = newState.RoleLeader(role);
                if (newLeader != oldState.RoleLeader(role))
                    yield return new RoleLeaderChanged(role, newLeader?.Address);
            }
        }

        internal static SeenChanged DiffSeen(MembershipState oldState, MembershipState newState)
        {
            if (ReferenceEquals(oldState, newState)) return null;

            var newConvergence = newState.Convergence(ImmutableHashSet<UniqueAddress>.Empty);
            var newSeenBy = newState.LatestGossip.SeenBy;

            if (newConvergence != oldState.Convergence(ImmutableHashSet<UniqueAddress>.Empty) ||
                !newSeenBy.SetEquals(oldState.LatestGossip.SeenBy))
            {
                return new SeenChanged(newConvergence, newSeenBy.Select(m => m.Address).ToImmutableHashSet());
            }
            else return null;
        }

        internal static ReachabilityChanged DiffReachability(MembershipState oldState, MembershipState newState) =>
            ReferenceEquals(newState.Overview.Reachability, oldState.Overview.Reachability)
                ? null
                : new ReachabilityChanged(newState.Overview.Reachability);
    }

    internal sealed class ClusterDomainEventPublisher : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly UniqueAddress _selfUniqueAddress = Cluster.Get(Context.System).SelfUniqueAddress;
        private readonly MembershipState _emptyMembershipState;
        private MembershipState _membershipState;

        public string SelfDataCenter => _cluster.Settings.SelfDataCenter;

        /// <summary>
        /// Default constructor for ClusterDomainEventPublisher.
        /// </summary>
        public ClusterDomainEventPublisher()
        {
            _emptyMembershipState = new MembershipState(
                latestGossip: Gossip.Empty,
                selfUniqueAddress: _cluster.SelfUniqueAddress,
                selfDataCenter: _cluster.SelfDataCenter,
                crossDataCenterConnections: _cluster.Settings.MultiDataCenter.CrossDcConnections);
            _membershipState = _emptyMembershipState;

            _eventStream = Context.System.EventStream;
        }

        /// <inheritdoc cref="ActorBase.PreRestart"/>
        protected override void PreRestart(Exception reason, object message)
        {
            // don't postStop when restarted, no children to stop
        }

        /// <inheritdoc cref="ActorBase.PostStop"/>
        protected override void PostStop()
        {
            // publish the final removed state before shutting down
            Publish(ClusterEvent.ClusterShuttingDown.Instance);
            PublishChanges(_emptyMembershipState);
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InternalClusterAction.PublishChanges publishChanges: PublishChanges(publishChanges.State); return true;
                case ClusterEvent.CurrentInternalStats currentStats: PublishInternalStats(currentStats); return true;
                case InternalClusterAction.SendCurrentClusterState send: SendCurrentClusterState(send.Receiver); return true;
                case InternalClusterAction.Subscribe subscribe: Subscribe(subscribe.Subscriber, subscribe.InitialStateMode, subscribe.To); return true;
                case InternalClusterAction.Unsubscribe unsubscribe: Unsubscribe(unsubscribe.Subscriber, unsubscribe.To); return true;
                case InternalClusterAction.PublishEvent publishEvent: Publish(publishEvent.Event); return true;
                default: return false;
            }
        }

        private readonly EventStream _eventStream;

        /// <summary>
        /// The current snapshot state corresponding to latest gossip 
        /// to mimic what you would have seen if you were listening to the events.
        /// </summary>
        private void SendCurrentClusterState(IActorRef receiver)
        {
            var latestGossip = _membershipState.LatestGossip;
            var unreachable = _membershipState.DcReachabilityNoOutsideNodes.AllUnreachableOrTerminated
                .Where(node => node != _selfUniqueAddress)
                .Select(node => latestGossip.GetMember(node))
                .ToImmutableHashSet();

            var unreachableDataCenters = !latestGossip.IsMultiDc
                ? ImmutableHashSet<string>.Empty
                : latestGossip.AllDataCenters.Where(dc => ClusterEvent.IsReachable(_membershipState, ImmutableHashSet<UniqueAddress>.Empty, dc)).ToImmutableHashSet();

            var state = new ClusterEvent.CurrentClusterState(
                members: latestGossip.Members,
                unreachable: unreachable,
                seenBy: latestGossip.SeenBy.Select(s => s.Address).ToImmutableHashSet(),
                leader: _membershipState.Leader?.Address,
                roleLeaderMap: latestGossip.AllRoles.ToImmutableDictionary(r => r, r =>
                {
                    var leader = _membershipState.RoleLeader(r);
                    return leader?.Address;
                }),
                unreachableDataCenters: unreachableDataCenters);
            receiver.Tell(state);
        }

        private void Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initMode, IReadOnlyCollection<Type> to)
        {
            switch (initMode)
            {
                case ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents:
                    void Pub(object @event)
                    {
                        var eventType = @event.GetType();
                        if (to.Any(o => o.IsAssignableFrom(eventType)))
                            subscriber.Tell(@event);
                    }
                    PublishDiff(_emptyMembershipState, _membershipState, Pub);
                    break;

                case ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot:
                    SendCurrentClusterState(subscriber);
                    break;
            }

            foreach (var t in to) _eventStream.Subscribe(subscriber, t);
        }

        private void Unsubscribe(IActorRef subscriber, Type to)
        {
            if (to == null) _eventStream.Unsubscribe(subscriber);
            else _eventStream.Unsubscribe(subscriber, to);
        }

        private void PublishChanges(MembershipState newState)
        {
            var oldState = _membershipState;
            // keep the _latestGossip to be sent to new subscribers
            _membershipState = newState;
            PublishDiff(oldState, newState, Publish);
        }

        private void PublishDiff(MembershipState oldState, MembershipState newState, Action<object> pub)
        {
            foreach (var e in ClusterEvent.DiffMemberEvents(oldState, newState)) pub(e);
            foreach (var e in ClusterEvent.DiffUnreachable(oldState, newState)) pub(e);
            foreach (var e in ClusterEvent.DiffReachable(oldState, newState)) pub(e);
            foreach (var e in ClusterEvent.DiffUnreachableDataCenters(oldState, newState)) pub(e);
            foreach (var e in ClusterEvent.DiffReachableDataCenters(oldState, newState)) pub(e);

            var leader = ClusterEvent.DiffLeader(oldState, newState); if (leader != null) pub(leader);

            foreach (var e in ClusterEvent.DiffRolesLeader(oldState, newState)) pub(e);

            // publish internal SeenState for testing purposes
            var seen = ClusterEvent.DiffSeen(oldState, newState); if (seen != null) pub(seen);

            var reachability = ClusterEvent.DiffReachability(oldState, newState); if (reachability != null) pub(reachability);
        }

        private void PublishInternalStats(ClusterEvent.CurrentInternalStats currentStats) => Publish(currentStats);

        private void Publish(object @event)
        {
            _eventStream.Publish(@event);
        }
    }
}
