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
        /// <summary>
        /// Test in cluster, with manual collection activation, collector mock, fast.
        /// </summary>
        public const string MockConfiguration = @"
            akka.cluster.metrics {
                  periodic-tasks-initial-delay = 100ms
                  collector {
                    enabled = off
                    sample-interval = 200ms
                    gossip-interval = 200ms
                    provider = ""Akka.Cluster.Metrics.Tests.Helpers.MetricsCollectorMock, Akka.Cluster.Metrics.Tests""
                    fallback = false
                }
            }
            akka.actor.provider = ""cluster""
        ";
        
        /// <inheritdoc />
        public MetricsCollectorMock(ActorSystem system) : base(system)
        {
        }

        /// <inheritdoc />
        public override NodeMetrics Sample()
        {
            return new NodeMetrics(new Address("akka", System.Name), DateTime.UtcNow.ToTimestamp().Seconds, new NodeMetrics.Types.Metric[]
            {
                new NodeMetrics.Types.Metric("metric", 10, new NodeMetrics.Types.EWMA(5, 0.5)) 
            });
        }
        
        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }
}