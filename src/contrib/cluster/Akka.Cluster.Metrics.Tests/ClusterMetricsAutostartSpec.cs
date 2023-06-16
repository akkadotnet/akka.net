//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsAutostartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Events;
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
        /// <summary>
        /// This is a single node test.
        /// </summary>
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka {
    extensions = [""Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics""]
    actor.provider = ""cluster""
    cluster.metrics.collector {
        provider = [""Akka.Cluster.Metrics.Collectors.DefaultCollector, Akka.Cluster.Metrics""]
        sample-interval = 200ms
        gossip-interval = 200ms
    }
}
");
        
        public ClusterMetricsAutostartSpec(ITestOutputHelper output)
            : base(Config, output)
        {
        }

        [Fact]
        public void Metrics_extension_Should_autostart_if_added_to_akka_extensions()
        {
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(IClusterMetricsEvent));
            // Should collect automatically
            probe.ExpectMsg<IClusterMetricsEvent>();
        }
    }
}
