//-----------------------------------------------------------------------
// <copyright file="EWMASpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Cluster.Metrics.Serialization;
using Akka.Cluster.Metrics.Tests.Helpers;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Metrics.Tests
{
    public class EWMASpec : AkkaSpec
    {
        public EWMASpec() : base(ClusterMetricsTestConfig.ClusterConfiguration)
        {
        }

        [Fact]
        public void EWMA_should_be_same_for_constant_values()
        {
            var e = new NodeMetrics.Types.EWMA(100, 0.18) + 100 + 100 + 100;
            e.Value.Should().BeApproximately(100, 0.001);
        }

        [Fact]
        public void EWMA_should_calculate_correct_ewma_for_normal_decay()
        {
            var e0 = new NodeMetrics.Types.EWMA(1000, 2.0 / (1 + 10));
            e0.Value.Should().BeApproximately(1000, 0.01);
            var e1 = e0 + 10.0;
            e1.Value.Should().BeApproximately(820.0, 0.01);
            var e2 = e1 + 10.0;
            e2.Value.Should().BeApproximately (672.73, 0.01);
            var e3 = e2 + 10.0;
            e3.Value.Should().BeApproximately(552.23, 0.01);
            var e4 = e3 + 10.0;
            e4.Value.Should().BeApproximately(453.64, 0.01);

            var en = Enumerable.Range(1, 100).Aggregate(e0, (e, _) => e + 10.0);
            en.Value.Should().BeApproximately(10.0, 0.1);
        }

        [Fact]
        public void EWMA_should_calculate_for_alpha_1_0_and_max_bias_towards_latest_value()
        {
            var e0 = new NodeMetrics.Types.EWMA(100, 1.0);
            e0.Value.Should().BeApproximately(100, 0.01);
            var e1 = e0 + 1;
            e1.Value.Should().BeApproximately(1, 0.01);
            var e2 = e1 + 57;
            e2.Value.Should().BeApproximately(57, 0.01);
            var e3 = e2 + 10;
            e3.Value.Should().BeApproximately(10, 0.01);
        }

        [Fact]
        public void EWMA_should_calculate_alpha_from_half_life_and_collect_interval()
        {
            // according to http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
            var expectedAlpha = 0.1;
            // alpha = 2.0 / (1 + N)
            var n = 19;
            var halfLife = n / 2.8854;
            var collectInterval = 1.Seconds();
            var halfLifeDuration = TimeSpan.FromSeconds(halfLife);
            NodeMetrics.Types.EWMA.GetAlpha(halfLifeDuration, collectInterval).Should().BeApproximately(expectedAlpha, 0.001);
        }

        [Fact]
        public void EWMA_should_calculate_same_alpha_from_short_halflife()
        {
            var alpha = NodeMetrics.Types.EWMA.GetAlpha(1.Milliseconds(), 3.Seconds());
            alpha.Should().BeInRange(0, 1);
            alpha.Should().BeApproximately(1, 0.001);
        }

        [Fact]
        public void EWMA_should_calculate_same_alpha_from_long_halflife()
        {
            var alpha = NodeMetrics.Types.EWMA.GetAlpha(1.Days(), 3.Seconds());
            alpha.Should().BeInRange(0, 1);
            alpha.Should().BeApproximately(0, 0.001);
        }
        
        // TODO: Port one more test with real MetricsCollector implementation
    }
}
