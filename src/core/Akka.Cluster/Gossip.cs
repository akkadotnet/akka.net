//-----------------------------------------------------------------------
// <copyright file="Gossip.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// An empty set of tombstones. Declared before <see cref="Empty"/> because the constructor
        /// chain reads it.
        /// </summary>
        public static readonly ImmutableDictionary<UniqueAddress, long> EmptyTombstones =
            ImmutableDictionary<UniqueAddress, long>.Empty;

        /// <summary>
        /// An empty <see cref="Gossip"/> object.
        /// </summary>
        public static readonly Gossip Empty = new(EmptyMembers);

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

        readonly ImmutableSortedSet<Member> _members;
        readonly GossipOverview _overview;
        readonly VectorClock _version;
        readonly ImmutableDictionary<UniqueAddress, long> _tombstones;

        /// <summary>
        /// The current members of the cluster
        /// </summary>
        public ImmutableSortedSet<Member> Members { get { return _members; } }
        /// <summary>
        /// The seen table and the reachability table for the current members.
        /// </summary>
        public GossipOverview Overview { get { return _overview; } }
        /// <summary>
        /// The vector clock that orders this gossip against gossip from other nodes. Every node that
        /// changes the cluster state bumps its own entry, so two clocks can be compared to tell which
        /// gossip is newer - or that the two are concurrent and have to be merged.
        /// </summary>
        public VectorClock Version { get { return _version; } }

        /// <summary>
        /// The nodes this cluster has removed, keyed by <see cref="UniqueAddress"/> and stamped with the
        /// epoch milliseconds of the removal.
        ///
        /// A member status on its own cannot tell "this member was removed" apart from "this node has
        /// not heard about it yet", so merging two gossips has no way to decide what to do with a member
        /// that only one side holds. A tombstone answers that directly: it is positive evidence that the
        /// removal happened, and it travels in the gossip so every peer can apply it.
        ///
        /// The key carries the node UID, so a restarted node at the same host and port is never blocked
        /// by the tombstone of its predecessor.
        ///
        /// The timestamp orders nothing. It exists only so the leader can drop tombstones older than
        /// <c>akka.cluster.prune-gossip-tombstones-after</c>.
        /// </summary>
        public ImmutableDictionary<UniqueAddress, long> Tombstones { get { return _tombstones; } }

        /// <summary>
        /// Creates a gossip for <paramref name="members"/> with an empty overview and a fresh vector clock.
        /// </summary>
        /// <inheritdoc cref="Gossip(ImmutableSortedSet{Member}, GossipOverview, VectorClock, ImmutableDictionary{UniqueAddress, long})"/>
        public Gossip(ImmutableSortedSet<Member> members) : this(members, new GossipOverview(), VectorClock.Create()) { }

        /// <summary>
        /// Creates a gossip for <paramref name="members"/> and <paramref name="overview"/> with a fresh vector clock.
        /// </summary>
        /// <inheritdoc cref="Gossip(ImmutableSortedSet{Member}, GossipOverview, VectorClock, ImmutableDictionary{UniqueAddress, long})"/>
        public Gossip(ImmutableSortedSet<Member> members, GossipOverview overview) : this(members, overview, VectorClock.Create()) { }

        /// <summary>
        /// Creates the cluster state carried by a single gossip message.
        /// </summary>
        /// <param name="members">The members of the cluster, sorted by address.</param>
        /// <param name="overview">The seen table and the reachability table for those members.</param>
        /// <param name="version">The vector clock that orders this gossip against gossip from other nodes.</param>
        /// <param name="tombstones">The nodes this cluster has removed. See <see cref="Tombstones"/>. Defaults to none.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when the arguments break an invariant - a member with status <see cref="MemberStatus.Removed"/>,
        /// a seen or reachability entry for a node that is not a member, or a node that is both a member and a
        /// tombstone. Only checked when <see cref="Cluster.IsAssertInvariantsEnabled"/> is set.
        /// </exception>
        public Gossip(ImmutableSortedSet<Member> members, GossipOverview overview, VectorClock version,
            ImmutableDictionary<UniqueAddress, long> tombstones = null)
        {
            _members = members;
            _overview = overview;
            _version = version;
            _tombstones = tombstones ?? EmptyTombstones;

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
        /// Creates a gossip that matches this one except for the arguments that are supplied.
        /// </summary>
        /// <param name="members">The new members, or <c>null</c> to keep the current ones.</param>
        /// <param name="overview">The new overview, or <c>null</c> to keep the current one.</param>
        /// <param name="version">The new vector clock, or <c>null</c> to keep the current one.</param>
        /// <param name="tombstones">The new tombstones, or <c>null</c> to keep the current ones. See <see cref="Tombstones"/>.</param>
        /// <returns>A new gossip. This method always allocates, even when nothing changed.</returns>
        public Gossip Copy(ImmutableSortedSet<Member> members = null, GossipOverview overview = null,
            VectorClock version = null, ImmutableDictionary<UniqueAddress, long> tombstones = null)
        {
            return new Gossip(members ?? _members, overview ?? _overview, version ?? _version,
                tombstones ?? _tombstones);
        }

        private void AssertInvariants()
        {
            IfTrueThrow(_members.Any(m => m.Status == MemberStatus.Removed),
                expected: "Live members must not have status [Removed]",
                actual: string.Join(", ",
                    _members.Where(m => m.Status == MemberStatus.Removed).Select(m => m.ToString())));


            var inReachabilityButNotMember = _overview.Reachability.AllObservers.Except(_members.Select(m => m.UniqueAddress));
            IfTrueThrow(!inReachabilityButNotMember.IsEmpty,
                expected: "Nodes not part of cluster in reachability table",
                actual: string.Join(", ", inReachabilityButNotMember.Select(a => a.ToString())));

            var inReachabilityVersionsButNotMember =
                _overview.Reachability.Versions.Keys.Except(Members.Select(x => x.UniqueAddress)).ToImmutableHashSet();
            IfTrueThrow(!inReachabilityVersionsButNotMember.IsEmpty,
                expected: "Nodes not part of cluster in reachability versions table",
                actual: string.Join(", ", inReachabilityVersionsButNotMember.Select(a => a.ToString())));

            var seenButNotMember = _overview.Seen.Except(_members.Select(m => m.UniqueAddress));
            IfTrueThrow(!seenButNotMember.IsEmpty,
                expected: "Nodes not part of cluster have marked the Gossip as seen",
                actual: string.Join(", ", seenButNotMember.Select(a => a.ToString())));

            // Members and tombstones must stay disjoint. Merge depends on it: a member that appears on
            // both sides is picked by status alone, without consulting tombstones, which is only correct
            // while no gossip carries a member and a tombstone for the same node.
            var tombstonedButStillMember = _members.Where(m => _tombstones.ContainsKey(m.UniqueAddress)).ToList();
            IfTrueThrow(tombstonedButStillMember.Count > 0,
                expected: "Removed nodes must not be members",
                actual: string.Join(", ", tombstonedButStillMember.Select(m => m.ToString())));
            return;

            void IfTrueThrow(bool func, string expected, string actual)
            {
                if (func) throw new ArgumentException($"{expected}, but found [{actual}]");
            }
        }

        //TODO: Serializer should ignore
        Lazy<ImmutableDictionary<UniqueAddress, Member>> _membersMap;

        /// <summary>
        /// Bumps this node's entry in the vector clock, marking the gossip as changed here. Callers do
        /// this whenever they alter the cluster state so peers can tell the new gossip descends from the old.
        /// </summary>
        /// <param name="node">The vector clock entry to bump, normally the caller's own.</param>
        /// <returns>A copy of this gossip with the bumped clock.</returns>
        public Gossip Increment(VectorClock.Node node)
        {
            return Copy(version: _version.Increment(node));
        }

        /// <summary>
        /// Adds a member to the member node ring.
        /// </summary>
        /// <param name="member">The member to add.</param>
        /// <returns>This gossip when the ring already holds that node. Membership is keyed by address, so
        /// this will not replace an existing member that differs only in status - use <see cref="Copy"/> for that.</returns>
        public Gossip AddMember(Member member)
        {
            if (_members.Contains(member)) return this;
            return Copy(members: _members.Add(member));
        }

        /// <summary>
        /// Marks the gossip as seen by this node (address) by updating the address entry in the 'gossip.overview.seen'
        /// </summary>
        /// <param name="node">The node that has seen this version of the gossip.</param>
        /// <returns>This gossip when the node had already seen it.</returns>
        public Gossip Seen(UniqueAddress node)
        {
            if (SeenByNode(node)) return this;
            return Copy(overview: _overview.Copy(seen: _overview.Seen.Add(node)));
        }

        /// <summary>
        /// Marks the gossip as seen by only this node (address) by replacing the 'gossip.overview.seen'
        /// </summary>
        /// <param name="node">The only node that has seen this version of the gossip.</param>
        /// <returns>A copy of this gossip whose seen table holds <paramref name="node"/> alone.</returns>
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
        /// Merges the seen table from two <see cref="Gossip"/> instances.
        /// The resulting seen set is filtered to only include addresses that are current members,
        /// ensuring the invariant that Seen ⊆ Members is always maintained.
        /// </summary>
        /// <param name="that">The other gossip instance to merge seen state from.</param>
        /// <returns>A new gossip instance with merged and filtered seen state.</returns>
        public Gossip MergeSeen(Gossip that)
        {
            var memberAddresses = _members.Select(m => m.UniqueAddress).ToImmutableHashSet();
            var mergedSeen = _overview.Seen.Union(that._overview.Seen).Intersect(memberAddresses);
            return Copy(overview: _overview.Copy(seen: mergedSeen));
        }

        /// <summary>
        /// Merges two <see cref="Gossip"/> objects together into a consistent view of the <see cref="Cluster"/>.
        /// </summary>
        /// <param name="that">The other gossip object to be merged.</param>
        /// <returns>A combined gossip object that uses the underlying <see cref="VectorClock"/> to determine which items are newest.</returns>
        public Gossip Merge(Gossip that)
        {
            //TODO: Member ordering import?
            // 1. merge tombstones - steps 2 and 3 both read the result
            var mergedTombstones = UnionTombstones(_tombstones, that._tombstones);
            var tombstonedNodes = mergedTombstones.Count == 0
                ? ImmutableHashSet<UniqueAddress>.Empty
                : mergedTombstones.Keys.ToImmutableHashSet();

            // 2. merge vector clocks, then drop the entry of every removed node.
            //    A clock entry is resurrected by the union the same way a member is, so filtering members
            //    alone would leave the removed node in the clock forever.
            var mergedVClock = tombstonedNodes.Aggregate(
                _version.Merge(that._version),
                (clock, node) => clock.Prune(VectorClock.Node.Create(ClusterCoreDaemon.VclockName(node))));

            // 3. merge members by selecting the single Member with highest MemberStatus out of the Member groups
            var mergedMembers = EmptyMembers.Union(Member.PickHighestPriority(this._members, that._members, tombstonedNodes));

            // 4. merge reachability table by picking records with highest version
            var mergedReachability = _overview.Reachability.Merge(mergedMembers.Select(m => m.UniqueAddress).ToImmutableSortedSet(),
                that._overview.Reachability);

            // 5. Nobody can have seen this new gossip yet
            var mergedSeen = ImmutableHashSet.Create<UniqueAddress>();

            return new Gossip(mergedMembers, new GossipOverview(mergedSeen, mergedReachability), mergedVClock,
                mergedTombstones);
        }

        /// <summary>
        /// Adds the tombstones of <paramref name="that"/> to this gossip, keeping the later timestamp
        /// when both sides hold the same node.
        ///
        /// <see cref="Merge"/> takes the same union on the concurrent branch of gossip reception. This
        /// method covers the branch where the two clocks are equal, where neither gossip descends from
        /// the other and each may hold a removal the other has not heard about. The branches that pick a
        /// strictly newer gossip keep the winner's tombstones alone - see
        /// <c>ClusterCoreDaemon.ReceiveGossip</c> for why the winner cannot be behind on removals.
        /// </summary>
        /// <param name="that">The gossip to take tombstones from.</param>
        /// <returns>This gossip when it already covers every tombstone of <paramref name="that"/>.</returns>
        public Gossip MergeTombstones(Gossip that)
        {
            if (that._tombstones.Count == 0) return this;

            // A node this gossip still holds as a member has not been removed here, so its tombstone is
            // not adopted: members and tombstones must stay disjoint, and Merge picks a member both sides
            // hold by status alone, without consulting tombstones.
            //
            // That is a real drop, not a formality - the caller loses a removal it was told about. It is
            // safe because this only runs with two equal clocks: a removal bumps the removing node's clock
            // entry, so both sides descend from every removal either of them knows about, and a member
            // that is back after a removal is one whose tombstone has already been pruned. Adopting that
            // tombstone again would undo the prune.
            var merged = UnionTombstones(_tombstones, that._tombstones.Where(kv => !HasMember(kv.Key)));
            if (ReferenceEquals(merged, _tombstones)) return this;
            return Copy(tombstones: merged);
        }

        /// <summary>
        /// Drops every tombstone stamped at or before <paramref name="removeEarlierThan"/>.
        /// </summary>
        /// <param name="removeEarlierThan">Epoch milliseconds. Tombstones at or before this are dropped.</param>
        /// <returns>This gossip when nothing was dropped. The caller compares by reference to decide
        /// whether the gossip needs to be published, so this identity matters.</returns>
        public Gossip PruneTombstones(long removeEarlierThan)
        {
            if (_tombstones.Count == 0) return this;

            var pruned = _tombstones.Where(kv => kv.Value <= removeEarlierThan).Select(kv => kv.Key).ToList();
            if (pruned.Count == 0) return this;

            return Copy(tombstones: _tombstones.RemoveRange(pruned));
        }

        /// <summary>
        /// Removes nodes from the cluster and records a tombstone for each of them.
        ///
        /// Members, the seen table, the reachability table and the vector clock are all stripped in one
        /// step, so no intermediate gossip that breaks the invariants is ever built.
        /// </summary>
        /// <param name="nodes">The nodes being removed.</param>
        /// <param name="removalTimestamp">Epoch milliseconds of the removal.</param>
        /// <returns>This gossip when <paramref name="nodes"/> is empty.</returns>
        public Gossip RemoveAll(IImmutableSet<UniqueAddress> nodes, long removalTimestamp)
        {
            if (nodes.Count == 0) return this;

            var newMembers = _members.Where(m => !nodes.Contains(m.UniqueAddress)).ToImmutableSortedSet();
            var newOverview = _overview.Copy(
                seen: _overview.Seen.Except(nodes),
                reachability: _overview.Reachability.Remove(nodes));

            // Clear the VectorClock when a member is removed. The change made by the leader is stamped
            // and will propagate as is if there are no other changes on other nodes.
            var newVersion = nodes.Aggregate(_version,
                (clock, node) => clock.Prune(VectorClock.Node.Create(ClusterCoreDaemon.VclockName(node))));

            var newTombstones = _tombstones;
            foreach (var node in nodes)
                newTombstones = newTombstones.SetItem(node, removalTimestamp);

            return new Gossip(newMembers, newOverview, newVersion, newTombstones);
        }

        /// <summary>
        /// Unions two tombstone sets, keeping the later timestamp when both hold the same node.
        ///
        /// Two nodes merge the same pair of gossips in opposite order, so the result has to be the same
        /// either way. Taking the later timestamp is commutative and associative, and it errs towards
        /// keeping a tombstone longer, which is the safe direction.
        /// </summary>
        private static ImmutableDictionary<UniqueAddress, long> UnionTombstones(
            ImmutableDictionary<UniqueAddress, long> current,
            IEnumerable<KeyValuePair<UniqueAddress, long>> other)
        {
            var merged = current;
            foreach (var kv in other)
            {
                if (!merged.TryGetValue(kv.Key, out var existing))
                    merged = merged.Add(kv.Key, kv.Value);
                else if (kv.Value > existing)
                    merged = merged.SetItem(kv.Key, kv.Value);
            }
            return merged;
        }

        /// <summary>
        /// The reachability table with every record written by a member with status
        /// <see cref="MemberStatus.Down"/> stripped out. A downed node's own view no longer counts,
        /// so its old unreachability records must not keep another node marked unreachable forever.
        /// Computed once on first read.
        /// </summary>
        public Lazy<Reachability> ReachabilityExcludingDownedObservers { get; }

        /// <summary>
        /// Every role held by any current member, deduplicated. Drives the per-role leader table, and the
        /// serializer uses it to build the role table that member entries index into.
        /// </summary>
        public ImmutableHashSet<string> AllRoles
        {
            get { return _members.SelectMany(m => m.Roles).ToImmutableHashSet(); }
        }

        /// <summary>
        /// <c>true</c> when the cluster has exactly one member. The daemon skips gossiping and reaping
        /// unreachable members in that case - there is no peer to talk to and nobody to watch.
        /// </summary>
        public bool IsSingletonCluster
        {
            get { return _members.Count == 1; }
        }

        /// <summary>
        /// Returns `true` if <paramref name="fromAddress"/> should be able to reach <paramref name="toAddress"/> 
        /// based on the unreachability data.
        /// </summary>
        /// <param name="fromAddress">The observing node.</param>
        /// <param name="toAddress">The node being observed. A node that is not a member is never reachable.</param>
        public bool IsReachable(UniqueAddress fromAddress, UniqueAddress toAddress)
        {
            if (!HasMember(toAddress)) 
                return false;

            // as it looks for specific unreachable entires for the node pair we don't have to filter on team
            return Overview.Reachability.IsReachable(fromAddress, toAddress);
        }

        /// <summary>
        /// Looks up a member by address.
        /// </summary>
        /// <param name="node">The address to look up.</param>
        /// <returns>The member, or a placeholder with status <see cref="MemberStatus.Removed"/> when the
        /// node is not in the ring. Callers that need to tell the two apart should use <see cref="HasMember"/>.</returns>
        public Member GetMember(UniqueAddress node)
        {
            return _membersMap.Value.GetOrElse(node,
                Member.Removed(node)); // placeholder for removed member
        }

        /// <summary>
        /// Checks whether a node is in the member ring.
        /// </summary>
        /// <param name="node">The address to look for.</param>
        /// <returns><c>true</c> when the node is a member, whatever its status.</returns>
        public bool HasMember(UniqueAddress node)
        {
            return _membersMap.Value.ContainsKey(node);
        }


        /// <summary>
        /// The member that joined most recently, i.e. the one with the highest up-number. A member that
        /// has not reached <see cref="MemberStatus.Up"/> yet carries an up-number of <see cref="int.MaxValue"/>
        /// and is counted as 0 here, so it does not outrank a member that is already up. The leader reads
        /// this to pick the next up-number to hand out.
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
        /// Drops a node's entry from the vector clock, leaving members and tombstones alone.
        ///
        /// This is the clock half of what <see cref="RemoveAll"/> does. Gossip reception calls it when two
        /// concurrent gossips disagree about a removal, so the merged clock matches the one the leader
        /// produced when it removed the node.
        /// </summary>
        /// <param name="removedNode">The clock entry to drop, named after the removed node.</param>
        /// <returns>This gossip when the clock had no entry for that node.</returns>
        public Gossip Prune(VectorClock.Node removedNode)
        {
            var newVersion = Version.Prune(removedNode);
            if (newVersion.Equals(Version))
                return this;
            else
                return Copy(version: newVersion);
        }

        /// <summary>
        /// Sets a member's status to <see cref="MemberStatus.Down"/> and drops it from the seen table, so a
        /// downed node no longer counts towards convergence.
        /// </summary>
        /// <param name="member">The member to mark as down.</param>
        /// <returns>A copy of this gossip with the member replaced.</returns>
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
            var members = string.Join(", ", _members.Select(m => m.ToString()));
            var tombstones = string.Join(", ", _tombstones.Select(t => $"{t.Key} -> {t.Value}"));
            return $"Gossip(members = [{members}], overview = {_overview}, version = {_version}, tombstones = [{tombstones}])";
        }
    }

    /// <summary>
    /// Represents the overview of the cluster, holds the cluster convergence table and set with unreachable nodes.
    /// </summary>
    internal class GossipOverview
    {
        readonly ImmutableHashSet<UniqueAddress> _seen;
        readonly Reachability _reachability;

        /// <summary>
        /// Creates an overview that nobody has seen and where every node is reachable.
        /// </summary>
        /// <inheritdoc cref="GossipOverview(ImmutableHashSet{UniqueAddress}, Reachability)"/>
        public GossipOverview() : this(ImmutableHashSet.Create<UniqueAddress>(), Reachability.Empty) { }

        /// <summary>
        /// Creates an overview from a reachability table that nobody has seen yet.
        /// </summary>
        /// <inheritdoc cref="GossipOverview(ImmutableHashSet{UniqueAddress}, Reachability)"/>
        public GossipOverview(Reachability reachability) : this(ImmutableHashSet.Create<UniqueAddress>(), reachability) { }

        /// <summary>
        /// Creates an overview from a seen table and a reachability table.
        /// </summary>
        /// <param name="seen">The nodes that have seen the current version of the gossip.</param>
        /// <param name="reachability">Which observers consider which members unreachable.</param>
        public GossipOverview(ImmutableHashSet<UniqueAddress> seen, Reachability reachability)
        {
            _seen = seen;
            _reachability = reachability;
        }

        /// <summary>
        /// Creates an overview that matches this one except for the arguments that are supplied.
        /// </summary>
        /// <param name="seen">The new seen table, or <c>null</c> to keep the current one.</param>
        /// <param name="reachability">The new reachability table, or <c>null</c> to keep the current one.</param>
        /// <returns>A new overview. This method always allocates, even when nothing changed.</returns>
        public GossipOverview Copy(ImmutableHashSet<UniqueAddress> seen = null, Reachability reachability = null)
        {
            return new GossipOverview(seen ?? _seen, reachability ?? _reachability);
        }

        /// <summary>
        /// The nodes that have seen the current version of the gossip. Convergence is reached once every
        /// member that counts has seen it. Merging two gossips yields a version nobody has seen, so the
        /// table starts over as empty.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> Seen { get { return _seen; } }
        /// <summary>
        /// Which observers consider which members unreachable, as reported by each node's failure detector.
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
    internal class GossipEnvelope : IClusterMessage
    {
        public GossipEnvelope(UniqueAddress from, UniqueAddress to, Gossip gossip, Deadline deadline = null)
        {
            From = from;
            To = to;
            Gossip = gossip;
            Deadline = deadline;
        }

        /// <summary>
        /// The sender of the gossip.
        /// </summary>
        public UniqueAddress From { get; }

        /// <summary>
        /// The receiver of the gossip.
        /// </summary>
        public UniqueAddress To { get; }

        /// <summary>
        /// The gossip content itself
        /// </summary>
        public Gossip Gossip { get; }
        
        /// <summary>
        /// The deadline for the gossip.
        /// </summary>
        public Deadline Deadline { get; set; }

        public override string ToString()
        {
            return $"GossipEnvelope(from={From}, to={To}, gossip={Gossip}, deadline={Deadline})";
        }
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
        /// The node that sent this status - either the one opening the chat or the one replying to it.
        /// </summary>
        public UniqueAddress From { get { return _from; } }
        /// <summary>
        /// The vector clock of the sender's gossip. The receiver compares it against its own to decide
        /// whether to reply with a <see cref="GossipEnvelope"/>, reply with its own status, or say nothing.
        /// </summary>
        public VectorClock Version { get { return _version; } }

        /// <summary>
        /// Creates a status message advertising what the sender knows.
        /// </summary>
        /// <param name="from">The sending node.</param>
        /// <param name="version">The vector clock of the sender's gossip.</param>
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
