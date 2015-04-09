//-----------------------------------------------------------------------
// <copyright file="EWMASpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class EWMASpec : MetricsCollectorFactory, IDisposable
    {
        private readonly IMetricsCollector _collector;
        public EWMASpec()
        {
            _collector = CreateMetricsCollector();
        }

        [Fact]
        public void DataStream_must_calculate_same_emwa_for_constant_values()
        {
            var ds = new EWMA(100.0d, 0.18) + 100.0D + 100.0D + 100.0D;
            Assert.True(ds.Value >= 100.0D - 0.001 && ds.Value <= 100.0D + 0.001);
        }

        [Fact]
        public void DataStream_must_calculate_correct_ewma_for_normal_decay()
        {
            var d0 = new EWMA(1000.0D, 2.0 / (1 + 10));
            Assert.True(d0.Value >= 1000.0D - 0.01 && d0.Value <= 1000.0D + 0.01);
            var d1 = d0 + 10.0d;
            Assert.True(d1.Value >= 820.0D - 0.01 && d1.Value <= 820.0D + 0.01);
            var d2 = d1 + 10.0d;
            Assert.True(d2.Value >= 672.73D - 0.01 && d2.Value <= 672.73D + 0.01);
            var d3 = d2 + 10.0d;
            Assert.True(d3.Value >= 552.23D - 0.01 && d3.Value <= 552.23D + 0.01);
            var d4 = d3 + 10.0d;
            Assert.True(d4.Value >= 453.64D - 0.01 && d4.Value <= 453.64D + 0.01);

            var dn = d0;
            for (var i = 0; i < 100; i++)
                dn = dn + 10.0d;
            Assert.True(dn.Value >= 10.0D - 0.1 && dn.Value <= 10.0D + 0.1);
        }

        [Fact]
        public void DataStream_must_calculate_correct_emwa_value_for_alpha_10_max_bias_towards_latest_value()
        {
            var d0 = new EWMA(100.0D, 1.0);
            Assert.True(d0.Value >= 100.0D - 0.01 && d0.Value <= 100.0D + 0.01);
            var d1 = d0 + 1.0d;
            Assert.True(d1.Value >= 1.0D - 0.01 && d1.Value <= 1.0D + 0.01);
            var d2 = d1 + 57.0d;
            Assert.True(d2.Value >= 57.0D - 0.01 && d2.Value <= 57.0D + 0.01);
            var d3 = d2 + 10.0d;
            Assert.True(d3.Value >= 10.0D - 0.01 && d3.Value <= 10.0D + 0.01);
        }

        [Fact]
        public void DataStream_must_calculate_alpha_from_halflife_and_collect_interval()
        {
            // according to http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
            var expectedAlpha = 0.1;
            // alpha = 2.0 / (1 + N)
            var n = 19;
            var halfLife = n / 2.8854;
            var collectInterval = TimeSpan.FromSeconds(1);
            var halfLifeDuration = TimeSpan.FromMilliseconds(halfLife * 1000);
            var alpha = EWMA.CalculateAlpha(halfLifeDuration, collectInterval);
            Assert.True(alpha >= expectedAlpha - 0.001 && alpha <= expectedAlpha + 0.001);
        }

        [Fact]
        public void DataStream_must_calculate_sane_alpha_from_short_halflife()
        {
            var alpha = EWMA.CalculateAlpha(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(3));
            Assert.True(alpha <= 1.0d);
            Assert.True(alpha >= 0.0d);
            Assert.True(alpha >= 1.0d - 0.001 && alpha <= 1.0d + 0.001);
        }

        [Fact]
        public void DataStream_must_calculate_sane_alpha_from_long_halflife()
        {
            var alpha = EWMA.CalculateAlpha(TimeSpan.FromDays(1), TimeSpan.FromSeconds(3));
            Assert.True(alpha <= 1.0d);
            Assert.True(alpha >= 0.0d);
            Assert.True(alpha >= 0.0d - 0.001 && alpha <= 0.0d + 0.001);
        }

        [Fact]
        public void Calculate_the_EWMA_for_multiple_variable_datastreams()
        {
            var streamingDataSet = ImmutableDictionary.Create<string, Metric>();
            var usedMemory = new byte[0];
            foreach (var i in Enumerable.Range(1, 50))
            {
                // wait a while between each message to give the metrics a chance to change
                Thread.Sleep(100);
                usedMemory =
                    usedMemory.Concat(Enumerable.Repeat(Convert.ToByte(ThreadLocalRandom.Current.Next(127)), 1024))
                        .ToArray();
                var changes = _collector.Sample().Metrics.Select(latest =>
                {
                    Metric previous;
                    if (!streamingDataSet.TryGetValue(latest.Name, out previous)) return latest;
                    if (latest.IsSmooth && latest.Value != previous.Value)
                    {
                        var updated = previous + latest;
                        updated.IsSmooth.ShouldBeTrue();
                        Assert.False(Math.Abs(updated.SmoothValue - previous.SmoothValue) < 0.01);
                        return updated;
                    }
                    else return latest;
                });
                streamingDataSet = streamingDataSet.Union(changes.ToDictionary(metric => metric.Name, metric => metric)).ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
            }
        }

        #region IDisposable members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _collector.Dispose();
                base.Dispose();
            }
        }

        #endregion
    }
}

