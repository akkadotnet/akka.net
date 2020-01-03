// //-----------------------------------------------------------------------
// // <copyright file="MetricsCollectorMock.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Metrics.Serialization;
using Google.Protobuf.WellKnownTypes;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Tests.Helpers
{
    /// <summary>
    /// Metrics collector mock implementation
    /// </summary>
    public class MetricsCollectorMock : MetricsCollectorBase
    {
        /// <inheritdoc />
        public MetricsCollectorMock(ActorSystem system) : base(system)
        {
        }

        /// <inheritdoc />
        public override NodeMetrics Sample()
        {
            return new NodeMetrics(new Address("akka", System.Name), DateTime.UtcNow.ToTimestamp().Seconds, new []
            {
                new NodeMetrics.Types.Metric("metric1", 10, new NodeMetrics.Types.EWMA(5, 0.5)) ,
                new NodeMetrics.Types.Metric("metric2", 10, new NodeMetrics.Types.EWMA(5, 0.2)), 
                new NodeMetrics.Types.Metric("metric3", 10, new NodeMetrics.Types.EWMA(5, 0.3)),
                new NodeMetrics.Types.Metric("metric4", 10, new NodeMetrics.Types.EWMA(5, 0.7))
            });
        }
        
        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }
}