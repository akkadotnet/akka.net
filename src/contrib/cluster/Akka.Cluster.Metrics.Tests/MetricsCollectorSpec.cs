//-----------------------------------------------------------------------
// <copyright file="MetricsCollectorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Cluster.Metrics.Tests.Base;
using Akka.Cluster.Metrics.Tests.Helpers;
using Akka.TestKit;
using Akka.Util.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Metrics.Tests
{
    public class MetricsCollectorSpec : AkkaSpecWithCollector
    {
        /// <inheritdoc />
        public MetricsCollectorSpec(ITestOutputHelper output) 
            : base(ClusterMetricsTestConfig.DefaultEnabled, output)
        {
        }

        [Fact]
        public void Metric_should_merge_2_metrics_that_are_tracking_the_same_metric()
        {
            for (var i = 1; i <= 40; ++i)
            {
                var sample1 = Collector.Sample().Metrics;
                var sample2 = Collector.Sample().Metrics;
                foreach (var latest in sample2)
                {
                    foreach (var peer in sample1.Where(latest.SameAs))
                    {
                        var m = peer + latest;
                        m.Value.Should().Be(latest.Value);
                        m.IsSmooth.Should().Be(peer.IsSmooth || latest.IsSmooth);
                    }
                }
            }
        }

        [Fact]
        public void MetricsCollector_should_collector_accurate_metrics_for_node()
        {
            var sample = Collector.Sample();
            var metrics = sample.Metrics.Select(m => (Name: m.Name, Value: m.Value)).ToList();
            var used = metrics.First(m => m.Name == StandardMetrics.MemoryUsed);
            var available = metrics.First(m => m.Name == StandardMetrics.MemoryAvailable);
            metrics.ForEach(m =>
            {
                switch (m.Name)
                {
                    case StandardMetrics.Processors:
                        m.Value.DoubleValue.Should().BeGreaterOrEqualTo(0);
                        break;
                    case StandardMetrics.MemoryAvailable: 
                        m.Value.LongValue.Should().BeGreaterThan(0);
                        break;
                    case StandardMetrics.MemoryUsed: 
                        m.Value.LongValue.Should().BeGreaterOrEqualTo(0);
                        break;
                    case StandardMetrics.MaxMemoryRecommended:
                        m.Value.LongValue.Should().BeGreaterThan(0);
                        // Since setting is only a recommendation, we can ignore it
                        // See: https://stackoverflow.com/a/7729022/3094849
                        
                        // used.Value.LongValue.Should().BeLessThan(m.Value.LongValue);
                        // available.Value.LongValue.Should().BeLessThan(m.Value.LongValue);
                        break;
                    case StandardMetrics.CpuProcessUsage:
                        m.Value.DoubleValue.Should().BeInRange(0, 1);
                        break;
                    case StandardMetrics.CpuTotalUsage: 
                        m.Value.DoubleValue.Should().BeInRange(0, 1);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"Unexpected metric type {m.Name}");
                }
            });
        }

        [Fact(Skip = "This performance really depends on current load - so while should work well with specified timeouts," +
                     "let's disable it to avoid flaky failures in future")]
        public async Task MetricsCollector_should_collect_50_node_metrics_samples_in_an_acceptable_duration()
        {
            const int iterationsCount = 50;
            var delay = TimeSpan.FromMilliseconds(100);
            var iterationAverageExpectation = TimeSpan.FromMilliseconds(500) /*max sample time*/ + delay;

            var delayBetweenFailures = TimeSpan.FromMilliseconds(500);
            const int tryCount = 3; // In case of we have a high load on CI, let's try to execute racy failure once more
            
            await AwaitAssertAsync(async () =>
            {
                await WithinAsync(iterationAverageExpectation.Multiply(iterationsCount), async () =>
                {
                    for (var i = 0; i < iterationsCount; ++i)
                    {
                        var sample = Collector.Sample();
                        sample.Metrics.Count.Should().BeGreaterOrEqualTo(3);
                        await Task.Delay(delay);
                    }
                });
            }, iterationAverageExpectation.Multiply(iterationsCount * tryCount), delayBetweenFailures);
        }
    }
}
