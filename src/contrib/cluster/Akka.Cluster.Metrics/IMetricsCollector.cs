// //-----------------------------------------------------------------------
// // <copyright file="IMetricsCollector.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// Metrics sampler.
    ///
    /// Implementations of cluster system metrics collectors extend this interface.
    /// </summary>
    public interface IMetricsCollector : IDisposable
    {
        /// <summary>
        /// Samples and collects new data points.
        /// This method is invoked periodically and should return current metrics for this node.
        /// </summary>
        NodeMetrics Sample();
    }
}