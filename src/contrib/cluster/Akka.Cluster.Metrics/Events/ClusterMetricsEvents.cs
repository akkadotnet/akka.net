//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsEvents.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Cluster.Metrics.Serialization;

namespace Akka.Cluster.Metrics.Events
{
    /// <summary>
    /// Local cluster metrics extension events.
    ///
    /// Published to local event bus subscribers by <see cref="ClusterMetricsCollector"/>
    /// </summary>
    public interface IClusterMetricsEvent
    {
    }

    /// <summary>
    /// Current snapshot of cluster node metrics.
    /// </summary>
    public sealed class ClusterMetricsChanged : IClusterMetricsEvent
    {
        /// <summary>
        /// Current snapshot of cluster node metrics.
        /// </summary>
        public IImmutableSet<NodeMetrics> NodeMetrics { get; }

        public ClusterMetricsChanged(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            NodeMetrics = nodeMetrics;
        }
    }
}
