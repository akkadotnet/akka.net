using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Cluster.Tests
{
    public static class MetricsEnabledSpec
    {
        public static readonly string Config = @"
            akka.cluster.metrics.enabled = on
            akka.cluster.metrics.collect-interval = 1 s
            akka.cluster.metrics.gossip-interval = 1 s
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""";
    }

    public class MetricsCollectorSpec : MetricsCollectorFactory, IDisposable
    {
        public ActorRef Self { get { return TestActor; } }

        private readonly IMetricsCollector _collector;

        public MetricsCollectorSpec()
        {
            _collector = CreateMetricsCollector();
        }

        [Fact]
        public void Metric_must_merge_2_metrics_that_are_tracking_the_same_metric()
        {
            for (var i = 1; i <= 20; i++)
            {
                var sample1 = _collector.Sample().Metrics;
                var sample2 = _collector.Sample().Metrics;
                var merged12 = sample1.Zip(sample2, (metric, metric1) => metric + metric1).ToList();
                foreach (var latest in merged12)
                {
                    Metric peer = null;
                    if (sample1.TryGetValue(latest, out peer))
                    {
                        var m = peer + latest;
                        Assert.Equal(latest.Value, m.Value);
                        Assert.True(m.IsSmooth == (peer.IsSmooth || latest.IsSmooth));
                    }
                    else
                    {
                        Assert.False(true, "Should have found set in collection");
                    }
                }

                var sample3 = _collector.Sample().Metrics;
                var sample4 = _collector.Sample().Metrics;
                var merged34 = sample3.Zip(sample4, (metric, metric1) => metric + metric1).ToList();
                foreach (var latest in merged12)
                {
                    Metric peer = null;
                    if (sample3.TryGetValue(latest, out peer))
                    {
                        var m = peer + latest;
                        Assert.Equal(latest.Value, m.Value);
                        Assert.True(m.IsSmooth == (peer.IsSmooth || latest.IsSmooth));
                    }
                    else
                    {
                        Assert.False(true, "Should have found set in collection");
                    }
                }
            }
        }

        [Fact]
        public void MetricsCollector_must_not_raise_errors_when_attempting_reflective_code_in_apply()
        {
            Assert.NotNull(CreateMetricsCollector());
        }

        [Fact]
        public void MetricsCollector_collect_accurate_metrics_for_a_node()
        {
            var sample = _collector.Sample();
            var metrics = sample.Metrics.Select(x => new {x.Name, x.Value}).ToList();
            var used = metrics.First(arg => arg.Name == StandardMetrics.ClrProcessMemoryUsed);
            var memoryAvailable = metrics.First(arg => arg.Name == StandardMetrics.SystemMemoryAvailable);
            foreach (var metric in metrics)
            {
                if (metric.Name == StandardMetrics.SystemLoadAverage) Assert.True(metric.Value >= 0.0);
                if (metric.Name == StandardMetrics.Processors) Assert.True(Convert.ToInt32(metric.Value) >= 0);
                if (metric.Name == StandardMetrics.ClrProcessMemoryUsed) Assert.True(Convert.ToInt64(metric.Value) >= 0L);
                if (metric.Name == StandardMetrics.SystemMemoryAvailable) Assert.True(Convert.ToInt64(metric.Value) > 0L);
                if (metric.Name == StandardMetrics.SystemMemoryMax)
                {
                    Assert.True(Convert.ToInt64(metric.Value) > 0L);
                    Assert.True(used.Value < metric.Value);
                    Assert.True(memoryAvailable.Value <= metric.Value);
                }
            }
        }

        [Fact]
        public void MetricsCollector_collect_50_node_metrics_samples_in_an_acceptable_duration()
        {
            foreach (var i in Enumerable.Range(1, 50))
            {
                var sample = _collector.Sample();
                Assert.True(sample.Metrics.Count >= 3);
                Thread.Sleep(100);
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

    /// <summary>
    /// Used when testing metrics without full Cluster
    /// </summary>
    public class MetricsCollectorFactory : AkkaSpec //TODO: if we inherit from ClusterSpecBase, tests never run - must be a config chaining problem
    {
        public MetricsCollectorFactory()
            : base(MetricsEnabledSpec.Config)
        {
            ExtendedActorSystem = Sys.AsInstanceOf<ExtendedActorSystem>();
            SelfAddress = ExtendedActorSystem.Provider.RootPath.Address;
        }

        protected Address SelfAddress;
        protected ExtendedActorSystem ExtendedActorSystem;
        protected readonly double DefaultDecayFactor = 2.0/(1 + 1.0);

        protected IMetricsCollector CreateMetricsCollector()
        {
            return new PerformanceCounterMetricsCollector(SelfAddress, DefaultDecayFactor);
        }
    }
}
