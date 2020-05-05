//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsTestConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

// //-----------------------------------------------------------------------
// // <copyright file="ClusterMetricsTestConfig.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

namespace Akka.Cluster.Metrics.Tests.Helpers
{
    /// <summary>
    /// Metrics test configurations.
    /// </summary>
    public class ClusterMetricsTestConfig
    {
        /// <summary>
        /// Default decay factor for tests
        /// </summary>
        public const double DefaultDecayFactor = 2.0 / (1 + 10);
        
        /// <summary>
        /// Test in cluster, with manual collection activation, collector mock, fast.
        /// </summary>
        public const string ClusterConfiguration = @"
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
        
        /// <summary>
        /// Test w/o cluster, with collection enabled.
        /// </summary>
        public const string DefaultEnabled = @"
            akka.cluster.metrics {
              collector {
                enabled = on
                sample-interval = 1s
                gossip-interval = 1s
              }
            }
            akka.actor.provider = remote
        ";
    }
}
