//-----------------------------------------------------------------------
// <copyright file="ShardingSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Serialization;

namespace Akka.Cluster.Sharding.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Akka.Cluster.Sharding's reference.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class ShardingSerializers : ModuleSerializers
    {
        // the constructor reflection picks for reference.conf; ClusterShardingMessageSerializer has one, so it always gets that one
        public override IReadOnlyList<BuiltInSerializer> Serializers { get; } = new[]
        {
            new BuiltInSerializer("akka-sharding", typeof(ClusterShardingMessageSerializer), (system, _) => new ClusterShardingMessageSerializer(system),
                new[] { typeof(IClusterShardingSerializable) }),
        };
    }
}
