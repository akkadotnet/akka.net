//-----------------------------------------------------------------------
// <copyright file="Gossip.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// The current members of the cluster
        /// </summary>
        public ImmutableSortedSet<Member> Members { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public GossipOverview Overview { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public VectorClock Version { get; }

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
            Members = members;
            Overview = overview;
            Version = version;

            _membersMap = new Lazy<ImmutableDictionary<UniqueAddress, Member>>(
                () => members.ToImmutableDictionary(m => m.UniqueAddress, m => m));

            ReachabilityExcludingDownedObservers = new Lazy<Reachability>(() =>
            {
                var downed = Members.Where(m => m.Status == MemberStatus.Down);
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
            return new Gossip(members ?? Members, overview ?? Overview, version ?? Version);
        }

        private void AssertInvariants()
        {
            void IfTrueThrow(bool func, string expected, string actual)
            {
                if (func) throw new ArgumentException($"{expected}, but found [{actual}]");
            }


            IfTrueThrow(Members.Any(m => m.Status == MemberStatus.Removed),
                expected: "Live members must not have status [Removed]",
                actual: string.Join(", ",
                    Members.Where(m => m.Status == MemberStatus.Removed).Select(m => m.ToString())));


            var inReachabilityButNotMember = Overview.Reachability.AllObservers.Except(Members.Select(m => m.UniqueAddress));
            IfTrueThrow(!inReachabilityButNotMember.IsEmpty,
                expected: "Nodes not part of cluster in reachability table",
                actual: string.Join(", ", inReachabilityButNotMember.Select(a => a.ToString())));

            var inReachabilityVersionsButNotMember =
                Overview.Reachability.Versions.Keys.Except(Members.Select(x => x.UniqueAddress)).ToImmutableHashSet();
            IfTrueThrow(!inReachabilityVersionsButNotMember.IsEmpty,
                expected: "Nodes not part of cluster in reachability versions table",
                actual: string.Join(", ", inReachabilityVersionsButNotMember.Select(a => a.ToString())));

            var seenButNotMember = Overview.Seen.Except(Members.Select(m => m.UniqueAddress));
            IfTrueThrow(!seenButNotMember.IsEmpty,
                expected: "Nodes not part of cluster have marked the Gossip as seen",
                actual: string.Join(", ", seenButNotMember.Select(a => a.ToString())));
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
            return Copy(version: Version.Increment(node));
        }

        /// <summary>
        /// Adds a member to the member node ring.
        /// </summary>
        /// <param name="member">TBD</param>
        /// <returns>TBD</returns>
        public Gossip AddMember(Member member)
        {
            if (Members.Contains(member)) return this;
            return Copy(members: Members.Add(member));
        }

        /// <summary>
        /// Marks the gossip as seen by this node (address) by updating the address entry in the 'gossip.overview.seen'
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public Gossip Seen(UniqueAddress node)
        {
            if (SeenByNode(node)) return this;
            return Copy(overview: Overview.Copy(seen: Overview.Seen.Add(node)));
        }

        /// <summary>
        /// Marks the gossip as seen by only this node (address) by replacing the 'gossip.overview.seen'
        /// </summary>
        /// <param name="node">TBD</param>
        /// <returns>TBD</returns>
        public Gossip OnlySeen(UniqueAddress node)
        {
            return Copy(overview: Overview.Copy(seen: ImmutableHashSet.Create(node)));
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
            get { return Overview.Seen; }
        }

        /// <summary>
        /// Has this Gossip been seen by this node.
        /// </summary>
        /// <param name="node">The unique address of the node.</param>
        /// <returns><c>true</c> if this gossip has been seen by the given node, <c>false</c> otherwise.</returns>
        public bool SeenByNode(UniqueAddress node)
        {
            return Overview.Seen.Contains(node);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="that">TBD</param>
        /// <returns>TBD</returns>
        public Gossip MergeSeen(Gossip that)
        {
            return Copy(overview: Overview.Copy(seen: Overview.Seen.Union(that.Overview.Seen)));
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
            var mergedVClock = Version.Merge(that.Version);

            // 2. merge members by selecting the single Member with highest MemberStatus out of the Member groups
            var mergedMembers = EmptyMembers.Union(Member.PickHighestPriority(this.Members, that.Members));

            // 3. merge reachability table by picking records with highest version
            var mergedReachability = Overview.Reachability.Merge(mergedMembers.Select(m => m.UniqueAddress).ToImmutableSortedSet(),
                that.Overview.Reachability);

            // 4. Nobody can have seen this new gossip yet
            var mergedSeen = ImmutableHashSet.Create<UniqueAddress>();

            return new Gossip(mergedMembers, new GossipOverview(mergedSeen, mergedReachability), mergedVClock);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Lazy<Reachability> ReachabilityExcludingDownedObservers { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<string> AllRoles
        {
            get { return Members.SelectMany(m => m.Roles).ToImmutableHashSet(); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsSingletonCluster
        {
            get { return Members.Count == 1; }
        }

        /// <summary>
        /// Returns `true` if <paramref name="fromAddress"/> should be able to reach <paramref name="toAddress"/> 
        /// based on the unreachability data.
        /// </summary>
        /// <param name="fromAddress">TBD</param>
        /// <param name="toAddress">TBD</param>
        public bool IsReachable(UniqueAddress fromAddress, UniqueAddress toAddress)
        {
            if (!HasMember(toAddress)) 
                return false;

            // as it looks for specific unreachable entires for the node pair we don't have to filter on team
            return Overview.Reachability.IsReachable(fromAddress, toAddress);
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
                if (!Members.Any()) throw new Exception("No youngest when no members");
                return Members.MaxBy(m => m.UpNumber == int.MaxValue ? 0 : m.UpNumber);
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

        public Gossip MarkAsDown(Member member)
        {
            // replace member (changed status)
            var newMembers = Members.Remove(member).Add(member.Copy(MemberStatus.Down));
            // remove nodes marked as DOWN from the 'seen' table
            var newSeen = Overview.Seen.Remove(member.UniqueAddress);

            //update gossip overview
            var newOverview = Overview.Copy(seen: newSeen);
            return Copy(newMembers, overview: newOverview);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var members = string.Join(", ", Members.Select(m => m.ToString()));
            return $"Gossip(members = [{members}], overview = {Overview}, version = {Version}";
        }
    }

    /// <summary>
    /// Represents the overview of the cluster, holds the cluster convergence table and set with unreachable nodes.
    /// </summary>
    internal class GossipOverview
    {
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
            Seen = seen;
            Reachability = reachability;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="seen">TBD</param>
        /// <param name="reachability">TBD</param>
        /// <returns>TBD</returns>
        public GossipOverview Copy(ImmutableHashSet<UniqueAddress> seen = null, Reachability reachability = null)
        {
            return new GossipOverview(seen ?? Seen, reachability ?? Reachability);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Seen { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Reachability Reachability { get; }

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
            From = from;
            To = to;
            Gossip = gossip;
            Deadline = deadline;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress From { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress To { get; }

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
        /// <summary>
        /// TBD
        /// </summary>
        public UniqueAddress From { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public VectorClock Version { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="version">TBD</param>
        public GossipStatus(UniqueAddress from, VectorClock version)
        {
            From = from;
            Version = version;
        }

        /// <inheritdoc/>
        protected bool Equals(GossipStatus other)
        {
            return From.Equals(other.From) && Version.IsSameAs(other.Version);
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
                return (From.GetHashCode() * 397) ^ Version.GetHashCode();
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"GossipStatus(from={From}, version={Version})";
    }
}
