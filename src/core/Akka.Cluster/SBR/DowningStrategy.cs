//-----------------------------------------------------------------------
// <copyright file="DowningStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Coordination;

namespace Akka.Cluster.SBR
{
    internal interface IDecision
    {
        bool IsIndirectlyConnected { get; }
    }

    internal class DownReachable : IDecision
    {
        public static readonly DownReachable Instance = new DownReachable();

        private DownReachable()
        {
        }

        public bool IsIndirectlyConnected => false;
    }

    internal class DownUnreachable : IDecision
    {
        public static readonly DownUnreachable Instance = new DownUnreachable();

        private DownUnreachable()
        {
        }

        public bool IsIndirectlyConnected => false;
    }

    internal class DownAll : IDecision
    {
        public static readonly DownAll Instance = new DownAll();

        private DownAll()
        {
        }

        public bool IsIndirectlyConnected => false;
    }

    internal class DownIndirectlyConnected : IDecision
    {
        public static readonly DownIndirectlyConnected Instance = new DownIndirectlyConnected();

        private DownIndirectlyConnected()
        {
        }

        public bool IsIndirectlyConnected => true;
    }

    internal interface IAcquireLeaseDecision : IDecision
    {
        TimeSpan AcquireDelay { get; }
    }

    internal class AcquireLeaseAndDownUnreachable : IAcquireLeaseDecision, IEquatable<AcquireLeaseAndDownUnreachable>
    {
        public AcquireLeaseAndDownUnreachable(TimeSpan acquireDelay)
        {
            AcquireDelay = acquireDelay;
        }

        public bool IsIndirectlyConnected => false;

        public TimeSpan AcquireDelay { get; }

        public bool Equals(AcquireLeaseAndDownUnreachable other)
        {
            if (ReferenceEquals(other, null))
                return false;
            return AcquireDelay.Equals(other.AcquireDelay);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AcquireLeaseAndDownUnreachable);
        }

        public override int GetHashCode()
        {
            return AcquireDelay.GetHashCode();
        }
    }

    internal class AcquireLeaseAndDownIndirectlyConnected : IAcquireLeaseDecision,
        IEquatable<AcquireLeaseAndDownIndirectlyConnected>
    {
        public AcquireLeaseAndDownIndirectlyConnected(TimeSpan acquireDelay)
        {
            AcquireDelay = acquireDelay;
        }

        public bool IsIndirectlyConnected => true;

        public TimeSpan AcquireDelay { get; }

        public bool Equals(AcquireLeaseAndDownIndirectlyConnected other)
        {
            if (ReferenceEquals(other, null))
                return false;
            return AcquireDelay.Equals(other.AcquireDelay);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AcquireLeaseAndDownIndirectlyConnected);
        }

        public override int GetHashCode()
        {
            return AcquireDelay.GetHashCode();
        }
    }

    internal class ReverseDownIndirectlyConnected : IDecision
    {
        public static readonly ReverseDownIndirectlyConnected Instance = new ReverseDownIndirectlyConnected();

        public bool IsIndirectlyConnected => true;

        private ReverseDownIndirectlyConnected()
        {
        }
    }

    internal abstract class DowningStrategy
    {
        protected DowningStrategy()
        {
            AllMembers = ImmutableSortedSet<Member>.Empty.WithComparer(Ordering);
        }

        protected IComparer<Member> Ordering { get; set; } = Member.Ordering;

        // may contain Joining and WeaklyUp
        public ImmutableHashSet<UniqueAddress> Unreachable { get; private set; } =
            ImmutableHashSet<UniqueAddress>.Empty;

        public string Role { get; protected set; }

        // all Joining and WeaklyUp members
        public ImmutableSortedSet<Member> Joining =>
            AllMembers.Where(m => m.Status == MemberStatus.Joining || m.Status == MemberStatus.WeaklyUp)
                .ToImmutableSortedSet(Ordering);

        // all members, both joining and up.
        public ImmutableSortedSet<Member> AllMembers { get; private set; }

        //All members, but doesn't contain Joining, WeaklyUp, Down and Exiting.
        public ImmutableSortedSet<Member> Members =>
            GetMembers(false, false);

        public ImmutableSortedSet<Member> MembersWithRole =>
            GetMembersWithRole(false, false);

        public ImmutableSortedSet<Member> ReachableMembers =>
            GetReachableMembers(false, false);

        public ImmutableSortedSet<Member> ReachableMembersWithRole =>
            GetReachableMembersWithRole(false, false);

        public ImmutableSortedSet<Member> UnreachableMembers =>
            GetUnreachableMembers(false, false);

        public ImmutableSortedSet<Member> UnreachableMembersWithRole =>
            GetUnreachableMembersWithRole(false, false);

        public Reachability Reachability { get; private set; } = Reachability.Empty;

        public ImmutableHashSet<Address> SeenBy { get; private set; } = ImmutableHashSet<Address>.Empty;

        /// <summary>
        ///     Nodes that are marked as unreachable but can communicate with gossip via a 3rd party.
        ///     Cycle in unreachability graph corresponds to that some node is both
        ///     observing another node as unreachable, and is also observed as unreachable by someone
        ///     else.
        ///     Another indication of indirectly connected nodes is if a node is marked as unreachable,
        ///     but it has still marked current gossip state as seen.
        ///     Those cases will not happen for clean splits and crashed nodes.
        /// </summary>
        public ImmutableHashSet<UniqueAddress> IndirectlyConnected =>
            IndirectlyConnectedFromIntersectionOfObserversAndSubjects.Union(IndirectlyConnectedFromSeenCurrentGossip);

        private ImmutableHashSet<UniqueAddress> IndirectlyConnectedFromIntersectionOfObserversAndSubjects
        {
            get
            {
                // cycle in unreachability graph
                var observers = Reachability.AllObservers;
                return observers.Intersect(Reachability.AllUnreachableOrTerminated);
            }
        }

        private ImmutableHashSet<UniqueAddress> IndirectlyConnectedFromSeenCurrentGossip =>
            Reachability.Records.SelectMany(r =>
            {
                if (SeenBy.Contains(r.Subject.Address))
                    return new[] { r.Observer, r.Subject };
                return Array.Empty<UniqueAddress>();
            }).ToImmutableHashSet();

        public bool HasIndirectlyConnected => !IndirectlyConnected.IsEmpty;

        public ImmutableHashSet<UniqueAddress> UnreachableButNotIndirectlyConnected =>
            Unreachable.Except(IndirectlyConnected);

        private ImmutableHashSet<UniqueAddress> AdditionalNodesToDownWhenIndirectlyConnected
        {
            get
            {
                if (UnreachableButNotIndirectlyConnected.IsEmpty) return ImmutableHashSet<UniqueAddress>.Empty;

                var originalUnreachable = Unreachable;
                var originalReachability = Reachability;
                try
                {
                    var intersectionOfObserversAndSubjects = IndirectlyConnectedFromIntersectionOfObserversAndSubjects;
                    var haveSeenCurrentGossip = IndirectlyConnectedFromSeenCurrentGossip;
                    // remove records between the indirectly connected
                    Reachability = Reachability.FilterRecords(
                        r =>
                            !(intersectionOfObserversAndSubjects.Contains(r.Observer) &&
                              intersectionOfObserversAndSubjects.Contains(r.Subject) ||
                              haveSeenCurrentGossip.Contains(r.Observer) && haveSeenCurrentGossip.Contains(r.Subject)));
                    Unreachable = Reachability.AllUnreachableOrTerminated;
                    var additionalDecision = Decide();

                    if (additionalDecision.IsIndirectlyConnected)
                        throw new InvalidOperationException(
                            $"SBR double {additionalDecision} decision, downing all instead. " +
                            $"originalReachability: [{originalReachability}], filtered reachability [{Reachability}], " +
                            $"still indirectlyConnected: [{string.Join(", ", IndirectlyConnected)}], seenBy: [{string.Join(", ", SeenBy)}]"
                        );

                    return NodesToDown(additionalDecision);
                }
                finally
                {
                    Unreachable = originalUnreachable;
                    Reachability = originalReachability;
                }
            }
        }

        public bool IsAllUnreachableDownOrExiting =>
            Unreachable.IsEmpty ||
            UnreachableMembers.All(m => m.Status == MemberStatus.Down || m.Status == MemberStatus.Exiting);

        public Lease Lease { get; protected set; }

        public bool IsUnreachable(Member m)
        {
            return Unreachable.Contains(m.UniqueAddress);
        }

        /// <summary>
        ///     All members in self DC, but doesn't contain Joining, WeaklyUp, Down and Exiting.
        ///     When `includingPossiblyUp=true` it also includes Joining and WeaklyUp members that could have been
        ///     changed to Up on the other side of a partition.
        ///     When `excludingPossiblyExiting=true` it doesn't include Leaving members that could have been
        ///     changed to Exiting on the other side of the partition.
        /// </summary>
        /// <param name="includingPossiblyUp"></param>
        /// <param name="excludingPossiblyExiting"></param>
        /// <returns></returns>
        public ImmutableSortedSet<Member> GetMembers(bool includingPossiblyUp, bool excludingPossiblyExiting)
        {
            return AllMembers.Where(m => !(!includingPossiblyUp && m.Status == MemberStatus.Joining ||
                                           !includingPossiblyUp && m.Status == MemberStatus.WeaklyUp ||
                                           excludingPossiblyExiting && m.Status == MemberStatus.Leaving ||
                                           m.Status == MemberStatus.Down ||
                                           m.Status == MemberStatus.Exiting)
            ).ToImmutableSortedSet(Ordering);
        }

        public ImmutableSortedSet<Member> GetMembersWithRole(bool includingPossiblyUp, bool excludingPossiblyExiting)
        {
            if (string.IsNullOrEmpty(Role))
                return GetMembers(includingPossiblyUp, excludingPossiblyExiting);
            return GetMembers(includingPossiblyUp, excludingPossiblyExiting).Where(m => m.HasRole(Role))
                .ToImmutableSortedSet(Ordering);
        }

        public ImmutableSortedSet<Member> GetReachableMembers(bool includingPossiblyUp, bool excludingPossiblyExiting)
        {
            var mbrs = GetMembers(includingPossiblyUp, excludingPossiblyExiting);
            if (Unreachable.IsEmpty)
                return mbrs;
            return mbrs.Where(m => !IsUnreachable(m)).ToImmutableSortedSet(Ordering);
        }

        public ImmutableSortedSet<Member> GetReachableMembersWithRole(bool includingPossiblyUp,
            bool excludingPossiblyExiting)
        {
            if (string.IsNullOrEmpty(Role))
                return GetReachableMembers(includingPossiblyUp, excludingPossiblyExiting);
            return GetReachableMembers(includingPossiblyUp, excludingPossiblyExiting).Where(m => m.HasRole(Role))
                .ToImmutableSortedSet(Ordering);
        }

        public ImmutableSortedSet<Member> GetUnreachableMembers(bool includingPossiblyUp, bool excludingPossiblyExiting)
        {
            if (Unreachable.IsEmpty)
                return ImmutableSortedSet<Member>.Empty;
            return GetMembers(includingPossiblyUp, excludingPossiblyExiting).Where(IsUnreachable)
                .ToImmutableSortedSet(Ordering);
        }

        public ImmutableSortedSet<Member> GetUnreachableMembersWithRole(bool includingPossiblyUp,
            bool excludingPossiblyExiting)
        {
            if (string.IsNullOrEmpty(Role))
                return GetUnreachableMembers(includingPossiblyUp, excludingPossiblyExiting);
            return GetUnreachableMembers(includingPossiblyUp, excludingPossiblyExiting).Where(m => m.HasRole(Role))
                .ToImmutableSortedSet(Ordering);
        }

        public void AddUnreachable(Member m)
        {
            Add(m);
            Unreachable = Unreachable.Add(m.UniqueAddress);
        }

        public void AddReachable(Member m)
        {
            Add(m);
            Unreachable = Unreachable.Remove(m.UniqueAddress);
        }

        public void Add(Member m)
        {
            RemoveFromAllMembers(m);
            AllMembers = AllMembers.Add(m);
        }

        public void Remove(Member m)
        {
            RemoveFromAllMembers(m);
            Unreachable = Unreachable.Remove(m.UniqueAddress);
        }

        private void RemoveFromAllMembers(Member m)
        {
            if (ReferenceEquals(Ordering, Member.Ordering))
                AllMembers = AllMembers.Remove(m);
            else
                // must use filterNot for removals/replace in the SortedSet when
                // ageOrdering is using upNumber and that will change when Joining -> Up
                AllMembers = AllMembers.Where(i => !i.UniqueAddress.Equals(m.UniqueAddress))
                    .ToImmutableSortedSet(Ordering);
        }

        public void SetReachability(Reachability r)
        {
            // skip records with Reachability.Reachable, and skip records related to other DC
            Reachability = r.FilterRecords(record =>
                record.Status == Reachability.ReachabilityStatus.Unreachable ||
                record.Status == Reachability.ReachabilityStatus.Terminated
            );
        }

        public void SetSeenBy(ImmutableHashSet<Address> s)
        {
            SeenBy = s;
        }

        public ImmutableHashSet<UniqueAddress> NodesToDown(IDecision decision = null)
        {
            decision = decision ?? Decide();

            var downable = Members
                .Union(Joining)
                .Where(m => m.Status != MemberStatus.Down && m.Status != MemberStatus.Exiting)
                .Select(m => m.UniqueAddress)
                .ToImmutableHashSet();

            switch (decision)
            {
                case DownUnreachable _:
                case AcquireLeaseAndDownUnreachable _:
                    return downable.Intersect(Unreachable);
                case DownReachable _:
                    return downable.Except(Unreachable);
                case DownAll _:
                    return downable;
                case DownIndirectlyConnected _:
                case AcquireLeaseAndDownIndirectlyConnected _:
                    // Down nodes that have been marked as unreachable via some network links but they are still indirectly
                    // connected via other links. It will keep other "normal" nodes.
                    // If there is a combination of indirectly connected nodes and a clean network partition (or node crashes)
                    // it will combine the above decision with the ordinary decision, e.g. keep majority, after excluding
                    // failure detection observations between the indirectly connected nodes.
                    // Also include nodes that corresponds to the decision without the unreachability observations from
                    // the indirectly connected nodes
                    return downable.Intersect(IndirectlyConnected.Union(AdditionalNodesToDownWhenIndirectlyConnected));

                case ReverseDownIndirectlyConnected _:
                    // indirectly connected + all reachable
                    return downable.Intersect(IndirectlyConnected).Union(downable.Except(Unreachable));
            }

            throw new InvalidOperationException();
        }

        public IDecision ReverseDecision(IDecision decision)
        {
            switch (decision)
            {
                case DownUnreachable _:
                    return DownReachable.Instance;
                case AcquireLeaseAndDownUnreachable _:
                    return DownReachable.Instance;
                case DownReachable _:
                    return DownUnreachable.Instance;
                case DownAll _:
                    return DownAll.Instance;
                case DownIndirectlyConnected _:
                    return ReverseDownIndirectlyConnected.Instance;
                case AcquireLeaseAndDownIndirectlyConnected _:
                    return ReverseDownIndirectlyConnected.Instance;
                case ReverseDownIndirectlyConnected _:
                    return DownIndirectlyConnected.Instance;
            }

            throw new InvalidOperationException();
        }

        public abstract IDecision Decide();
    }

    /// <summary>
    ///     Down the unreachable nodes if the number of remaining nodes are greater than or equal to the
    ///     given `quorumSize`. Otherwise down the reachable nodes, i.e. it will shut down that side of the partition.
    ///     In other words, the `quorumSize` defines the minimum number of nodes that the cluster must have to be operational.
    ///     If there are unreachable nodes when starting up the cluster, before reaching this limit,
    ///     the cluster may shutdown itself immediately. This is not an issue if you start all nodes at
    ///     approximately the same time.
    ///     Note that you must not add more members to the cluster than `quorumSize * 2 - 1`, because then
    ///     both sides may down each other and thereby form two separate clusters. For example,
    ///     quorum quorumSize configured to 3 in a 6 node cluster may result in a split where each side
    ///     consists of 3 nodes each, i.e. each side thinks it has enough nodes to continue by
    ///     itself. A warning is logged if this recommendation is violated.
    ///     If the `role` is defined the decision is based only on members with that `role`.
    ///     It is only counting members within the own data center.
    /// </summary>
    internal class StaticQuorum : DowningStrategy
    {
        public StaticQuorum(int quorumSize, string role)
        {
            QuorumSize = quorumSize;
            Role = role;
        }

        public int QuorumSize { get; }

        public bool IsTooManyMembers =>
            MembersWithRole.Count > QuorumSize * 2 - 1;

        public override IDecision Decide()
        {
            if (IsTooManyMembers)
                return DownAll.Instance;
            if (HasIndirectlyConnected)
                return DownIndirectlyConnected.Instance;
            if (MembersWithRole.Count - UnreachableMembersWithRole.Count >= QuorumSize)
                return DownUnreachable.Instance;
            return DownReachable.Instance;
        }
    }

    /// <summary>
    ///     Down the unreachable nodes if the current node is in the majority part based the last known
    ///     membership information. Otherwise down the reachable nodes, i.e. the own part. If the the
    ///     parts are of equal size the part containing the node with the lowest address is kept.
    ///     If the `role` is defined the decision is based only on members with that `role`.
    ///     Note that if there are more than two partitions and none is in majority each part
    ///     will shutdown itself, terminating the whole cluster.
    ///     It is only counting members within the own data center.
    /// </summary>
    internal class KeepMajority : DowningStrategy
    {
        public KeepMajority(string role)
        {
            Role = role;
        }

        public override IDecision Decide()
        {
            if (HasIndirectlyConnected) return DownIndirectlyConnected.Instance;

            var ms = MembersWithRole;
            if (ms.IsEmpty) return DownAll.Instance; // no node with matching role

            var reachableSize = ReachableMembersWithRole.Count;
            var unreachableSize = UnreachableMembersWithRole.Count;

            var decision = MajorityDecision(reachableSize, unreachableSize, ms.FirstOrDefault());

            switch (decision)
            {
                case DownUnreachable _:
                    var decision2 = MajorityDecisionWhenIncludingMembershipChangesEdgeCase();
                    switch (decision2)
                    {
                        case DownUnreachable _:
                            return DownUnreachable.Instance; // same conclusion
                        default:
                            return DownAll.Instance; // different conclusion, safest to DownAll
                    }
                default:
                    return decision;
            }
        }

        private IDecision MajorityDecision(int thisSide, int otherSide, Member lowest)
        {
            if (thisSide == otherSide)
            {
                // equal size, keep the side with the lowest address (first in members)
                if (IsUnreachable(lowest))
                    return DownReachable.Instance;
                return DownUnreachable.Instance;
            }

            if (thisSide > otherSide)
                // we are in majority
                return DownUnreachable.Instance;
            return DownReachable.Instance;
        }

        /// <summary>
        ///     Check for edge case when membership change happens at the same time as partition.
        ///     Count Joining and WeaklyUp on other side since those might be Up on other side.
        ///     Don't count Leaving on this side since those might be Exiting on other side.
        ///     Note that the membership changes we are looking for will only be done when all
        ///     members have seen previous state, i.e. when a member is moved to Up everybody
        ///     has seen it joining.
        /// </summary>
        /// <returns></returns>
        private IDecision MajorityDecisionWhenIncludingMembershipChangesEdgeCase()
        {
            // for this side we count as few as could be possible (excluding joining, excluding leaving)
            var ms = GetMembersWithRole(false, true);
            if (ms.IsEmpty) return DownAll.Instance;

            var thisSideReachableSize = GetReachableMembersWithRole(false, true).Count;
            // for other side we count as many as could be possible (including joining, including leaving)
            var otherSideUnreachableSize = GetUnreachableMembersWithRole(true, false).Count;
            return MajorityDecision(thisSideReachableSize, otherSideUnreachableSize, ms.FirstOrDefault());
        }
    }

    /// <summary>
    ///     Down the part that does not contain the oldest member (current singleton).
    ///     There is one exception to this rule if `downIfAlone` is defined to `true`.
    ///     Then, if the oldest node has partitioned from all other nodes the oldest will
    ///     down itself and keep all other nodes running. The strategy will not down the
    ///     single oldest node when it is the only remaining node in the cluster.
    ///     Note that if the oldest node crashes the others will remove it from the cluster
    ///     when `downIfAlone` is `true`, otherwise they will down themselves if the
    ///     oldest node crashes, i.e. shutdown the whole cluster together with the oldest node.
    ///     If the `role` is defined the decision is based only on members with that `role`,
    ///     i.e. using the oldest member (singleton) within the nodes with that role.
    ///     It is only using members within the own data center, i.e. oldest within the
    ///     data center.
    /// </summary>
    internal class KeepOldest : DowningStrategy
    {
        public KeepOldest(bool downIfAlone, string role)
        {
            DownIfAlone = downIfAlone;
            Role = role;

            // sort by age, oldest first
            Ordering = Member.AgeOrdering;
        }

        public bool DownIfAlone { get; }

        public override IDecision Decide()
        {
            if (HasIndirectlyConnected) return DownIndirectlyConnected.Instance;

            var ms = MembersWithRole;
            if (ms.IsEmpty) return DownAll.Instance; // no node with matching role

            var oldest = ms.FirstOrDefault();
            var oldestIsReachable = !IsUnreachable(oldest);
            var reachableCount = ReachableMembersWithRole.Count;
            var unreachableCount = UnreachableMembersWithRole.Count;

            var decision = OldestDecision(oldestIsReachable, reachableCount, unreachableCount);
            switch (decision)
            {
                case DownUnreachable _:
                    var decision2 = OldestDecisionWhenIncludingMembershipChangesEdgeCase();
                    switch (decision2)
                    {
                        case DownUnreachable _:
                            return DownUnreachable.Instance; // same conclusion
                        default:
                            return DownAll.Instance; // different conclusion, safest to DownAll
                    }
                default:
                    return decision;
            }
        }

        private IDecision OldestDecision(bool oldestIsOnThisSide, int thisSide, int otherSide)
        {
            if (oldestIsOnThisSide)
            {
                // if there are only 2 nodes in the cluster it is better to keep the oldest, even though it is alone
                // E.g. 2 nodes: thisSide=1, otherSide=1 => DownUnreachable, i.e. keep the oldest
                //               even though it is alone (because the node on the other side is no better)
                // E.g. 3 nodes: thisSide=1, otherSide=2 => DownReachable, i.e. shut down the
                //               oldest because it is alone
                if (DownIfAlone && thisSide == 1 && otherSide >= 2)
                    return DownReachable.Instance;
                return DownUnreachable.Instance;
            }

            if (DownIfAlone && otherSide == 1 && thisSide >= 2)
                return DownUnreachable.Instance;
            return DownReachable.Instance;
        }

        /// <summary>
        ///     Check for edge case when membership change happens at the same time as partition.
        ///     Exclude Leaving on this side because those could be Exiting on other side.
        ///     When `downIfAlone` also consider Joining and WeaklyUp since those might be Up on other side,
        ///     and thereby flip the alone test.
        /// </summary>
        /// <returns></returns>
        private IDecision OldestDecisionWhenIncludingMembershipChangesEdgeCase()
        {
            var ms = GetMembersWithRole(false, true);
            if (ms.IsEmpty) return DownAll.Instance;

            var oldest = ms.First();
            var oldestIsReachable = !IsUnreachable(oldest);
            // Joining and WeaklyUp are only relevant when downIfAlone = true
            var includingPossiblyUp = DownIfAlone;
            var reachableCount = GetReachableMembersWithRole(includingPossiblyUp, true).Count;
            var unreachableCount = GetUnreachableMembersWithRole(includingPossiblyUp, true).Count;

            return OldestDecision(oldestIsReachable, reachableCount, unreachableCount);
        }
    }

    /// <summary>
    ///     Down all nodes unconditionally.
    /// </summary>
    internal class DownAllNodes : DowningStrategy
    {
        public override IDecision Decide()
        {
            return DownAll.Instance;
        }
    }

    /// <summary>
    ///     Keep the part that can acquire the lease, and down the other part.
    ///     Best effort is to keep the side that has most nodes, i.e. the majority side.
    ///     This is achieved by adding a delay before trying to acquire the lease on the
    ///     minority side.
    ///     If the `role` is defined the majority/minority is based only on members with that `role`.
    ///     It is only counting members within the own data center.
    /// </summary>
    internal class LeaseMajority : DowningStrategy
    {
        public LeaseMajority(string role, Lease lease, TimeSpan acquireLeaseDelayForMinority)
        {
            Role = role;
            Lease = lease;
            AcquireLeaseDelayForMinority = acquireLeaseDelayForMinority;
        }

        public TimeSpan AcquireLeaseDelayForMinority { get; }

        private TimeSpan AcquireLeaseDelay
        {
            get
            {
                if (IsInMinority)
                    return AcquireLeaseDelayForMinority;
                return TimeSpan.Zero;
            }
        }

        private bool IsInMinority
        {
            get
            {
                var ms = MembersWithRole;
                if (ms.IsEmpty) return false; // no node with matching role

                var unreachableSize = UnreachableMembersWithRole.Count;
                var membersSize = ms.Count;

                if (unreachableSize * 2 == membersSize)
                    // equal size, try to keep the side with the lowest address (first in members)
                    return IsUnreachable(ms.FirstOrDefault());
                if (unreachableSize * 2 < membersSize)
                    // we are in majority
                    return false;
                return true;
            }
        }

        public override IDecision Decide()
        {
            if (HasIndirectlyConnected)
                return new AcquireLeaseAndDownIndirectlyConnected(TimeSpan.Zero);
            return new AcquireLeaseAndDownUnreachable(AcquireLeaseDelay);
        }
    }
}
