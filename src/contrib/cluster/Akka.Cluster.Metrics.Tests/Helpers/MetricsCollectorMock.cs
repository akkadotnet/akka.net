//-----------------------------------------------------------------------
// <copyright file="MetricsCollectorMock.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    public class MetricsCollectorMock : IMetricsCollector
    {
        private readonly ActorSystem _system;
        private readonly Random _random;
        
        public MetricsCollectorMock(ActorSystem system)
        {
            _system = system;
            _random = new Random();
        }

        /// <inheritdoc />
        public NodeMetrics Sample()
        {
            return new NodeMetrics(new Address("akka", _system.Name), DateTime.UtcNow.ToTimestamp().Seconds, new []
            {
                new NodeMetrics.Types.Metric("metric1", _random.Next(0, 100), new NodeMetrics.Types.EWMA(5, 0.5)),
                new NodeMetrics.Types.Metric("metric2", _random.Next(0, 100), new NodeMetrics.Types.EWMA(5, 0.2)), 
                new NodeMetrics.Types.Metric("metric3", _random.Next(0, 100), new NodeMetrics.Types.EWMA(5, 0.3)),
                new NodeMetrics.Types.Metric("metric4", _random.Next(0, 100), new NodeMetrics.Types.EWMA(5, 0.7))
            });
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
