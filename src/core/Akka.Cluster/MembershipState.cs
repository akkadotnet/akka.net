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
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    internal sealed class MembershipState
    {
        // this must be publication even thou we don't share state of the fields - lazy deadlock detector can give false negatives
        private const LazyThreadSafetyMode LazySafety = LazyThreadSafetyMode.PublicationOnly;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLeaderMemberStatus(MemberStatus status) => status == MemberStatus.Up || status == MemberStatus.Leaving;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsConvergenceMemberStatus(MemberStatus status) => status == MemberStatus.Up || status == MemberStatus.Leaving;

        /// <summary>
        /// If there are unreachable members in the cluster with any of these statuses, they will be skipped during convergence checks.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConvergenceSkipUnreachableWithMemberStatus(MemberStatus status) => status == MemberStatus.Down || status == MemberStatus.Exiting;

        /// <summary>
        /// If there are unreachable members in the cluster with any of these statuses, they will be pruned from the local gossip
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRemoveUnreachableWithMemberStatus(MemberStatus status) => status == MemberStatus.Down || status == MemberStatus.Exiting;

        public Gossip LatestGossip { get; }
        public UniqueAddress SelfUniqueAddress { get; }
        public string SelfDataCenter { get; }
        public int CrossDataCenterConnections { get; }

        private readonly Lazy<Member> _selfMember;
        private readonly Lazy<Reachability> _dcReachability;
        private readonly Lazy<Reachability> _dcReachabilityWithoutObservationsWithin;
        private readonly Lazy<Reachability> _dcReachabilityExcludingDownedObservers;
        private readonly Lazy<Reachability> _dcReachabilityNoOutsideNodes;
        private readonly Lazy<ImmutableDictionary<string, ImmutableSortedSet<Member>>> _ageSortedTopOldestMembersPerDc;

        public MembershipState(Gossip latestGossip, UniqueAddress selfUniqueAddress, string selfDataCenter, int crossDataCenterConnections)
        {
            LatestGossip = latestGossip;
            SelfUniqueAddress = selfUniqueAddress;
            SelfDataCenter = selfDataCenter;
            CrossDataCenterConnections = crossDataCenterConnections;

            _selfMember = new Lazy<Member>(() => LatestGossip.GetMember(SelfUniqueAddress), LazySafety);
            _dcReachability = new Lazy<Reachability>(() => 
                Overview.Reachability.RemoveObservers(
                    Members
                    .Where(m => m.DataCenter != SelfDataCenter)
                    .Select(m => m.UniqueAddress)
                    .ToImmutableHashSet()), LazySafety);
            
            _dcReachabilityWithoutObservationsWithin = new Lazy<Reachability>(() => 
                DcReachability.FilterRecords(r => LatestGossip.GetMember(r.Subject).DataCenter != SelfDataCenter), LazySafety);
            
            _dcReachabilityExcludingDownedObservers = new Lazy<Reachability>(() =>
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

                return Overview.Reachability
                    .RemoveObservers(membersToExclude.ToImmutable())
                    .Remove(nonDcMembers);
            }, LazySafety);
            
            _dcReachabilityNoOutsideNodes = new Lazy<Reachability>(() => 
                Overview.Reachability.Remove(
                    Members
                        .Where(m => m.DataCenter != SelfDataCenter)
                        .Select(m => m.UniqueAddress)), LazySafety);
            
            _ageSortedTopOldestMembersPerDc = new Lazy<ImmutableDictionary<string, ImmutableSortedSet<Member>>>(() =>
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

                return acc.ToImmutable();
            }, LazySafety);
        }

        public Member SelfMember => _selfMember.Value;

        /// <summary>
        /// Reachability excluding observations from nodes outside of the data center, 
        /// but including observed unreachable nodes outside of the data center.
        /// </summary>
        public Reachability DcReachability => _dcReachability.Value;

        /// <summary>
        /// Reachability excluding observations from nodes outside of the data center 
        /// and observations within self data center, but including observed unreachable 
        /// nodes outside of the data center.
        /// </summary>
        public Reachability DcReachabilityWithoutObservationsWithin => _dcReachabilityWithoutObservationsWithin.Value;

        /// <summary>
        /// Reachability for data center nodes, with observations from outside the data center or from downed nodes filtered out.
        /// </summary>
        public Reachability DcReachabilityExcludingDownedObservers => _dcReachabilityExcludingDownedObservers.Value;

        public Reachability DcReachabilityNoOutsideNodes => _dcReachabilityNoOutsideNodes.Value;

        /// <summary>
        /// Up to <see cref="CrossDataCenterConnections"/> number of the oldest members for each DC.
        /// </summary>
        public ImmutableDictionary<string, ImmutableSortedSet<Member>> AgeSortedTopOldestMembersPerDc =>
            _ageSortedTopOldestMembersPerDc.Value;

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
                && IsConvergenceMemberStatus(member.Status)
                && !(LatestGossip.SeenByNode(member.UniqueAddress) || exitingConfirmed.Contains(member.UniqueAddress)));

            // Find cluster members in the data center that are unreachable from other members of the data center
            // excluding observations from members outside of the data center, that have status DOWN or is passed in as confirmed exiting.
            var unreachableInDc = DcReachabilityExcludingDownedObservers.AllUnreachableOrTerminated
                .Where(node => node != SelfUniqueAddress && !exitingConfirmed.Contains(node))
                .Select(node => LatestGossip.GetMember(node));

            // unreachables outside of the data center or with status DOWN or EXITING does not affect convergence
            var allUnreachablesCanBeIgnored = unreachableInDc.All(unreachable => IsConvergenceSkipUnreachableWithMemberStatus(unreachable.Status));
            return allUnreachablesCanBeIgnored && !memberHinderingConvergenceExists;
        }

        public ImmutableSortedSet<Member> DcMembers => LatestGossip.IsMultiDc ? Members.Where(m => m.DataCenter != SelfDataCenter).ToImmutableSortedSet(Member.AgeOrdering) : Members;

        public UniqueAddress Leader => LeaderOf(Members);

        public Member YoungestMember => DcMembers.MaxBy(m => m.UpNumber == int.MaxValue ? 0 : m.UpNumber);

        public bool IsLeader(UniqueAddress node) => Leader == null || Leader == node;

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
                    .FirstOrDefault(m => IsLeaderMemberStatus(m.Status)) ?? reachableMembersInDc.Min(Member.LeaderStatusOrdering);
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

        internal MembershipState Copy(Gossip latestGossip = null,
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

        public override string ToString() => $"MembershipState(selfDC: {SelfDataCenter}, selfAddress: {SelfUniqueAddress}, selfMember: {SelfMember}, latestGossip: {LatestGossip}, reachability: {DcReachability})";
    }

    internal class GossipTargetSelector
    {
        public double ReduceGossipDifferentViewProbability { get; }
        public double CrossDcGossipProbability { get; }

        public GossipTargetSelector(double reduceGossipDifferentViewProbability, double crossDcGossipProbability)
        {
            ReduceGossipDifferentViewProbability = reduceGossipDifferentViewProbability;
            CrossDcGossipProbability = crossDcGossipProbability;
        }

        public UniqueAddress GossipTarget(MembershipState state) => SelectRandomNode(GossipTargets(state));

        public UniqueAddress[] GossipTargets(MembershipState state) => state.LatestGossip.IsMultiDc ? MultiDcGossipTargets(state) : LocalDcGossipTargets(state);

        /// <summary>
        /// Select <paramref name="n"/> random nodes to gossip to (used to quickly inform the rest of the cluster when leaving for example)
        /// </summary>
        public IEnumerable<UniqueAddress> RandomNodesForFullGossip(MembershipState state, int n)
        {
            UniqueAddress SelectOtherDcNode(string[] dcs)
            {
                foreach (var dc in dcs)
                foreach (var member in state.AgeSortedTopOldestMembersPerDc[dc])
                {
                    if (state.IsValidNodeForGossip(member.UniqueAddress)) return member.UniqueAddress;
                }

                return null;
            }

            var randomizedNodes = state.Members.ToArray();
            randomizedNodes.Shuffle();

            if (state.LatestGossip.IsMultiDc && state.AgeSortedTopOldestMembersPerDc[state.SelfDataCenter].Contains(state.SelfMember))
            {
                var randomizedDcs = state.AgeSortedTopOldestMembersPerDc.Keys.Except(new []{ state.SelfDataCenter }).ToArray();
                randomizedDcs.Shuffle();

                var otherDc = SelectOtherDcNode(randomizedDcs);
                if (otherDc != null) n--; // we'll defer yield of this one at the end

                // this node is one of the N oldest in the cluster, gossip to one cross-dc but mostly locally
                foreach (var member in randomizedNodes)
                {
                    if (member.DataCenter == state.SelfDataCenter && state.IsValidNodeForGossip(member.UniqueAddress))
                    {
                        yield return member.UniqueAddress;
                        n--;
                        if (n == 0) yield break;
                    }
                }

                if (otherDc != null) yield return otherDc;
            }
            else
            {
                // single dc or not among the N oldest - select local nodes
                foreach (var member in randomizedNodes)
                {
                    if (member.DataCenter == state.SelfDataCenter && state.IsValidNodeForGossip(member.UniqueAddress))
                    {
                        yield return member.UniqueAddress;
                        n--;
                        if (n == 0) yield break;
                    }
                }
            }
        }


        /// <summary>
        /// Chooses a set of possible gossip targets that is in the same dc. If the cluster is not multi dc this
        /// means it is a choice among all nodes of the cluster.
        /// </summary>
        public UniqueAddress[] LocalDcGossipTargets(MembershipState state)
        {
            var latestGossip = state.LatestGossip;
            var firstSelection = PreferNodesWithDifferentView(state)
                // If it's time to try to gossip to some nodes with a different view
                // gossip to a random alive same dc member with preference to a member with older gossip version
                ? latestGossip.Members
                    .Where(m => m.DataCenter == state.SelfDataCenter && !latestGossip.SeenByNode(m.UniqueAddress) &&
                                state.IsValidNodeForGossip(m.UniqueAddress))
                    .Select(m => m.UniqueAddress)
                    .ToArray()
                : new UniqueAddress[0];

            // Fall back to localGossip
            if (firstSelection.Length == 0)
                return state.LatestGossip.Members
                    .Where(m => m.DataCenter == state.SelfDataCenter && state.IsValidNodeForGossip(m.UniqueAddress))
                    .Select(m => m.UniqueAddress).ToArray();
            else return firstSelection;
        }

        /// <summary>
        /// Choose cross-dc nodes if this one of the N oldest nodes, and if not fall back to gossip locally in the DC.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public UniqueAddress[] MultiDcGossipTargets(MembershipState state)
        {
            // only a fraction of the time across data centers
            if (SelectDcLocalNodes(state)) return LocalDcGossipTargets(state);
            else
            {
                var nodesPerDc = state.AgeSortedTopOldestMembersPerDc;

                // only do cross DC gossip if this node is among the N oldest
                if (!nodesPerDc[state.SelfDataCenter].Contains(state.SelfMember)) return LocalDcGossipTargets(state);
                else
                {
                    IEnumerable<UniqueAddress> FindFirstDcWithValidNodes(string[] dcs)
                    {
                        foreach (var dc in dcs)
                        foreach (var member in nodesPerDc[dc])
                        {
                            if (state.IsValidNodeForGossip(member.UniqueAddress)) yield return member.UniqueAddress;
                        }
                    }

                    // chose another DC at random
                    var otherDcsInRandomOrder = DcsInRandomOrder(nodesPerDc.Remove(state.SelfDataCenter).Keys.ToArray());
                    var nodes = FindFirstDcWithValidNodes(otherDcsInRandomOrder).ToArray();
                    return nodes.Length != 0 ? nodes : LocalDcGossipTargets(state); // no other dc with reachable nodes, fall back to local gossip
                }
            }
        }

        /// <summary>
        /// For large clusters we should avoid shooting down individual
        /// nodes. Therefore the probability is reduced for large clusters.
        /// </summary>
        public double AdjustedGossipDifferentViewProbability(int clusterSize)
        {
            var low = ReduceGossipDifferentViewProbability;
            var high = low * 3;
            // start reduction when cluster is larger than configured ReduceGossipDifferentViewProbability
            if (clusterSize <= low)
                return ReduceGossipDifferentViewProbability;
            else
            {
                // don't go lower than 1/10 of the configured GossipDifferentViewProbability
                var minP = ReduceGossipDifferentViewProbability / 10;
                if (clusterSize >= high) return minP;
                else
                {
                    // linear reduction of the probability with increasing number of nodes
                    // from ReduceGossipDifferentViewProbability at ReduceGossipDifferentViewProbability nodes
                    // to ReduceGossipDifferentViewProbability / 10 at ReduceGossipDifferentViewProbability * 3 nodes
                    // i.e. default from 0.8 at 400 nodes, to 0.08 at 1600 nodes
                    var k = (minP - ReduceGossipDifferentViewProbability) / (high - low);
                    return ReduceGossipDifferentViewProbability + (clusterSize - low) * k;
                }
            }
        }

        /// <summary>
        /// For small DCs prefer cross DC gossip. This speeds up the bootstrapping of
        /// new DCs as adding an initial node means it has no local peers.
        /// Once the DC is at 5 members use the configured crossDcGossipProbability, before
        /// that for a single node cluster use 1.0, two nodes use 0.75 etc
        /// </summary>
        protected bool SelectDcLocalNodes(MembershipState state)
        {
            var localMembers = state.DcMembers.Count;
            var probability = localMembers > 4
                ? CrossDcGossipProbability
                // don't go below the configured probability
                : Math.Max((5 - localMembers) * 0.25, CrossDcGossipProbability);
            return ThreadLocalRandom.Current.NextDouble() > probability;
        }

        protected bool PreferNodesWithDifferentView(MembershipState state) => 
            ThreadLocalRandom.Current.NextDouble() < AdjustedGossipDifferentViewProbability(state.LatestGossip.Members.Count);

        protected string[] DcsInRandomOrder(string[] dcs)
        {
            var result = new string[dcs.Length];
            dcs.CopyTo(result, 0);
            result.Shuffle();
            return result;
        }

        protected UniqueAddress SelectRandomNode(UniqueAddress[] nodes) => nodes.Length == 0 ? null : nodes[ThreadLocalRandom.Current.Next(nodes.Length)];
    }
}