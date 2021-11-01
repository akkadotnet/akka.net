//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsExtensionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Tests.Helpers;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Metrics.Tests
{
    public class ClusterMetricsAutostartSpec : AkkaSpec
    {
        private readonly ClusterMetricsView _metricsView;
        
        private int MetricsNodeCount => _metricsView.ClusterMetrics.Count;

        /// <summary>
        /// This is a single node test.
        /// </summary>
        private const int NodeCount = 1;
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka.extensions = [""Akka.Management.Cluster.Bootstrap.ClusterBootstrapProvider, Akka.Management.Cluster.Bootstrap""]
akka.cluster.metrics.collector.enabled = on")
                .WithFallback(ClusterMetricsTestConfig.ClusterConfiguration);
        
        public ClusterMetricsAutostartSpec(ITestOutputHelper output)
            : base(Config, output)
        {
            var cluster = Cluster.Get(Sys);
            _metricsView = new ClusterMetricsView(cluster.System);
        }

        [Fact]
        public async Task Metrics_extension_Should_autostart_if_added_to_akka_extensions()
        {
            // Should collect automatically
            await AwaitAssertAsync(() => MetricsNodeCount.Should().Be(NodeCount), 15.Seconds());
        }
    }
}
