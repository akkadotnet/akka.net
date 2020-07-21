//-----------------------------------------------------------------------
// <copyright file="ClusterEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Newtonsoft.Json;

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
    public class ClusterEvent
    {
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
        public interface IClusterDomainEvent : IDeadLetterSuppression { }

        /// <summary>
        /// A snapshot of the current state of the <see cref="Cluster"/>
        /// </summary>
        public sealed class CurrentClusterState : INoSerializationVerificationNeeded
        {
            private readonly ImmutableSortedSet<Member> _members;
            private readonly ImmutableHashSet<Member> _unreachable;
            private readonly ImmutableHashSet<Address> _seenBy;
            private readonly Address _leader;
            private readonly ImmutableDictionary<string, Address> _roleLeaderMap;

            /// <summary>
            /// Creates a new instance of the current cluster state.
            /// </summary>
            public CurrentClusterState() : this(
                ImmutableSortedSet<Member>.Empty,
                ImmutableHashSet<Member>.Empty,
                ImmutableHashSet<Address>.Empty,
                null,
                ImmutableDictionary<string, Address>.Empty)
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
                ImmutableDictionary<string, Address> roleLeaderMap)
            {
                _members = members;
                _unreachable = unreachable;
                _seenBy = seenBy;
                _leader = leader;
                _roleLeaderMap = roleLeaderMap;
            }

            /// <summary>
            /// Get current member list
            /// </summary>
            public ImmutableSortedSet<Member> Members
            {
                get { return _members; }
            }

            /// <summary>
            /// Get current unreachable set
            /// </summary>
            public ImmutableHashSet<Member> Unreachable
            {
                get { return _unreachable; }
            }

            /// <summary>
            /// Get current "seen-by" set
            /// </summary>
            public ImmutableHashSet<Address> SeenBy
            {
                get { return _seenBy; }
            }

            /// <summary>
            /// Get address of current leader, or null if noe
            /// </summary>
            public Address Leader
            {
                get { return _leader; }
            }

            /// <summary>
            /// All node roles in the cluster
            /// </summary>
            public ImmutableHashSet<string> AllRoles
            {
                get { return _roleLeaderMap.Keys.ToImmutableHashSet(); }
            }

            /// <summary>
            /// Needed internally inside the <see cref="ClusterReadView"/>
            /// </summary>
            internal ImmutableDictionary<string, Address> RoleLeaderMap
            {
                get { return _roleLeaderMap; }
            }

            /// <summary>
            /// Get address of current leader, if any, within the role set
            /// </summary>
            /// <param name="role">The role we wish to check.</param>
            /// <returns>The address of the node who is the real leader, if any. Otherwise <c>null</c>.</returns>
            public Address RoleLeader(string role)
            {
                return _roleLeaderMap.GetOrElse(role, null);
            }

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
                ImmutableDictionary<string, Address> roleLeaderMap = null)
            {
                return new CurrentClusterState(
                    members ?? _members,
                    unreachable ?? _unreachable,
                    seenBy ?? _seenBy,
                    leader ?? (_leader != null ? (Address)_leader.Clone() : null),
                    roleLeaderMap ?? _roleLeaderMap);
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
            /// TBD
            /// </summary>
            protected readonly Member _member;

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
                _member = member;
            }

            /// <summary>
            /// The cluster member node that changed status.
            /// </summary>
            public Member Member
            {
                get { return _member; }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as MemberStatusChange;
                if (other == null) return false;
                return _member.Equals(other._member);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _member.GetHashCode();
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
        /// Member status changed to `MemberStatus.Down` and will be removed
        /// when all members have seen the `Down` status.
        /// </summary>
        public sealed class MemberDowned : MemberStatusChange
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MemberJoined"/> class.
            /// </summary>
            /// <param name="member">The node that changed state.</param>
            public MemberDowned(Member member)
                : base(member, MemberStatus.Down)
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
            readonly MemberStatus _previousStatus;

            /// <summary>
            /// The status of the node before the state change event.
            /// </summary>
            public MemberStatus PreviousStatus
            {
                get { return _previousStatus; }
            }

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
                _previousStatus = previousStatus;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as MemberRemoved;
                if (other == null) return false;
                return _member.Equals(other._member) && _previousStatus == other._previousStatus;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * +base.GetHashCode();
                    hash = hash * 23 + _previousStatus.GetHashCode();
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
            private readonly Address _leader;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="leader">TBD</param>
            public LeaderChanged(Address leader)
            {
                _leader = leader;
            }

            /// <summary>
            /// Address of current leader, or null if none
            /// </summary>
            public Address Leader
            {
                get { return _leader; }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as LeaderChanged;
                if (other == null) return false;
                return (_leader == null && other._leader == null) || (_leader != null && _leader.Equals(other._leader));
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + (_leader == null ? 0 : _leader.GetHashCode());
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"LeaderChanged(NewLeader={Leader})";
            }
        }

        /// <summary>
        /// First member (leader) of the members within a role set changed.
        /// Published when the state change is first seen on a node.
        /// </summary>
        public sealed class RoleLeaderChanged : IClusterDomainEvent
        {
            private readonly Address _leader;
            private readonly string _role;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="role">TBD</param>
            /// <param name="leader">TBD</param>
            public RoleLeaderChanged(string role, Address leader)
            {
                _role = role;
                _leader = leader;
            }

            /// <summary>
            /// Address of current leader, or null if none
            /// </summary>
            public Address Leader
            {
                get { return _leader; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public string Role
            {
                get { return _role; }
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _role.GetHashCode();
                    hash = hash * 23 + (_leader == null ? 0 : _leader.GetHashCode());
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as RoleLeaderChanged;
                if (other == null) return false;
                return _role.Equals(other._role)
                    && ((_leader == null && other._leader == null) || (_leader != null && _leader.Equals(other._leader)));
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"RoleLeaderChanged(Leader={Leader}, Role={Role})";
            }
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
            public override string ToString()
            {
                return "ClusterShuttingDown";
            }
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
            private readonly Member _member;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="member">TBD</param>
            protected ReachabilityEvent(Member member)
            {
                _member = member;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Member Member
            {
                get { return _member; }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ReachabilityEvent;
                if (other == null) return false;
                return _member.Equals(other._member);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _member.GetHashCode();
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
        /// The nodes that have seen current version of the Gossip.
        /// </summary>
        internal sealed class SeenChanged : IClusterDomainEvent
        {
            private readonly bool _convergence;
            private readonly ImmutableHashSet<Address> _seenBy;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="convergence">TBD</param>
            /// <param name="seenBy">TBD</param>
            public SeenChanged(bool convergence, ImmutableHashSet<Address> seenBy)
            {
                _convergence = convergence;
                _seenBy = seenBy;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public bool Convergence
            {
                get { return _convergence; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableHashSet<Address> SeenBy
            {
                get { return _seenBy; }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as SeenChanged;
                if (other == null) return false;
                return _convergence.Equals(other._convergence) && _seenBy.SequenceEqual(other._seenBy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _convergence.GetHashCode();

                    foreach (var t in _seenBy)
                    {
                        hash = hash * 23 + t.GetHashCode();
                    }

                    return hash;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class ReachabilityChanged : IClusterDomainEvent
        {
            private readonly Reachability _reachability;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="reachability">TBD</param>
            public ReachabilityChanged(Reachability reachability)
            {
                _reachability = reachability;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Reachability Reachability
            {
                get { return _reachability; }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ReachabilityChanged;
                if (other == null) return false;
                return _reachability.Equals(other._reachability);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _reachability.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class CurrentInternalStats : IClusterDomainEvent
        {
            private readonly GossipStats _gossipStats;
            private readonly VectorClockStats _vclockStats;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="gossipStats">TBD</param>
            /// <param name="vclockStats">TBD</param>
            public CurrentInternalStats(GossipStats gossipStats, VectorClockStats vclockStats)
            {
                _gossipStats = gossipStats;
                _vclockStats = vclockStats;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public GossipStats GossipStats
            {
                get { return _gossipStats; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public VectorClockStats SeenBy
            {
                get { return _vclockStats; }
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + _gossipStats.GetHashCode();
                    hash = hash * 23 + _vclockStats.GetHashCode();
                    return hash;
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as CurrentInternalStats;
                if (other == null) return false;
                return _gossipStats.Equals(other._gossipStats) && _vclockStats.Equals(other._vclockStats);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldGossip">TBD</param>
        /// <param name="newGossip">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        internal static ImmutableList<UnreachableMember> DiffUnreachable(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList<UnreachableMember>.Empty;
            }

            var oldUnreachableNodes = oldGossip.Overview.Reachability.AllUnreachableOrTerminated;
            return newGossip.Overview.Reachability.AllUnreachableOrTerminated
                    .Where(node => !oldUnreachableNodes.Contains(node) && !node.Equals(selfUniqueAddress))
                    .Select(node => new UnreachableMember(newGossip.GetMember(node)))
                    .ToImmutableList();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldGossip">TBD</param>
        /// <param name="newGossip">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        internal static ImmutableList<ReachableMember> DiffReachable(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList<ReachableMember>.Empty;
            }

            return oldGossip.Overview.Reachability.AllUnreachable
                    .Where(node => newGossip.HasMember(node) && newGossip.Overview.Reachability.IsReachable(node) && !node.Equals(selfUniqueAddress))
                    .Select(node => new ReachableMember(newGossip.GetMember(node)))
                    .ToImmutableList();
        }

        /// <summary>
        /// Compares two <see cref="Gossip"/> instances and uses them to publish the appropriate <see cref="IMemberEvent"/>
        /// for any given change to the membership of the current cluster.
        /// </summary>
        /// <param name="oldGossip">The previous gossip instance.</param>
        /// <param name="newGossip">The new gossip instance.</param>
        /// <returns>A possibly empty set of membership events to be published to all subscribers.</returns>
        internal static ImmutableList<IMemberEvent> DiffMemberEvents(Gossip oldGossip, Gossip newGossip)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList<IMemberEvent>.Empty;
            }

            var newMembers = newGossip.Members.Except(oldGossip.Members);
            var membersGroupedByAddress = newGossip.Members
                .Concat(oldGossip.Members)
                .GroupBy(m => m.UniqueAddress);

            var changedMembers = membersGroupedByAddress
                .Where(g => g.Count() == 2
                && (g.First().Status != g.Skip(1).First().Status
                    || g.First().UpNumber != g.Skip(1).First().UpNumber))
                .Select(g => g.First());

            var memberEvents = CollectMemberEvents(newMembers.Union(changedMembers));
            var removedMembers = oldGossip.Members.Except(newGossip.Members);
            var removedEvents = removedMembers.Select(m => new MemberRemoved(m.Copy(status: MemberStatus.Removed), m.Status));

            return memberEvents.Concat(removedEvents).ToImmutableList();
        }

        private static IEnumerable<IMemberEvent> CollectMemberEvents(IEnumerable<Member> members)
        {
            foreach (var member in members)
            {
                switch (member.Status)
                {
                    case MemberStatus.Joining:
                        yield return new MemberJoined(member);
                        break;
                    case MemberStatus.WeaklyUp:
                        yield return new MemberWeaklyUp(member);
                        break;
                    case MemberStatus.Up:
                        yield return new MemberUp(member);
                        break;
                    case MemberStatus.Leaving:
                        yield return new MemberLeft(member);
                        break;
                    case MemberStatus.Exiting:
                        yield return new MemberExited(member);
                        break;
                    case MemberStatus.Down:
                        yield return new MemberDowned(member);
                        break;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldGossip">TBD</param>
        /// <param name="newGossip">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        internal static ImmutableList<LeaderChanged> DiffLeader(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            var newLeader = newGossip.Leader(selfUniqueAddress);
            if ((newLeader == null && oldGossip.Leader(selfUniqueAddress) == null)
                || newLeader != null && newLeader.Equals(oldGossip.Leader(selfUniqueAddress)))
                return ImmutableList<LeaderChanged>.Empty;

            return ImmutableList.Create(newLeader == null
                ? new LeaderChanged(null)
                : new LeaderChanged(newLeader.Address));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldGossip">TBD</param>
        /// <param name="newGossip">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        internal static ImmutableHashSet<RoleLeaderChanged> DiffRolesLeader(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            return InternalDiffRolesLeader(oldGossip, newGossip, selfUniqueAddress).ToImmutableHashSet();
        }

        private static IEnumerable<RoleLeaderChanged> InternalDiffRolesLeader(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            foreach (var role in oldGossip.AllRoles.Union(newGossip.AllRoles))
            {
                var newLeader = newGossip.RoleLeader(role, selfUniqueAddress);
                if (newLeader == null && oldGossip.RoleLeader(role, selfUniqueAddress) != null)
                    yield return new RoleLeaderChanged(role, null);
                if (newLeader != null && !newLeader.Equals(oldGossip.RoleLeader(role, selfUniqueAddress)))
                    yield return new RoleLeaderChanged(role, newLeader.Address);
            }
        }

        /// <summary>
        /// Used for checking convergence when we don't have any information from the cluster daemon.
        /// </summary>
        private static readonly HashSet<UniqueAddress> EmptySet = new HashSet<UniqueAddress>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldGossip">TBD</param>
        /// <param name="newGossip">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        internal static ImmutableList<SeenChanged> DiffSeen(Gossip oldGossip, Gossip newGossip, UniqueAddress selfUniqueAddress)
        {
            if (newGossip.Equals(oldGossip))
            {
                return ImmutableList<SeenChanged>.Empty;
            }

            var newConvergence = newGossip.Convergence(selfUniqueAddress, EmptySet);
            var newSeenBy = newGossip.SeenBy;
            if (!newConvergence.Equals(oldGossip.Convergence(selfUniqueAddress, EmptySet)) || !newSeenBy.SequenceEqual(oldGossip.SeenBy))
            {
                return ImmutableList.Create(new SeenChanged(newConvergence, newSeenBy.Select(s => s.Address).ToImmutableHashSet()));
            }

            return ImmutableList<SeenChanged>.Empty;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldGossip">TBD</param>
        /// <param name="newGossip">TBD</param>
        /// <returns>TBD</returns>
        internal static ImmutableList<ReachabilityChanged> DiffReachability(Gossip oldGossip, Gossip newGossip)
        {
            if (newGossip.Overview.Reachability.Equals(oldGossip.Overview.Reachability))
                return ImmutableList<ReachabilityChanged>.Empty;

            return ImmutableList.Create(new ReachabilityChanged(newGossip.Overview.Reachability));
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Publishes <see cref="ClusterEvent"/>s out to all subscribers.
    /// </summary>
    internal sealed class ClusterDomainEventPublisher : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private Gossip _latestGossip;
        private readonly UniqueAddress _selfUniqueAddress = Cluster.Get(Context.System).SelfUniqueAddress;

        /// <summary>
        /// Default constructor for ClusterDomainEventPublisher.
        /// </summary>
        public ClusterDomainEventPublisher()
        {
            _latestGossip = Gossip.Empty;
            _eventStream = Context.System.EventStream;

            Receive<InternalClusterAction.PublishChanges>(newGossip => PublishChanges(newGossip.NewGossip));
            Receive<ClusterEvent.CurrentInternalStats>(currentStats => PublishInternalStats(currentStats));
            Receive<InternalClusterAction.SendCurrentClusterState>(receiver => SendCurrentClusterState(receiver.Receiver));
            Receive<InternalClusterAction.Subscribe>(sub => Subscribe(sub.Subscriber, sub.InitialStateMode, sub.To));
            Receive<InternalClusterAction.Unsubscribe>(unsub => Unsubscribe(unsub.Subscriber, unsub.To));
            Receive<InternalClusterAction.PublishEvent>(evt => Publish(evt));
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
            PublishChanges(Gossip.Empty);
        }

        private readonly EventStream _eventStream;

        /// <summary>
        /// The current snapshot state corresponding to latest gossip
        /// to mimic what you would have seen if you were listening to the events.
        /// </summary>
        private void SendCurrentClusterState(IActorRef receiver)
        {
            var unreachable = _latestGossip.Overview.Reachability.AllUnreachableOrTerminated
                .Where(node => !node.Equals(_selfUniqueAddress))
                .Select(node => _latestGossip.GetMember(node))
                .ToImmutableHashSet();

            var state = new ClusterEvent.CurrentClusterState(
                members: _latestGossip.Members,
                unreachable: unreachable,
                seenBy: _latestGossip.SeenBy.Select(s => s.Address).ToImmutableHashSet(),
                leader: _latestGossip.Leader(_selfUniqueAddress) == null ? null : _latestGossip.Leader(_selfUniqueAddress).Address,
                roleLeaderMap: _latestGossip.AllRoles.ToImmutableDictionary(r => r, r =>
                {
                    var leader = _latestGossip.RoleLeader(r, _selfUniqueAddress);
                    return leader == null ? null : leader.Address;
                }));
            receiver.Tell(state);
        }

        private void Subscribe(IActorRef subscriber, ClusterEvent.SubscriptionInitialStateMode initMode, IEnumerable<Type> to)
        {
            if (initMode == ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents)
            {
                Action<object> pub = @event =>
                {
                    var eventType = @event.GetType();
                    if (to.Any(o => o.IsAssignableFrom(eventType)))
                        subscriber.Tell(@event);
                };
                PublishDiff(Gossip.Empty, _latestGossip, pub);
            }
            else if (initMode == ClusterEvent.SubscriptionInitialStateMode.InitialStateAsSnapshot)
            {
                SendCurrentClusterState(subscriber);
            }

            foreach (var t in to) _eventStream.Subscribe(subscriber, t);
        }

        private void Unsubscribe(IActorRef subscriber, Type to)
        {
            if (to == null) _eventStream.Unsubscribe(subscriber);
            else _eventStream.Unsubscribe(subscriber, to);
        }

        private void PublishChanges(Gossip newGossip)
        {
            var oldGossip = _latestGossip;
            // keep the _latestGossip to be sent to new subscribers
            _latestGossip = newGossip;
            PublishDiff(oldGossip, newGossip, Publish);
        }

        private void PublishDiff(Gossip oldGossip, Gossip newGossip, Action<object> pub)
        {
            foreach (var @event in ClusterEvent.DiffMemberEvents(oldGossip, newGossip)) pub(@event);
            foreach (var @event in ClusterEvent.DiffUnreachable(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffReachable(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffLeader(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffRolesLeader(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            // publish internal SeenState for testing purposes
            foreach (var @event in ClusterEvent.DiffSeen(oldGossip, newGossip, _selfUniqueAddress)) pub(@event);
            foreach (var @event in ClusterEvent.DiffReachability(oldGossip, newGossip)) pub(@event);
        }

        private void PublishInternalStats(ClusterEvent.CurrentInternalStats currentStats)
        {
            Publish(currentStats);
        }

        private void Publish(object @event)
        {
            _eventStream.Publish(@event);
        }

        private void ClearState()
        {
            _latestGossip = Gossip.Empty;
        }
    }
}
