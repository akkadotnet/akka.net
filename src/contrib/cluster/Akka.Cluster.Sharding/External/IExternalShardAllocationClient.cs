//-----------------------------------------------------------------------
// <copyright file="IExternalShardAllocationClient.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Sharding.External
{
    using ShardId = String;

    /// <summary>
    /// API May Change
    /// Not for user extension
    /// </summary>
    public interface IExternalShardAllocationClient
    {

        /// <summary>
        /// Update the given shard's location. The [[Address]] should
        /// match one of the nodes in the cluster. If the node has not joined
        /// the cluster yet it will be moved to that node after the first cluster
        /// sharding rebalance it does.
        /// </summary>
        /// <param name="shard">The shard identifier</param>
        /// <param name="location">Location (akka node) to allocate the shard to</param>
        /// <returns>Confirmation that the update has been propagated to a majority of cluster nodes</returns>
        Task<Done> UpdateShardLocation(ShardId shard, Address location);

        /// <summary>
        /// Update all of the provided ShardLocations.
        /// The [[Address]] should match one of the nodes in the cluster. If the node has not joined
        /// the cluster yet it will be moved to that node after the first cluster
        /// sharding rebalance it does.
        /// </summary>
        /// <param name="locations">to update</param>
        /// <returns>Confirmation that the update has been propagates to a majority of cluster nodes</returns>
        Task<Done> UpdateShardLocations(IImmutableDictionary<ShardId, Address> locations);

        /// <summary>
        /// Get all the current shard locations that have been set via updateShardLocation
        /// </summary>
        /// <returns></returns>
        Task<ShardLocations> ShardLocations();
    }
}
