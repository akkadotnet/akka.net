//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Cluster.Metrics.Configuration;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Dispatch;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using FluentAssertions;
using FsCheck;
using Akka.Configuration;
using FluentAssertions.Extensions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Cluster.Metrics.Tests
{
    public class ClusterMetricsSettingsSpec : AkkaSpec
    {
        [Fact]
        public void Should_be_able_to_parse_settings()
        {
            var config = Sys.Settings.Config.WithFallback(
                ConfigurationFactory.FromResource<ClusterMetricsSettings>("Akka.Cluster.Metrics.reference.conf"));
            var settings = new ClusterMetricsSettings(config);
            
            // Extension.
            settings.MetricsDispatcher.ShouldBe(Dispatchers.DefaultDispatcherId);
            settings.PeriodicTasksInitialDelay.Should().Be(1.Seconds());
            
            // Supervisor.
            settings.SupervisorName.Should().Be("cluster-metrics");
            settings.SupervisorStrategyProvider.Should().BeEquivalentTo(typeof(ClusterMetricsStrategy).TypeQualifiedName());
            settings.SupervisorStrategyConfiguration.ToString().Should().BeEquivalentTo(
                ConfigurationFactory.ParseString("loggingEnabled=true,withinTimeRange=3s,maxNrOfRetries=3").ToString());
            
            // Collector.
            settings.CollectorEnabled.Should().BeTrue();
            settings.CollectorProvider.Should().BeEmpty();
            settings.CollectorSampleInterval.Should().Be(3.Seconds());
            settings.CollectorGossipInterval.Should().Be(3.Seconds());
            settings.CollectorMovingAverageHalfLife.Should().Be(12.Seconds());
        }
    }
}
