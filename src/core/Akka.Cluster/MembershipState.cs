//-----------------------------------------------------------------------
// <copyright file="MembershipState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Annotations;
using Akka.Util;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    internal sealed class MembershipState : IEquatable<MembershipState>
    {
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

        public MembershipState(Gossip latestGossip, UniqueAddress selfUniqueAddress)
        {
            LatestGossip = latestGossip;
            SelfUniqueAddress = selfUniqueAddress;
        }

        private Member _selfMember = null;

        public Member SelfMember
        {
            get
            {
                if (_selfMember == null)
                {
                    _selfMember = LatestGossip.GetMember(SelfUniqueAddress);
                }

                return _selfMember;
            }
        }

        public Gossip LatestGossip { get; }

        public UniqueAddress SelfUniqueAddress { get; }

        public GossipOverview Overview => LatestGossip.Overview;

        public ImmutableSortedSet<Member> Members => LatestGossip.Members;

        /// <summary>
        /// TODO: this will eventually need to be made DC-aware and tailored specifically to the current DC
        /// </summary>
        public Reachability DcReachability => Overview.Reachability;

        private Option<Reachability> _reachabilityExcludingDownedObservers = Option<Reachability>.None;
        public Reachability DcReachabilityExcludingDownedObservers
        {
            get
            {
                if (_reachabilityExcludingDownedObservers.IsEmpty)
                {
                    // TODO: adjust for data center
                    var membersToExclude = Members
                        .Where(x => x.Status == MemberStatus.Down)
                        .Select(x => x.UniqueAddress).ToImmutableHashSet();
                    _reachabilityExcludingDownedObservers = Overview.Reachability.RemoveObservers(membersToExclude);
                }

                return _reachabilityExcludingDownedObservers.Value;
            }
        }

        public bool IsReachableExcludingDownedObservers(UniqueAddress toAddress)
        {
            if (!LatestGossip.HasMember(toAddress)) return false;

            // TODO: check for multiple DCs
            return LatestGossip.ReachabilityExcludingDownedObservers.Value.IsReachable(toAddress);
        }

        public UniqueAddress Leader => LeaderOf(Members);

        public UniqueAddress LeaderOf(IImmutableSet<Member> mbrs)
        {
            var reachability = DcReachability;
            var reachableMembers = (reachability.IsAllReachable
                    ? mbrs.Where(m => m.Status != MemberStatus.Down)
                    : mbrs
                        .Where(m => m.Status != MemberStatus.Down && reachability.IsReachable(m.UniqueAddress) || m.UniqueAddress == SelfUniqueAddress))
                .ToImmutableSortedSet();

            if (!reachableMembers.Any()) return null;

            var member = reachableMembers.FirstOrDefault(m => LeaderMemberStatus.Contains(m.Status)) ??
                         reachableMembers.Min(Member.LeaderStatusOrdering);

            return member.UniqueAddress;
        }

        public bool IsLeader(UniqueAddress node)
        {
            return Leader != null && Leader.Equals(node);
        }

        public UniqueAddress RoleLeader(string role)
        {
            return LeaderOf(Members.Where(x => x.HasRole(role)).ToImmutableHashSet());
        }

        /// <summary>
        /// First check that:
        ///   1. we don't have any members that are unreachable, or
        ///   2. all unreachable members in the set have status DOWN or EXITING
        /// Else we can't continue to check for convergence. When that is done 
        /// we check that all members with a convergence status is in the seen 
        /// table and has the latest vector clock version.
        /// </summary>
        /// <param name="exitingConfirmed">The set of nodes who have been confirmed to be exiting.</param>
        /// <returns><c>true</c> if convergence has been achieved. <c>false</c> otherwise.</returns>
        public bool Convergence(IImmutableSet<UniqueAddress> exitingConfirmed)
        {
            // If another member in the data center that is UP or LEAVING
            // and has not seen this gossip or is exiting
            // convergence cannot be reached
            bool MemberHinderingConvergenceExists()
            {
                return Members.Any(x => ConvergenceMemberStatus.Contains(x.Status)
                                        && !(LatestGossip.SeenByNode(x.UniqueAddress) ||
                                             exitingConfirmed.Contains(x.UniqueAddress)));
            }

            // Find cluster members in the data center that are unreachable from other members of the data center
            // excluding observations from members outside of the data center, that have status DOWN or is passed in as confirmed exiting.
            var unreachable = DcReachabilityExcludingDownedObservers.AllUnreachableOrTerminated
                .Where(node => node != SelfUniqueAddress && !exitingConfirmed.Contains(node))
                .Select(x => LatestGossip.GetMember(x));

            // unreachables outside of the data center or with status DOWN or EXITING does not affect convergence
            var allUnreachablesCanBeIgnored =
                unreachable.All(m => ConvergenceSkipUnreachableWithMemberStatus.Contains(m.Status));

            return allUnreachablesCanBeIgnored && !MemberHinderingConvergenceExists();
        }

        /// <summary>
        /// Copies the current <see cref="MembershipState"/> and marks the <see cref="LatestGossip"/> as Seen
        /// by the <see cref="SelfUniqueAddress"/>.
        /// </summary>
        /// <returns>A new <see cref="MembershipState"/> instance with the updated seen records.</returns>
        public MembershipState Seen() => Copy(LatestGossip.Seen(SelfUniqueAddress));

        public MembershipState Copy(Gossip gossip = null, UniqueAddress selfUniqueAddress = null)
        {
            return new MembershipState(gossip ?? LatestGossip, selfUniqueAddress ?? SelfUniqueAddress);
        }

        public bool Equals(MembershipState other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return SelfUniqueAddress.Equals(other.SelfUniqueAddress) && LatestGossip.Equals(other.LatestGossip);
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is MembershipState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (LatestGossip.GetHashCode() * 397) ^ SelfUniqueAddress.GetHashCode();
            }
        }

        /// <summary>
        /// Never gossip to self and not to node marked as unreachable by self (heartbeat
        /// messages are not getting through so no point in trying to gossip).
        ///
        ///  Nodes marked as unreachable by others are still valid targets for gossip.
        /// </summary>
        /// <param name="node">The node to check for gossip validity.</param>
        /// <returns><c>true</c> if we can gossip to this node, <c>false</c> otherwise.</returns>
        public bool ValidNodeForGossip(UniqueAddress node)
        {
            return !node.Equals(SelfUniqueAddress) && Overview.Reachability.IsReachable(SelfUniqueAddress, node);
        }
    }
}
