// //-----------------------------------------------------------------------
// // <copyright file="IMetricsCollector.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Metrics.Serialization;

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

    /// <summary>
    /// Base class that implements <see cref="IMetricsCollector"/> and requires valid constructor
    /// </summary>
    public abstract class MetricsCollectorBase : IMetricsCollector
    {
        /// <summary>
        /// Assigned actor system
        /// </summary>
        protected ActorSystem System { get; }

        public MetricsCollectorBase(ActorSystem system)
        {
            System = system;
        }
        
        /// <inheritdoc />
        public abstract void Dispose();

        /// <inheritdoc />
        public abstract NodeMetrics Sample();
    }
}