//-----------------------------------------------------------------------
// <copyright file="AbstractLeastShardAllocationStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Sharding.Internal
{
    using static Akka.Cluster.ClusterEvent;
    using ShardId = String;

    /// <summary>
    /// Common logic for the least shard allocation strategy implementations
    /// </summary>
    public abstract class AbstractLeastShardAllocationStrategy : IActorSystemDependentAllocationStrategy
    {
        private static readonly ImmutableHashSet<MemberStatus> JoiningCluster = ImmutableHashSet.Create(MemberStatus.Joining, MemberStatus.WeaklyUp);
        private static readonly ImmutableHashSet<MemberStatus> LeavingClusterStatuses = ImmutableHashSet.Create(MemberStatus.Leaving, MemberStatus.Exiting, MemberStatus.Down);

        public sealed class RegionEntry
        {
            public RegionEntry(IActorRef region, Member member, IImmutableList<ShardId> shardIds)
            {
                Region = region;
                Member = member;
                ShardIds = shardIds;
            }

            public IActorRef Region { get; }
            public Member Member { get; }
            public IImmutableList<string> ShardIds { get; }
        }

        internal class ShardSuitabilityOrdering : IComparer<RegionEntry>
        {
            public static readonly ShardSuitabilityOrdering Instance = new ShardSuitabilityOrdering();

            private ShardSuitabilityOrdering()
            {

            }

            public int Compare(RegionEntry x, RegionEntry y)
            {
                if (x.Member.Status != y.Member.Status)
                {
                    // prefer allocating to nodes that are not on their way out of the cluster
                    var xIsLeaving = LeavingClusterStatuses.Contains(x.Member.Status);
                    var yIsLeaving = LeavingClusterStatuses.Contains(y.Member.Status);
                    return xIsLeaving.CompareTo(yIsLeaving);
                }
                else if (x.Member.AppVersion != y.Member.AppVersion)
                {
                    // prefer nodes with the highest rolling update app version
                    return y.Member.AppVersion.CompareTo(x.Member.AppVersion);
                }
                else
                {
                    // prefer the node with the least allocated shards
                    return x.ShardIds.Count.CompareTo(y.ShardIds.Count);
                }
            }
        }

        private ActorSystem system;
        private Cluster cluster;

        // protected for testability
        protected virtual CurrentClusterState ClusterState => cluster.State;
        protected virtual Member SelfMember => cluster.SelfMember;

        public void Start(ActorSystem system)
        {
            this.system = system;
            cluster = Cluster.Get(system);
        }

        public async Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations)
        {
            var regionEntries = RegionEntriesFor(currentShardAllocations);
            if (regionEntries.IsEmpty)
            {
                // very unlikely to ever happen but possible because of cluster state view not yet updated when collecting
                // region entries, view should be updated after a very short time
                await Task.Delay(50);
                return await AllocateShard(requester, shardId, currentShardAllocations);
            }
            else
            {
                var suitable = MostSuitableRegion(regionEntries);
                return suitable.Region;
            }
        }

        protected bool IsAGoodTimeToRebalance(IEnumerable<RegionEntry> regionEntries)
        {
            // Avoid rebalance when rolling update is in progress
            // (This will ignore versions on members with no shard regions, because of sharding role or not yet completed joining)
            var region = regionEntries.FirstOrDefault();
            if (region == null)
                return false; // empty list of regions, probably not a good time to rebalance...
            var allNodesSameVersion = regionEntries.All(r => r.Member.AppVersion == region.Member.AppVersion);
            // Rebalance requires ack from regions and proxies - no need to rebalance if it cannot be completed
            // FIXME #29589, we currently only look at same dc but proxies in other dcs may delay complete as well right now
            var neededMembersReachable = !ClusterState.Members.Any(m => ClusterState.Unreachable.Contains(m));
            // No members in same dc joining, we want that to complete before rebalance, such nodes should reach Up soon
            var membersInProgressOfJoining =
                ClusterState.Members.Any(m => JoiningCluster.Contains(m.Status));

            return allNodesSameVersion && neededMembersReachable && !membersInProgressOfJoining;
        }

        protected (IActorRef Region, IImmutableList<ShardId> Shards) MostSuitableRegion(
            IEnumerable<RegionEntry> regionEntries)
        {
            var mostSuitableEntry = regionEntries.Min(ShardSuitabilityOrdering.Instance);
            return (mostSuitableEntry.Region, mostSuitableEntry.ShardIds);
        }

        protected ImmutableList<RegionEntry> RegionEntriesFor(IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations)
        {
            var addressToMember = ClusterState.Members.ToImmutableDictionary(m => m.Address, m => m);

            return currentShardAllocations.Select(i =>
            {
                var regionAddress = i.Key.Path.Address.HasLocalScope ? SelfMember.Address : i.Key.Path.Address;

                var memberForRegion = addressToMember.GetValueOrDefault(regionAddress);
                // if the member is unknown (very unlikely but not impossible) because of view not updated yet
                // that node is ignored for this invocation
                if (memberForRegion != null)
                    return new RegionEntry(i.Key, memberForRegion, i.Value);
                return null;
            }).Where(i => i != null).ToImmutableList();
        }

        public abstract Task<IImmutableSet<string>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations, IImmutableSet<string> rebalanceInProgress);
    }
}
