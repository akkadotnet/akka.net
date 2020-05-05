//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsExtensionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Tests.Helpers;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Metrics.Tests
{
    public class ClusterMetricsExtensionSpec : AkkaSpec
    {
        private readonly ClusterMetrics _extension;
        private readonly ClusterMetricsView _metricsView;
        private TimeSpan _sampleInterval;
        
        private int MetricsNodeCount => _metricsView.ClusterMetrics.Count;
        private int MetricsHistorySize => _metricsView.MetricsHistory.Count;
        private TimeSpan SampleCollectTimeout => TimeSpan.FromMilliseconds(_sampleInterval.TotalMilliseconds * 5);

        /// <summary>
        /// This is a single node test.
        /// </summary>
        private const int NodeCount = 1;
        /// <summary>
        /// Limit collector sample count.
        /// </summary>
        private const int SampleCount = 10;
        /// <summary>
        /// Metrics verification precision.
        /// </summary>
        private const double Epsilon = 0.001;
        
        public ClusterMetricsExtensionSpec(ITestOutputHelper output)
            : base(ClusterMetricsTestConfig.ClusterConfiguration, output)
        {
            var cluster = Cluster.Get(Sys);
            _extension = ClusterMetrics.Get(Sys);
            _metricsView = new ClusterMetricsView(cluster.System);
            _sampleInterval = _extension.Settings.CollectorSampleInterval;
        }

        [Fact]
        public async Task Metrics_extension_Should_collect_metrics_after_start_command()
        {
            // Should collect after start
            _extension.Supervisor.Tell(ClusterMetricsSupervisorMetadata.CollectionStartMessage.Instance);
            await AwaitAssertAsync(() => MetricsNodeCount.Should().Be(NodeCount), 15.Seconds());
            
            // Should collect during time window
            await AwaitAssertAsync(() => MetricsHistorySize.Should().BeGreaterOrEqualTo(SampleCount), 15.Seconds());
            var beforeStop = MetricsHistorySize;
            _extension.Supervisor.Tell(ClusterMetricsSupervisorMetadata.CollectionStopMessage.Instance);
            await AwaitSampleAsync();
            MetricsNodeCount.Should().Be(NodeCount);
            MetricsHistorySize.Should().BeGreaterOrEqualTo(beforeStop);
        }

        [Fact(Skip = "Racy")]
        public async Task Metrics_extension_Should_control_collector_on_off_state()
        {
            int size2 = 0, size3 = 0, size4 = 0;
            for (var i = 0; i < 3; ++i)
            {
                var size1 = MetricsHistorySize;
                await AwaitSampleAsync();
                await AwaitAssertAsync(() =>
                {
                    size2 = MetricsHistorySize;
                    size1.Should().Be(size2);
                }, SampleCollectTimeout);
            
                _extension.Supervisor.Tell(ClusterMetricsSupervisorMetadata.CollectionStartMessage.Instance);
                await AwaitSampleAsync();
                await AwaitAssertAsync(() =>
                {
                    size3 = MetricsHistorySize;
                    size3.Should().BeGreaterThan(size2);
                }, SampleCollectTimeout);
            
                _extension.Supervisor.Tell(ClusterMetricsSupervisorMetadata.CollectionStopMessage.Instance);
                await AwaitSampleAsync();
                await AwaitAssertAsync(() =>
                {
                    size4 = MetricsHistorySize;
                    size4.Should().BeGreaterOrEqualTo(size3);
                }, SampleCollectTimeout);

                await AwaitSampleAsync();
                await AwaitAssertAsync(() =>
                {
                    var size5 = MetricsHistorySize;
                    size5.Should().Be(size4);
                }, SampleCollectTimeout);
            }
        }

        private Task AwaitSampleAsync(double? timeMs = null)
        {
            timeMs = timeMs ?? _sampleInterval.TotalMilliseconds * 5;

            return Task.Delay(TimeSpan.FromMilliseconds(timeMs.Value));
        }
    }
}
