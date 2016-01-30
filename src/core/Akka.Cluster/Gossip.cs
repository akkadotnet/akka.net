//-----------------------------------------------------------------------
// <copyright file="Gossip.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
    class Gossip
    {
        public static readonly ImmutableSortedSet<Member> EmptyMembers = ImmutableSortedSet.Create<Member>();
        public static readonly Gossip Empty = new Gossip(EmptyMembers);

        public static Gossip Create(ImmutableSortedSet<Member> members)
        {
            if (members.IsEmpty) return Empty;
            return Empty.Copy(members: members);
        }

        static readonly ImmutableHashSet<MemberStatus> LeaderMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving);

        static readonly ImmutableHashSet<MemberStatus> ConvergenceMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Leaving);

        public static readonly ImmutableHashSet<MemberStatus> ConvergenceSkipUnreachableWithMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Down, MemberStatus.Exiting);

        public static readonly ImmutableHashSet<MemberStatus> RemoveUnreachableWithMemberStatus =
            ImmutableHashSet.Create(MemberStatus.Down, MemberStatus.Exiting);

        readonly ImmutableSortedSet<Member> _members;
        readonly GossipOverview _overview;
        readonly VectorClock _version;
        private readonly Lazy<Reachability> _reachability;

        public ImmutableSortedSet<Member> Members { get { return _members; } }
        public GossipOverview Overview { get { return _overview; } }
        public VectorClock Version { get { return _version; } }
        public Reachability ReachabilityExcludingDownedObservers { get { return _reachability.Value; } }

        public Gossip(ImmutableSortedSet<Member> members) : this(members, new GossipOverview(), VectorClock.Create() ) {}

        public Gossip(ImmutableSortedSet<Member> members, GossipOverview overview) : this(members, overview, VectorClock.Create()) { }

        public Gossip(ImmutableSortedSet<Member> members, GossipOverview overview, VectorClock version)
        {
            _members = members;
            _overview = overview;
            _version = version;

            _membersMap = new Lazy<ImmutableDictionary<UniqueAddress, Member>>(
                () => members.ToImmutableDictionary(m => m.UniqueAddress, m => m));

            _reachability = new Lazy<Reachability>(() =>
            {
                var downed = _members
                    .Where(m => m.Status == MemberStatus.Down)
                    .Select(m=>m.UniqueAddress);

                return overview.Reachability.Remove(downed);
            });

            if (Cluster.IsAssertInvariantsEnabled) AssertInvariants();
        }

        public Gossip Copy(ImmutableSortedSet<Member> members = null, GossipOverview overview = null,
            VectorClock version = null)
        {
            return new Gossip(members ?? _members, overview ?? _overview, version ?? _version);
        }

        private void AssertInvariants()
        {
            if(_members.Any(m => m.Status == MemberStatus.Removed))
                throw new ArgumentException(string.Format("Live members must not have status [Removed], got {0}", 
                    _members.Where(m => m.Status == MemberStatus.Removed).Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b)));

            var inReachabilityButNotMember =
                _overview.Reachability.AllObservers.Except(_members.Select(m => m.UniqueAddress));
            if(!inReachabilityButNotMember.IsEmpty)
                throw new ArgumentException("Nodes not part of cluster in reachability table, got {0}", 
                    inReachabilityButNotMember.Select(a => a.ToString()).Aggregate((a,b) => a + ", " + b));

            var seenButNotMember = _overview.Seen.Except(_members.Select(m => m.UniqueAddress));
            if (!seenButNotMember.IsEmpty)
                throw new ArgumentException("Nodes not part of cluster have marked the Gossip as seen, got {0}",
                    seenButNotMember.Select(a => a.ToString()).Aggregate((a, b) => a + ", " + b));
        }

        //TODO: Serializer should ignore
        Lazy<ImmutableDictionary<UniqueAddress, Member>> _membersMap;

        /// <summary>
        /// Increments the version for this 'Node'.
        /// </summary>
        public Gossip Increment(VectorClock.Node node)
        {
            return Copy(version: _version.Increment(node));
        }

        /// <summary>
        /// Adds a member to the member node ring.
        /// </summary>
        /// <param name="member"></param>
        /// <returns></returns>
        public Gossip AddMember(Member member)
        {
            if (_members.Contains(member)) return this;
            return Copy(members: _members.Add(member));
        }

        /// <summary>
        /// Marks the gossip as seen by this node (address) by updating the address entry in the 'gossip.overview.seen'
        /// </summary>
        public Gossip Seen(UniqueAddress node)
        {
            if (SeenByNode(node)) return this;
            return Copy(overview: _overview.Copy(seen: _overview.Seen.Add(node)));
        }
        
        /// <summary>
        /// Marks the gossip as seen by only this node (address) by replacing the 'gossip.overview.seen'
        /// </summary>
        public Gossip OnlySeen(UniqueAddress node)
        {
            return Copy(overview: _overview.Copy(seen: ImmutableHashSet.Create(node)));
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
        public bool SeenByNode(UniqueAddress node)
        {
            return _overview.Seen.Contains(node);
        }

        public Gossip MergeSeen(Gossip that)
        {
            return Copy(overview: _overview.Copy(seen: _overview.Seen.Union(that._overview.Seen)));
        }

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

        // First check that:
        //   1. we don't have any members that are unreachable, or
        //   2. all unreachable members in the set have status DOWN or EXITING
        // Else we can't continue to check for convergence
        // When that is done we check that all members with a convergence
        // status is in the seen table and has the latest vector clock
        // version
        public bool Convergence(UniqueAddress selfUniqueAddress)
        {
            var unreachable = _overview.Reachability.AllUnreachableOrTerminated
                .Where(node => node != selfUniqueAddress)
                .Select(GetMember);

            var convergedUnreachable = unreachable
                .All(m => ConvergenceSkipUnreachableWithMemberStatus.Contains(m.Status));

            var convergedSeen =
                !_members.Any(m => ConvergenceMemberStatus.Contains(m.Status) && !SeenByNode(m.UniqueAddress));

            return convergedUnreachable && convergedSeen;
        }

        public bool IsLeader(UniqueAddress node, UniqueAddress selfUniqueAddress)
        {
            return Leader(selfUniqueAddress) == node && node != null;
        }

        public UniqueAddress Leader(UniqueAddress selfUniqueAddress)
        {
           return LeaderOf(_members, selfUniqueAddress);
        }

        public UniqueAddress RoleLeader(string role, UniqueAddress selfUniqueAddress)
        {
            var roleMembers = _members
                .Where(m => m.HasRole(role))
                .ToImmutableSortedSet();

            return LeaderOf(roleMembers, selfUniqueAddress);
        }

        private UniqueAddress LeaderOf(ImmutableSortedSet<Member> mbrs, UniqueAddress selfUniqueAddress)
        {
            var reachableMembers = _overview.Reachability.IsAllReachable
                ? mbrs
                : mbrs
                    .Where(m => _overview.Reachability.IsReachable(m.UniqueAddress) || m.UniqueAddress == selfUniqueAddress)
                    .ToImmutableSortedSet();

            if (!reachableMembers.Any()) return null;

            var member = reachableMembers.FirstOrDefault(m => LeaderMemberStatus.Contains(m.Status)) ??
                         reachableMembers.Min(Member.LeaderStatusOrdering);

            return member.UniqueAddress;
        }

        public ImmutableHashSet<string> AllRoles
        {
            get { return _members.SelectMany(m => m.Roles).ToImmutableHashSet(); }
        }

        public bool IsSingletonCluster
        {
            get { return _members.Count == 1; }
        }

        public Member GetMember(UniqueAddress node)
        {
            return _membersMap.Value.GetOrElse(node, 
                Member.Removed(node)); // placeholder for removed member
        }

        public bool HasMember(UniqueAddress node)
        {
            return _membersMap.Value.ContainsKey(node);
        }


        public Member YoungestMember
        {
            get
            {
                //TODO: Akka exception?
                if (!_members.Any()) throw new Exception("No youngest when no members");
                return _members.MaxBy(m => m.UpNumber == int.MaxValue ? 0 : m.UpNumber);
            }
        }

        public override string ToString()
        {
            return String.Format("Gossip(members = [{0}], overview = {1}, version = {2}",
                _members.Select(m => m.ToString()).Aggregate((a, b) => a + ", " + b), _overview, _version);
        }
    }

    /// <summary>
    /// Represents the overview of the cluster, holds the cluster convergence table and set with unreachable nodes.
    /// </summary>
    class GossipOverview
    {
        readonly ImmutableHashSet<UniqueAddress> _seen;
        readonly Reachability _reachability;

        public GossipOverview() : this(ImmutableHashSet.Create<UniqueAddress>(), Reachability.Empty) { }

        public GossipOverview(Reachability reachability) : this(ImmutableHashSet.Create<UniqueAddress>(), reachability) { }

        public GossipOverview(ImmutableHashSet<UniqueAddress> seen, Reachability reachability)
        {
            _seen = seen;
            _reachability = reachability;
        }

        public GossipOverview Copy(ImmutableHashSet<UniqueAddress> seen = null, Reachability reachability = null)
        {
            return new GossipOverview(seen ?? _seen, reachability ?? _reachability);
        }

        public ImmutableHashSet<UniqueAddress> Seen { get { return _seen; } }
        public Reachability Reachability { get { return _reachability; } }
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

        public GossipEnvelope(UniqueAddress from, UniqueAddress to, Gossip gossip, Deadline deadline = null)
        {
            _from = from;
            _to = to;
            Gossip = gossip;
            Deadline = deadline;
        }

        public UniqueAddress From { get { return _from; } }
        public UniqueAddress To { get { return _to; } }
        public Gossip Gossip { get; set; }
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

        public UniqueAddress From { get { return _from; } }
        public VectorClock Version { get { return _version; } }

        public GossipStatus(UniqueAddress from, VectorClock version)
        {
            _from = from;
            _version = version;
        }

        protected bool Equals(GossipStatus other)
        {
            return _from.Equals(other._from) && _version.IsSameAs(other._version);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((GossipStatus) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_from.GetHashCode() * 397) ^ _version.GetHashCode();
            }
        }

    }
}


