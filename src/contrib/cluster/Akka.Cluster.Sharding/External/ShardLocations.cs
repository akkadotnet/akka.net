//-----------------------------------------------------------------------
// <copyright file="ShardLocations.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;

namespace Akka.Cluster.Sharding.External
{
    using ShardId = String;

    public sealed class ShardLocations
    {
        public ShardLocations(IImmutableDictionary<ShardId, ExternalShardAllocationStrategy.ShardLocation> locations)
        {
            Locations = locations;
        }

        public IImmutableDictionary<string, ExternalShardAllocationStrategy.ShardLocation> Locations { get; }
    }
}
