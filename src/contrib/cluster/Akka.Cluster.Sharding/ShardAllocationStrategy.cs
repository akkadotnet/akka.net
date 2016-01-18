//-----------------------------------------------------------------------
// <copyright file="ShardAllocationStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// to the <see cref="ShardRegion"/> with least number of previously allocated shards. It picks shards 
    /// for rebalancing handoff from the <see cref="ShardRegion"/> with most number of previously allocated shards.
    /// They will then be allocated to the <see cref="ShardRegion"/> with least number of previously allocated shards,
    /// i.e. new members in the cluster. There is a configurable threshold of how large the difference
    /// must be to begin the rebalancing. The number of ongoing rebalancing processes can be limited.
    /// </summary>
    [Serializable]
    public class LeastShardAllocationStrategy : IShardAllocationStrategy
    {
        private readonly int _rebalanceThreshold;
        private readonly int _maxSimultaneousRebalance;

        public LeastShardAllocationStrategy(int rebalanceThreshold, int maxSimultaneousRebalance)
        {
            _rebalanceThreshold = rebalanceThreshold;
            _maxSimultaneousRebalance = maxSimultaneousRebalance;
        }

        public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations)
        {
            var min = GetMinBy(currentShardAllocations, kv => kv.Value.Count);
            return Task.FromResult(min.Key);
        }

        public Task<IImmutableSet<ShardId>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations, IImmutableSet<ShardId> rebalanceInProgress)
        {
            if (rebalanceInProgress.Count < _maxSimultaneousRebalance)
            {
                var leastShardsRegion = GetMinBy(currentShardAllocations, kv => kv.Value.Count);
                var shards =
                    currentShardAllocations.Select(kv => kv.Value.Where(s => !rebalanceInProgress.Contains(s)).ToArray());
                var mostShards = GetMaxBy(shards, x => x.Length);

                if (mostShards.Length - leastShardsRegion.Value.Count >= _rebalanceThreshold)
                {
                    return Task.FromResult<IImmutableSet<ShardId>>(ImmutableHashSet.Create(mostShards.First()));
                }
            }

            return Task.FromResult<IImmutableSet<ShardId>>(ImmutableHashSet<ShardId>.Empty);
        }

        private static T GetMinBy<T>(IEnumerable<T> collection, Func<T, int> extractor)
        {
            var minSize = int.MaxValue;
            var result = default(T);
            foreach (var value in collection)
            {
                var x = extractor(value);
                if (x < minSize)
                {
                    minSize = x;
                    result = value;
                }
            }
            return result;
        }

        private static T GetMaxBy<T>(IEnumerable<T> collection, Func<T, int> extractor)
        {
            var minSize = int.MinValue;
            var result = default(T);
            foreach (var value in collection)
            {
                var x = extractor(value);
                if (x > minSize)
                {
                    minSize = x;
                    result = value;
                }
            }
            return result;
        }
    }
}