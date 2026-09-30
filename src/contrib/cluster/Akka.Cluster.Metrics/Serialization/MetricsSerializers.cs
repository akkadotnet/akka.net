//-----------------------------------------------------------------------
// <copyright file="MetricsSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Serialization;

namespace Akka.Cluster.Metrics.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Akka.Cluster.Metrics' reference.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class MetricsSerializers : ModuleSerializers
    {
        // the constructor reflection picks for reference.conf; ClusterMetricsMessageSerializer has one, so it always gets that one
        public override IReadOnlyList<ModuleSerializer> Serializers { get; } = new[]
        {
            new ModuleSerializer(typeof(ClusterMetricsMessageSerializer), (system, _) => new ClusterMetricsMessageSerializer(system)),
        };

        public override IReadOnlyList<Type> BoundTypes { get; } = new[]
        {
            typeof(MetricsGossipEnvelope),
            typeof(AdaptiveLoadBalancingPool),
            typeof(MixMetricsSelector),
            typeof(CpuMetricsSelector),
            typeof(MemoryMetricsSelector),
        };
    }
}
