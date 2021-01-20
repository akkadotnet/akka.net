//-----------------------------------------------------------------------
// <copyright file="LeastShardAllocationStrategy.cs" company="Akka.NET Project">
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
    using ShardId = String;

    /// <summary>
    /// INTERNAL API: Use <see cref="ShardAllocationStrategy.LeastShardAllocationStrategy(int, double)"/> factory method.
    ///
    /// <see cref="IShardAllocationStrategy"/> that  allocates new shards to the <see cref="ShardRegion"/> (node) with least
    /// number of previously allocated shards.
    ///
    /// When a node is added to the cluster the shards on the existing nodes will be rebalanced to the new node.
    /// The <see cref="LeastShardAllocationStrategy"/> picks shards for rebalancing from the <see cref="ShardRegion"/>s with most number
    /// of previously allocated shards. They will then be allocated to the <see cref="ShardRegion"/> with least number of
    /// previously allocated shards, i.e. new members in the cluster. The amount of shards to rebalance in each
    /// round can be limited to make it progress slower since rebalancing too many shards at the same time could
    /// result in additional load on the system. For example, causing many Event Sourced entites to be started
    /// at the same time.
    ///
    /// It will not rebalance when there is already an ongoing rebalance in progress.
    /// </summary>
    [Serializable]
    internal class LeastShardAllocationStrategy : AbstractLeastShardAllocationStrategy
    {
        private static readonly Task<IImmutableSet<ShardId>> emptyRebalanceResult = Task.FromResult<IImmutableSet<ShardId>>(ImmutableHashSet<ShardId>.Empty);

        private readonly int _absoluteLimit;
        private readonly double _relativeLimit;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="absoluteLimit">The maximum number of shards that will be rebalanced in one rebalance round</param>
        /// <param name="relativeLimit">Fraction (&lt; 1.0) of total number of (known) shards that will be rebalanced in one rebalance round</param>
        public LeastShardAllocationStrategy(int absoluteLimit, double relativeLimit)
        {
            _absoluteLimit = absoluteLimit;
            _relativeLimit = relativeLimit;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentShardAllocations">TBD</param>
        /// <param name="rebalanceInProgress">TBD</param>
        /// <returns>TBD</returns>
        public override Task<IImmutableSet<ShardId>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<ShardId>> currentShardAllocations, IImmutableSet<ShardId> rebalanceInProgress)
        {
            int Limit(int numberOfShards)
            {
                return Math.Max(1, Math.Min((int)(_relativeLimit * numberOfShards), _absoluteLimit));
            }

            IImmutableSet<ShardId> RebalancePhase1(
                int numberOfShards,
                int optimalPerRegion,
                IImmutableList<RegionEntry> sortedEntries
                )
            {
                var selected = ImmutableList.CreateBuilder<ShardId>();

                foreach (var entry in sortedEntries)
                {
                    if (entry.ShardIds.Count > optimalPerRegion)
                    {
                        selected.AddRange(entry.ShardIds.Take(entry.ShardIds.Count - optimalPerRegion));
                    }
                }

                var result = selected.ToImmutable();
                return result.Take(Limit(numberOfShards)).ToImmutableHashSet();
            }

            Task<IImmutableSet<ShardId>> RebalancePhase2(
                int numberOfShards,
                int optimalPerRegion,
                IImmutableList<RegionEntry> sortedEntries)
            {
                // In the first phase the optimalPerRegion is rounded up, and depending on number of shards per region and number
                // of regions that might not be the exact optimal.
                // In second phase we look for diff of >= 2 below optimalPerRegion and rebalance that number of shards.
                var countBelowOptimal = sortedEntries.Select(entry => Math.Max(0, (optimalPerRegion - 1) - entry.ShardIds.Count)).Sum();

                if (countBelowOptimal == 0)
                {
                    return emptyRebalanceResult;
                }
                else
                {
                    var selected = ImmutableList.CreateBuilder<ShardId>();
                    foreach (var entry in sortedEntries)
                    {
                        if (entry.ShardIds.Count >= optimalPerRegion)
                        {
                            selected.Add(entry.ShardIds.First());
                        }
                    }

                    var result = selected.ToImmutable().Take(Math.Min(countBelowOptimal, Limit(numberOfShards))).ToImmutableHashSet();
                    return Task.FromResult<IImmutableSet<ShardId>>(result);
                }
            }

            if (rebalanceInProgress.Count > 0)
            {
                // one rebalance at a time
                return emptyRebalanceResult;
            }
            else
            {
                var sortedRegionEntries = RegionEntriesFor(currentShardAllocations).OrderBy(i => i, ShardSuitabilityOrdering.Instance).ToImmutableList();
                if (!IsAGoodTimeToRebalance(sortedRegionEntries))
                {
                    return emptyRebalanceResult;
                }
                else
                {
                    var numberOfShards = sortedRegionEntries.Select(i => i.ShardIds.Count).Sum();
                    var numberOfRegions = sortedRegionEntries.Count;
                    if (numberOfRegions == 0 || numberOfShards == 0)
                    {
                        return emptyRebalanceResult;
                    }
                    else
                    {
                        var optimalPerRegion = numberOfShards / numberOfRegions + ((numberOfShards % numberOfRegions == 0) ? 0 : 1);

                        var result1 = RebalancePhase1(numberOfShards, optimalPerRegion, sortedRegionEntries);

                        if (result1.Count > 0)
                        {
                            return Task.FromResult<IImmutableSet<ShardId>>(result1);
                        }
                        else
                        {
                            return RebalancePhase2(numberOfShards, optimalPerRegion, sortedRegionEntries);
                        }
                    }
                }
            }
        }

        public override string ToString()
        {
            return $"LeastShardAllocationStrategy({_absoluteLimit},{_relativeLimit})";
        }
    }
}
