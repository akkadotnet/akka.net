// //-----------------------------------------------------------------------
// // <copyright file="DummyCollector.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Cluster.Metrics.Serialization;

namespace Akka.Cluster.Metrics.Collectors
{
    internal class DummyCollector : IMetricsCollector
    {
        /// <inheritdoc />
        public void Dispose()
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public NodeMetrics Sample()
        {
            throw new System.NotImplementedException();
        }
    }
}