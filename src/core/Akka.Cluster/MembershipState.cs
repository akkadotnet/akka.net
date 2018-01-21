#region copyright
//-----------------------------------------------------------------------
// <copyright file="MembershipState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Cluster
{
    internal sealed class MembershipState
    {
        private static readonly MemberStatus[] LeaderMemberStatus = { MemberStatus.Up, MemberStatus.Leaving };

        private static readonly MemberStatus[] ConvergenceMemberStatus = { MemberStatus.Up, MemberStatus.Leaving };
        public static readonly MemberStatus[] ConvergenceSkipUnreachableWithMemberStatus = { MemberStatus.Down, MemberStatus.Exiting };
        public static readonly MemberStatus[] RemoveUnreachableWithMemberStatus = { MemberStatus.Down, MemberStatus.Exiting };

        public Gossip LatestGossip { get; }
        public UniqueAddress SelfUniqueAddress { get; }
        public string SelfDataCenter { get; }
        public int CrossDataCenterConnections { get; }

        public MembershipState(Gossip latestGossip, UniqueAddress selfUniqueAddress, string selfDataCenter, int crossDataCenterConnections)
        {
            LatestGossip = latestGossip;
            SelfUniqueAddress = selfUniqueAddress;
            SelfDataCenter = selfDataCenter;
            CrossDataCenterConnections = crossDataCenterConnections;
        }

        private Member _selfMember;
        private Reachability _dcReachability;
        private Reachability _dcReachabilityWithoutObservationsWithin;
        private Reachability _dcReachabilityExcludingDownedObservers;
        private Reachability _dcReachabilityNoOutsideNodes;
        private ImmutableDictionary<string, ImmutableSortedSet<Member>> _ageSortedTopOldestMembersPerDc;

        public Member SelfMember => _selfMember ?? (_selfMember = LatestGossip.GetMember(SelfUniqueAddress));

        /// <summary>
        /// Reachability excluding observations from nodes outside of the data center, 
        /// but including observed unreachable nodes outside of the data center.
        /// </summary>
        public Reachability DcReachability => _dcReachability ?? (_dcReachability = Overview.Reachability.RemoveObservers(Members.Where(m => m.DataCenter != SelfDataCenter).Select(m => m.UniqueAddress).ToImmutableHashSet()));

        /// <summary>
        /// Reachability excluding observations from nodes outside of the data center 
        /// and observations within self data center, but including observed unreachable 
        /// nodes outside of the data center.
        /// </summary>
        public Reachability DcReachabilityWithoutObservationsWithin =>
            _dcReachabilityWithoutObservationsWithin ?? (_dcReachabilityWithoutObservationsWithin = DcReachability.FilterRecords(r => LatestGossip.GetMember(r.Subject).DataCenter != SelfDataCenter));

        /// <summary>
        /// Reachability for data center nodes, with observations from outside the data center or from downed nodes filtered out.
        /// </summary>
        public Reachability DcReachabilityExcludingDownedObservers
        {
            get
            {
                if (_dcReachabilityExcludingDownedObservers == null)
                {
                    var membersToExclude = ImmutableHashSet<UniqueAddress>.Empty.ToBuilder();
                    var nonDcMembers = new List<UniqueAddress>();

                    foreach (var member in Members)
                    {
                        if (member.DataCenter != SelfDataCenter)
                        {
                            nonDcMembers.Add(member.UniqueAddress);
                            membersToExclude.Add(member.UniqueAddress);
                        }
                        else if (member.Status == MemberStatus.Down)
                        {
                            membersToExclude.Add(member.UniqueAddress);
                        }
                    }

                    _dcReachabilityExcludingDownedObservers = Overview.Reachability
                        .RemoveObservers(membersToExclude.ToImmutable())
                        .Remove(nonDcMembers);
                }
                return _dcReachabilityExcludingDownedObservers;
            }
        }

        public Reachability DcReachabilityNoOutsideNodes =>
            _dcReachabilityNoOutsideNodes ?? (_dcReachabilityNoOutsideNodes = Overview.Reachability.Remove(Members.Where(m => m.DataCenter != SelfDataCenter).Select(m => m.UniqueAddress)));

        /// <summary>
        /// Up to <see cref="CrossDataCenterConnections"/> number of the oldest members for each DC.
        /// </summary>
        public ImmutableDictionary<string, ImmutableSortedSet<Member>> AgeSortedTopOldestMembersPerDc
        {
            get
            {
                if (_ageSortedTopOldestMembersPerDc == null)
                {
                    var acc = ImmutableDictionary<string, ImmutableSortedSet<Member>>.Empty.ToBuilder();
                    foreach (var member in LatestGossip.Members)
                    {
                        if (acc.TryGetValue(member.DataCenter, out var set))
                        {
                            if (set.Count < CrossDataCenterConnections)
                            {
                                acc[member.DataCenter] = set.Add(member);
                            }
                            else
                            {
                                var younger = set.FirstOrDefault(m => member.IsOlderThan(m));
                                if (younger != null)
                                {
                                    acc[member.DataCenter] = set.Remove(younger).Add(member);
                                }
                            }
                        }
                        else
                        {
                            acc[member.DataCenter] = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering).Add(member);
                        }
                    }

                    _ageSortedTopOldestMembersPerDc = acc.ToImmutable();
                }

                return _ageSortedTopOldestMembersPerDc;
            }
        }

        public ImmutableSortedSet<Member> Members => LatestGossip.Members;

        public GossipOverview Overview => LatestGossip.Overview;

        public MembershipState Seen() => Copy(latestGossip: LatestGossip.Seen(SelfUniqueAddress));

        /// <summary>
        /// Checks if we have a cluster convergence. If there are any in data center node pairs that cannot reach each other
        /// then we can't have a convergence until those nodes reach each other again or one of them is downed
        /// </summary>
        /// <returns>true if convergence have been reached and false if not</returns>
        public bool Convergence(ImmutableHashSet<UniqueAddress> exitingConfirmed)
        {
            // If another member in the data center that is UP or LEAVING and has not seen this gossip or is exiting
            // convergence cannot be reached
            var memberHinderingConvergenceExists = Members.Any(member =>
                member.DataCenter == SelfDataCenter
                && (member.Status == MemberStatus.Up || member.Status == MemberStatus.Leaving)
                && !(LatestGossip.SeenByNode(member.UniqueAddress) || exitingConfirmed.Contains(member.UniqueAddress)));

            // Find cluster members in the data center that are unreachable from other members of the data center
            // excluding observations from members outside of the data center, that have status DOWN or is passed in as confirmed exiting.
            var unreachableInDc = DcReachabilityExcludingDownedObservers.AllUnreachableOrTerminated
                .Where(node => node != SelfUniqueAddress && !exitingConfirmed.Contains(node))
                .Select(node => LatestGossip.GetMember(node));

            // unreachables outside of the data center or with status DOWN or EXITING does not affect convergence
            var allUnreachablesCanBeIgnored = unreachableInDc.All(unreachable => unreachable.Status == MemberStatus.Down || unreachable.Status == MemberStatus.Exiting);
            return allUnreachablesCanBeIgnored && !memberHinderingConvergenceExists;
        }

        public ImmutableSortedSet<Member> DcMembers => LatestGossip.IsMultiDc ? Members.Where(m => m.DataCenter != SelfDataCenter).ToImmutableSortedSet(Member.AgeOrdering) : Members;

        public UniqueAddress Leader => LeaderOf(Members);

        public bool IsLeader(UniqueAddress node) => Leader == node;

        public UniqueAddress RoleLeader(string role) => LeaderOf(Members.Where(m => m.HasRole(role)));

        public UniqueAddress LeaderOf(IEnumerable<Member> members)
        {
            var reachability = DcReachability;
            var reachableInDc = reachability.IsAllReachable
                ? members.Where(m => m.DataCenter == SelfDataCenter && m.Status != MemberStatus.Down)
                : members.Where(m => m.DataCenter == SelfDataCenter && m.Status != MemberStatus.Down && (reachability.IsReachable(m.UniqueAddress) || m.UniqueAddress == SelfUniqueAddress));
            var reachableMembersInDc = reachableInDc.ToImmutableSortedSet(Member.LeaderStatusOrdering);
            if (reachableMembersInDc.IsEmpty) return null;
            else
            {
                var found = reachableMembersInDc
                    .FirstOrDefault(m => m.Status == MemberStatus.Up || m.Status == MemberStatus.Leaving) ?? reachableMembersInDc.First();
                return found.UniqueAddress;
            }
        }

        public bool IsInSameDc(UniqueAddress node) => node == SelfUniqueAddress || LatestGossip.GetMember(node).DataCenter == SelfDataCenter;

        public bool IsValidNodeForGossip(UniqueAddress node) =>
            // if cross DC we need to check pairwise unreachable observation
            node != SelfUniqueAddress && (IsInSameDc(node) && IsReachableExcludingDownedObservers(node)) || Overview.Reachability.IsReachable(SelfUniqueAddress, node);

        /// <summary>
        /// Returns true if toAddress should be reachable from the fromDc in general, within a data center
        /// this means only caring about data center local observations, across data centers it
        /// means caring about all observations for the toAddress.
        /// </summary>
        public bool IsReachableExcludingDownedObservers(UniqueAddress toAddress)
        {
            if (!LatestGossip.HasMember(toAddress)) return false;
            else
            {
                var to = LatestGossip.GetMember(toAddress);

                // if member is in the same data center, we ignore cross data center unreachability
                if (to.DataCenter == SelfDataCenter) return DcReachabilityExcludingDownedObservers.IsReachable(toAddress);

                // if not it is enough that any non-downed node observed it as unreachable
                return LatestGossip.ReachabilityExcludingDownedObservers.Value.IsReachable(toAddress);
            }
        }

        private MembershipState Copy(Gossip latestGossip = null,
            UniqueAddress selfUniqueAddress = null,
            string selfDataCenter = null,
            int? crossDataCenterConnections = null)
        {
            return new MembershipState(
                selfUniqueAddress: selfUniqueAddress ?? SelfUniqueAddress,
                latestGossip: latestGossip ?? LatestGossip,
                selfDataCenter: selfDataCenter ?? SelfDataCenter,
                crossDataCenterConnections: crossDataCenterConnections ?? CrossDataCenterConnections);
        }
    }
}