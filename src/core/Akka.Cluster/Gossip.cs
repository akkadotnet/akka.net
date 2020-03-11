//-----------------------------------------------------------------------
// <copyright file="Gossip.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Remote;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    /// <summary>
    /// Represents the state of the cluster; cluster ring membership, ring convergence -
    /// all versioned by a vector clock.
    ///
    /// When a node is joining the `Member`, with status `Joining`, is added to `members`.
    /// If the joining node was downed it is moved from `overview.unreachable` (status `Down`)
    /// to `members` (status `Joining`). It cannot rejoin if not first downed.
    ///
    /// When convergence is reached the leader change status of `members` from `Joining`
    /// to `Up`.
    ///
    /// When failure detector consider a node as unavailable it will be moved from
    /// `members` to `overview.unreachable`.
    ///
    /// When a node is downed, either manually or automatically, its status is changed to `Down`.
    /// It is also removed from `overview.seen` table. The node will reside as `Down` in the
    /// `overview.unreachable` set until joining again and it will then go through the normal
    /// joining procedure.
    ///
    /// When a `Gossip` is received the version (vector clock) is used to determine if the
    /// received `Gossip` is newer or older than the current local `Gossip`. The received `Gossip`
    /// and local `Gossip` is merged in case of conflicting version, i.e. vector clocks without
    /// same history.
    ///
    /// When a node is told by the user to leave the cluster the leader will move it to `Leaving`
    /// and then rebalance and repartition the cluster and start hand-off by migrating the actors
    /// from the leaving node to the new partitions. Once this process is complete the leader will
    /// move the node to the `Exiting` state and once a convergence is complete move the node to
    /// `Removed` by removing it from the `members` set and sending a `Removed` command to the
    /// removed node telling it to shut itself down.
    /// </summary>
    internal sealed class Gossip
    {
        /// <summary>
        /// An empty set of members
        /// </summary>
        public static readonly ImmutableSortedSet<Member> EmptyMembers = ImmutableSortedSet.Create<Member>();

        /// <summary>
        /// An empty <see cref="Gossip"/> object.
        /// </summary>
        public static readonly Gossip Empty = new Gossip(EmptyMembers);

        /// <summary>
        /// Creates a new <see cref="Gossip"/> from the given set of members.
        /// </summary>
        /// <param name="members">The current membership of the cluster.</param>
        /// <returns>A gossip object for the given members.</returns>
        public static Gossip Create(ImmutableSortedSet<Member> members)
        {
            if (members.IsEmpty) return Empty;
            return Empty.Copy(members: members);
        }

        private static readonly ImmutableHashSet<MemberStatus> LeaderMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving);

        private static readonly ImmutableHashSet<MemberStatus> ConvergenceMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving);

        /// <summary>
        /// If there are unreachable members in the cluster with any of these statuses, they will be skipped during convergence checks.
        /// </summary>
        public static readonly ImmutableHashSet<MemberStatus> ConvergenceSkipUnreachableWithMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Down, MemberStatus.Exiting);

        /// <summary>
        /// If there are unreachable members in the cluster with any of these statuses, they will be pruned from the local gossip
        /// </summary>
        public static readonly ImmutableHashSet<MemberStatus> RemoveUnreachableWithMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Down, MemberStatus.Exiting);

        readonly ImmutableSortedSet<Member> _members;
        readonly GossipOverview _overview;
        readonly VectorClock _version;

        /// <summary>
        /// The current members of the cluster
        /// </summary>
        public ImmutableSortedSet<Member> Members { get { return _members; } }
        /// <summary>
        /// TBD
        /// </summary>
        public GossipOverview Overview { get { return _overview; } }
        /// <summary>
        /// TBD
        /// </summary>
        public VectorClock Version { get { return _version; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="members">TBD</param>
        public Gossip(ImmutableSortedSet<Member> members) : this(members, new GossipOverview(), VectorClock.Create()) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="members">TBD</param>
        /// <param name="overview">TBD</param>
        public Gossip(ImmutableSortedSet<Member> members, GossipOverview overview) : this(members, overview, VectorClock.Create()) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="members">TBD</param>
        /// <param name="overview">TBD</param>
        /// <param name="version">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        public Gossip(ImmutableSortedSet<Member> members, GossipOverview overview, VectorClock version)
        {
            _members = members;
            _overview = overview;
            _version = version;

            _membersMap = new Lazy<ImmutableDictionary<UniqueAddress, Member>>(
                () => members.ToImmutableDictionary(m => m.UniqueAddress, m => m));

            ReachabilityExcludingDownedObservers = new Lazy<Reachability>(() =>
            {
                var downed = Members.Where(m => m.Status == MemberStatus.Down).ToList();
                return Overview.Reachability.RemoveObservers(downed.Select(m => m.UniqueAddress).ToImmutableHashSet());
            });

            if (Cluster.IsAssertInvariantsEnabled) AssertInvariants();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="members">TBD</param>
        /// <param name="overview">TBD</param>
        /// <param name="version">TBD</param>
        /// <returns>TBD</returns>
        public Gossip Copy(ImmutableSortedSet<Member> members = null, GossipOverview overview = null,
            VectorClock version = null)
        {
            return new Gossip(members ?? _members, overview ?? _overview, version ?? _version);
        }

        private void AssertInvariants()
        {
            if (_members.Any(m => m.Status == MemberStatus.Removed))
            {
                var members = string.Join(", ", _members.Where(m => m.Status == MemberStatus.Removed).Select(m => m.ToString()));
                throw new ArgumentException($"Live members must not have status [Removed], got {members}", nameof(_members));
            }

            var inReachabilityButNotMember = _overview.Reachability.AllObservers.Except(_members.Select(m => m.UniqueAddress));
            if (!inReachabilityButNotMember.IsEmpty)
            {
                var inreachability = string.Join(", ", inReachabilityButNotMember.Select(a => a.ToString()));
                throw new ArgumentException($"Nodes not part of cluster in reachability table, got {inreachability}", nameof(_overview));
            }

            var seenButNotMember = _overview.Seen.Except(_members.Select(m => m.UniqueAddress));
            if (!seenButNotMember.IsEmpty)
            {
                var seen = string.Join(", ", seenButNotMember.Select(a => a.ToString()));
                throw new ArgumentException($"Nodes not part of cluster have marked the Gossip as seen, got {seen}", nameof(_overview));
            }
        }

        //TODO: Serializer should ignore
        Lazy<ImmutableDictionary<UniqueAddress, Member>> _membersMap;

        /// <summary>
        /// Increments the version for this 'Node'.
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public Gossip Increment(VectorClock.Node node)
        {
            return Copy(version: _version.Increment(node));
        }

        /// <summary>
        /// Adds a member to the member node ring.
        /// </summary>
        /// <param name="member">TBD</param>
        /// <returns>TBD</returns>
        public Gossip AddMember(Member member)
        {
            if (_members.Contains(member)) return this;
            return Copy(members: _members.Add(member));
        }

        /// <summary>
        /// Marks the gossip as seen by this node (address) by updating the address entry in the 'gossip.overview.seen'
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public Gossip Seen(UniqueAddress node)
        {
            if (SeenByNode(node)) return this;
            return Copy(overview: _overview.Copy(seen: _overview.Seen.Add(node)));
        }

        /// <summary>
        /// Marks the gossip as seen by only this node (address) by replacing the 'gossip.overview.seen'
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public Gossip OnlySeen(UniqueAddress node)
        {
            return Copy(overview: _overview.Copy(seen: ImmutableHashSet.Create(node)));
        }

        /// <summary>
        /// Removes all seen entries from the gossip.
        /// </summary>
        /// <returns>A copy of the current gossip with no seen entries.</returns>
        public Gossip ClearSeen()
        {
            return Copy(overview: Overview.Copy(seen: ImmutableHashSet<UniqueAddress>.Empty));
        }

        /// <summary>
        /// The nodes that have seen the current version of the Gossip.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> SeenBy
        {
            get { return _overview.Seen; }
        }

        /// <summary>
        /// Has this Gossip been seen by this node.
        /// </summary>
        /// <param name="node">The unique address of the node.</param>
        /// <returns><c>true</c> if this gossip has been seen by the given node, <c>false</c> otherwise.</returns>
        public bool SeenByNode(UniqueAddress node)
        {
            return _overview.Seen.Contains(node);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public Gossip MergeSeen(Gossip that)
        {
            return Copy(overview: _overview.Copy(seen: _overview.Seen.Union(that._overview.Seen)));
        }

        /// <summary>
        /// Merges two <see cref="Gossip"/> objects together into a consistent view of the <see cref="Cluster"/>.
        /// </summary>
        /// <param name="that">The other gossip object to be merged.</param>
        /// <returns>A combined gossip object that uses the underlying <see cref="VectorClock"/> to determine which items are newest.</returns>
        public Gossip Merge(Gossip that)
        {
            //TODO: Member ordering import?
            // 1. merge vector clocks
            var mergedVClock = _version.Merge(that._version);

            // 2. merge members by selecting the single Member with highest MemberStatus out of the Member groups
            var mergedMembers = EmptyMembers.Union(Member.PickHighestPriority(this._members, that._members));

            // 3. merge reachability table by picking records with highest version
            var mergedReachability = this._overview.Reachability.Merge(mergedMembers.Select(m => m.UniqueAddress),
                that._overview.Reachability);

            // 4. Nobody can have seen this new gossip yet
            var mergedSeen = ImmutableHashSet.Create<UniqueAddress>();

            return new Gossip(mergedMembers, new GossipOverview(mergedSeen, mergedReachability), mergedVClock);
        }


        /// <summary>
        /// First check that:
        ///   1. we don't have any members that are unreachable, or
        ///   2. all unreachable members in the set have status DOWN or EXITING
        /// Else we can't continue to check for convergence. When that is done 
        /// we check that all members with a convergence status is in the seen 
        /// table and has the latest vector clock version.
        /// </summary>
        /// <param name="selfUniqueAddress">The unique address of the node checking for convergence.</param>
        /// <param name="exitingConfirmed">The set of nodes who have been confirmed to be exiting.</param>
        /// <returns><c>true</c> if convergence has been achieved. <c>false</c> otherwise.</returns>
        public bool Convergence(UniqueAddress selfUniqueAddress, HashSet<UniqueAddress> exitingConfirmed)
        {
            var unreachable = ReachabilityExcludingDownedObservers.Value.AllUnreachableOrTerminated
                .Where(node => node != selfUniqueAddress && !exitingConfirmed.Contains(node))
                .Select(GetMember);

            return unreachable.All(m => ConvergenceSkipUnreachableWithMemberStatus.Contains(m.Status))
                && !_members.Any(m => ConvergenceMemberStatus.Contains(m.Status) 
                && !(SeenByNode(m.UniqueAddress) || exitingConfirmed.Contains(m.UniqueAddress)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Lazy<Reachability> ReachabilityExcludingDownedObservers { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        public bool IsLeader(UniqueAddress node, UniqueAddress selfUniqueAddress)
        {
            return Leader(selfUniqueAddress) == node && node != null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        public UniqueAddress Leader(UniqueAddress selfUniqueAddress)
        {
            return LeaderOf(_members, selfUniqueAddress);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="role">TBD</param>
        /// <param name="selfUniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        public UniqueAddress RoleLeader(string role, UniqueAddress selfUniqueAddress)
        {
            var roleMembers = _members
                .Where(m => m.HasRole(role))
                .ToImmutableSortedSet();

            return LeaderOf(roleMembers, selfUniqueAddress);
        }

        /// <summary>
        /// Determine which node is the leader of the given range of members.
        /// </summary>
        /// <param name="mbrs">All members in the cluster.</param>
        /// <param name="selfUniqueAddress">The address of the current node.</param>
        /// <returns><c>null</c> if <paramref name="mbrs"/> is empty. The <see cref="UniqueAddress"/> of the leader otherwise.</returns>
        public UniqueAddress LeaderOf(ImmutableSortedSet<Member> mbrs, UniqueAddress selfUniqueAddress)
        {
            var reachableMembers = (_overview.Reachability.IsAllReachable
                ? mbrs.Where(m => m.Status != MemberStatus.Down)
                : mbrs
                    .Where(m => m.Status != MemberStatus.Down && _overview.Reachability.IsReachable(m.UniqueAddress) || m.UniqueAddress == selfUniqueAddress))
                    .ToImmutableSortedSet();

            if (!reachableMembers.Any()) return null;

            var member = reachableMembers.FirstOrDefault(m => LeaderMemberStatus.Contains(m.Status)) ??
                         reachableMembers.Min(Member.LeaderStatusOrdering);

            return member.UniqueAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<string> AllRoles
        {
            get { return _members.SelectMany(m => m.Roles).ToImmutableHashSet(); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsSingletonCluster
        {
            get { return _members.Count == 1; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public Member GetMember(UniqueAddress node)
        {
            return _membersMap.Value.GetOrElse(node,
                Member.Removed(node)); // placeholder for removed member
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public bool HasMember(UniqueAddress node)
        {
            return _membersMap.Value.ContainsKey(node);
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="Exception">
        /// This exception is thrown when there are no members in the cluster.
        /// </exception>
        public Member YoungestMember
        {
            get
            {
                //TODO: Akka exception?
                if (!_members.Any()) throw new Exception("No youngest when no members");
                return _members.MaxBy(m => m.UpNumber == int.MaxValue ? 0 : m.UpNumber);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public Gossip Prune(VectorClock.Node removedNode)
        {
            var newVersion = Version.Prune(removedNode);
            if (newVersion.Equals(Version))
                return this;
            else
                return new Gossip(Members, Overview, newVersion);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var members = string.Join(", ", _members.Select(m => m.ToString()));
            return $"Gossip(members = [{members}], overview = {_overview}, version = {_version}";
        }
    }

    /// <summary>
    /// Represents the overview of the cluster, holds the cluster convergence table and set with unreachable nodes.
    /// </summary>
    class GossipOverview
    {
        readonly ImmutableHashSet<UniqueAddress> _seen;
        readonly Reachability _reachability;

        /// <summary>
        /// TBD
        /// </summary>
        public GossipOverview() : this(ImmutableHashSet.Create<UniqueAddress>(), Reachability.Empty) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reachability">TBD</param>
        public GossipOverview(Reachability reachability) : this(ImmutableHashSet.Create<UniqueAddress>(), reachability) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="seen">TBD</param>
        /// <param name="reachability">TBD</param>
        public GossipOverview(ImmutableHashSet<UniqueAddress> seen, Reachability reachability)
        {
            _seen = seen;
            _reachability = reachability;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="seen">TBD</param>
        /// <param name="reachability">TBD</param>
        /// <returns>TBD</returns>
        public GossipOverview Copy(ImmutableHashSet<UniqueAddress> seen = null, Reachability reachability = null)
        {
            return new GossipOverview(seen ?? _seen, reachability ?? _reachability);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Seen { get { return _seen; } }
        /// <summary>
        /// TBD
        /// </summary>
        public Reachability Reachability { get { return _reachability; } }
        
        /// <inheritdoc/>
        public override string ToString() => $"GossipOverview(seen=[{string.Join(", ", Seen)}], reachability={Reachability})";
    }

    /// <summary>
    /// Envelope adding a sender and receiver address to the gossip.
    /// The reason for including the receiver address is to be able to
    /// ignore messages that were intended for a previous incarnation of
    /// the node with same host:port. The `uid` in the `UniqueAddress` is
    /// different in that case.
    /// </summary>
    class GossipEnvelope : IClusterMessage
    {
        //TODO: Serialization?
        //TODO: ser stuff?

        readonly UniqueAddress _from;
        readonly UniqueAddress _to;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <param name="gossip">TBD</param>
        /// <param name="deadline">TBD</param>
        /// <returns>TBD</returns>
        public GossipEnvelope(UniqueAddress from, UniqueAddress to, Gossip gossip, Deadline deadline = null)
        {
            _from = from;
            _to = to;
            Gossip = gossip;
            Deadline = deadline;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress From { get { return _from; } }
        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress To { get { return _to; } }
        /// <summary>
        /// TBD
        /// </summary>
        public Gossip Gossip { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Deadline Deadline { get; set; }
    }

    /// <summary>
    /// When there are no known changes to the node ring a `GossipStatus`
    /// initiates a gossip chat between two members. If the receiver has a newer
    /// version it replies with a `GossipEnvelope`. If receiver has older version
    /// it replies with its `GossipStatus`. Same versions ends the chat immediately.
    /// </summary>
    class GossipStatus : IClusterMessage
    {
        readonly UniqueAddress _from;
        readonly VectorClock _version;

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress From { get { return _from; } }
        /// <summary>
        /// TBD
        /// </summary>
        public VectorClock Version { get { return _version; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="version">TBD</param>
        public GossipStatus(UniqueAddress from, VectorClock version)
        {
            _from = from;
            _version = version;
        }

        /// <inheritdoc/>
        protected bool Equals(GossipStatus other)
        {
            return _from.Equals(other._from) && _version.IsSameAs(other._version);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((GossipStatus)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (_from.GetHashCode() * 397) ^ _version.GetHashCode();
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"GossipStatus(from={From}, version={Version})";
    }
}
