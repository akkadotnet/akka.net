//-----------------------------------------------------------------------
// <copyright file="ShardAllocationStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /// <summary>
    /// Interface of the pluggable shard allocation and rebalancing logic used by the <see cref="PersistentShardCoordinator"/>.
    /// </summary>
    public interface IShardAllocationStrategy : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Invoked when the location of a new shard is to be decided.
        /// </summary>
        /// <param name="requester">
        /// Actor reference to the <see cref="ShardRegion"/> that requested the location of the shard, can be returned
        /// if preference should be given to the node where the shard was first accessed.
        /// </param>
        /// <param name="shardId">The id of the shard to allocate.</param>
        /// <param name="currentShardAllocations">
        /// All actor refs to <see cref="ShardRegion"/> and their current allocated shards, in the order they were allocated
        /// </param>
        /// <returns>
        /// <see cref="Task"/> of the actor ref of the <see cref="ShardRegion"/> that is to be responsible for the shard,
        /// must be one of the references included in the <paramref name="currentShardAllocations"/> parameter.
        /// </returns>
        Task<IActorRef> AllocateShard(IActorRef requester, ShardId shardId, IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations);

        /// <summary>
        /// Invoked periodically to decide which shards to rebalance to another location.
        /// </summary>
        /// <param name="currentShardAllocations">
        /// All actor refs to <see cref="ShardRegion"/> and their current allocated shards, in the order they were allocated.
        /// </param>
        /// <param name="rebalanceInProgress">
        /// Set of shards that are currently being rebalanced, i.e. you should not include these in the returned set.
        /// </param>
        /// <returns><see cref="Task"/> of the shards to be migrated, may be empty to skip rebalance in this round. </returns>
        Task<IImmutableSet<ShardId>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations, IImmutableSet<ShardId> rebalanceInProgress);
    }

    /// <summary>
    /// The default implementation of <see cref="Akka.Cluster.Sharding.LeastShardAllocationStrategy"/> allocates new shards
    /// to the <see cref="ShardRegion"/> with least number of previously allocated shards.
    ///
    /// When a node is added to the cluster the shards on the existing nodes will be rebalanced to the new node.
    /// evenly spread on the remaining nodes (by picking regions with least shards).
    ///
    /// When a node is added to the cluster the shards on the existing nodes will be rebalanced to the new node.
    /// It picks shards for rebalancing from the `ShardRegion` with most number of previously allocated shards.
    ///
    /// They will then be allocated to the <see cref="ShardRegion"/> with least number of previously allocated shards,
    /// i.e. new members in the cluster. There is a configurable threshold of how large the difference
    /// must be to begin the rebalancing.The difference between number of shards in the region with most shards and
    /// the region with least shards must be greater than the `rebalanceThreshold` for the rebalance to occur.
    ///
    /// A `rebalanceThreshold` of 1 gives the best distribution and therefore typically the best choice.
    /// A higher threshold means that more shards can be rebalanced at the same time instead of one-by-one.
    /// That has the advantage that the rebalance process can be quicker but has the drawback that the
    /// the number of shards (and therefore load) between different nodes may be significantly different.
    /// Given the recommendation of using 10x shards than number of nodes and `rebalanceThreshold=10` can result
    /// in one node hosting ~2 times the number of shards of other nodes.Example: 1000 shards on 100 nodes means
    /// 10 shards per node.One node may have 19 shards and others 10 without a rebalance occurring.
    ///
    /// The number of ongoing rebalancing processes can be limited by `maxSimultaneousRebalance`.
    /// </summary>
    [Serializable]
    public class LeastShardAllocationStrategy : IShardAllocationStrategy
    {
        private readonly int _rebalanceThreshold;
        private readonly int _maxSimultaneousRebalance;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="rebalanceThreshold">TBD</param>
        /// <param name="maxSimultaneousRebalance">TBD</param>
        public LeastShardAllocationStrategy(int rebalanceThreshold, int maxSimultaneousRebalance)
        {
            _rebalanceThreshold = rebalanceThreshold;
            _maxSimultaneousRebalance = maxSimultaneousRebalance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="requester">TBD</param>
        /// <param name="shardId">TBD</param>
        /// <param name="currentShardAllocations">TBD</param>
        /// <returns>TBD</returns>
        public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations)
        {
            var min = currentShardAllocations.OrderBy(i => i.Value.Count).FirstOrDefault();
            return Task.FromResult(min.Key);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentShardAllocations">TBD</param>
        /// <param name="rebalanceInProgress">TBD</param>
        /// <returns>TBD</returns>
        public Task<IImmutableSet<ShardId>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations, IImmutableSet<ShardId> rebalanceInProgress)
        {
            if (rebalanceInProgress.Count < _maxSimultaneousRebalance)
            {
                var leastShardsRegion = currentShardAllocations.OrderBy(i => i.Value.Count).FirstOrDefault();
                var mostShards = currentShardAllocations.Select(kv => kv.Value.Where(s => !rebalanceInProgress.Contains(s))).OrderByDescending(i => i.Count()).FirstOrDefault()?.ToArray();

                var difference = mostShards.Length - leastShardsRegion.Value.Count;
                if (difference >= _rebalanceThreshold)
                {
                    var n = Math.Min(
                        Math.Min(difference - _rebalanceThreshold, _rebalanceThreshold),
                        _maxSimultaneousRebalance - rebalanceInProgress.Count);

                    return Task.FromResult<IImmutableSet<ShardId>>(mostShards.OrderBy(i => i).Take(n).ToImmutableHashSet());
                }
            }

            return Task.FromResult<IImmutableSet<ShardId>>(ImmutableHashSet<ShardId>.Empty);
        }
    }
}
