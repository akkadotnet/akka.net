// //-----------------------------------------------------------------------
// // <copyright file="MetricSelectors.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// A MetricsSelector is responsible for producing weights from the node metrics.
    /// </summary>
    public interface IMetricsSelector
    {
        /// <summary>
        /// The weights per address, based on the nodeMetrics.
        /// </summary>
        IImmutableDictionary<Actor.Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics);
    }

    /// <summary>
    /// MetricsSelectorBuilder
    /// </summary>
    public static class MetricsSelectorBuilder
    {
        /// <summary>
        /// Builds <see cref="IMetricsSelector"/> defined in configuration
        /// </summary>
        /// <returns></returns>
        public static IMetricsSelector BuildFromConfig(Config config)
        {
            var selectorTypeName = config.GetString("metrics-selector");
            switch (selectorTypeName)
            {
                case "min": return MixMetricsSelector.Instance;
                case "heap": return HeapMetricsSelector.Instance;
                case "cpu": return CpuMetricsSelector.Instance;
                case "load": return SystemLoadAverageMetricsSelector.Instance;
                default:
                    return DynamicAccess.CreateInstanceFor<IMetricsSelector>(selectorTypeName, config)
                        .Recover(ex => throw new ArgumentException($"Cannot instantiate metrics-selector [{selectorTypeName}]," +
                                                                   $"make sure it extends [Akka.Cluster.Metrics.MetricsSelector] and " +
                                                                   $"has constructor with [Akka.Configuration.Config] parameter", ex))
                        .Get();
            }
        }
    }

    /// <summary>
    /// A MetricsSelector producing weights from remaining capacity.
    /// The weights are typically proportional to the remaining capacity.
    /// </summary>
    public abstract class CapacityMetricsSelector : IMetricsSelector
    {
        /// <summary>
        /// Remaining capacity for each node. The value is between 0.0 and 1.0, where 0.0 means no remaining capacity
        /// (full utilization) and 1.0 means full remaining capacity (zero utilization).
        /// </summary>
        public abstract IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics);

        /// <summary>
        /// Converts the capacity values to weights. The node with lowest capacity gets weight 1
        /// (lowest usable capacity is 1%) and other nodes gets weights proportional to their capacity compared to
        /// the node with lowest capacity.
        /// </summary>
        public IImmutableDictionary<Actor.Address, int> Weights(IImmutableDictionary<Actor.Address, double> capacity)
        {
            if (capacity.Count == 0)
                return ImmutableDictionary<Actor.Address, int>.Empty;

            var min = capacity.Min(c => c.Value);
            // lowest usable capacity is 1% (>= 0.5% will be rounded to weight 1), also avoids div by zero
            var divisor = Math.Max(0.01, min);
            return capacity.ToImmutableDictionary(pair => pair.Key, pair => (int)Math.Round(pair.Value / divisor));
        }
        
        /// <inheritdoc />
        public IImmutableDictionary<Actor.Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return Weights(Capacity(nodeMetrics));
        }
    }

    /// <summary>
    /// MetricsSelector that uses the heap metrics.
    /// Low heap capacity => small weight.
    /// </summary>
    public class HeapMetricsSelector : CapacityMetricsSelector
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static readonly HeapMetricsSelector Instance = new HeapMetricsSelector();
        
        /// <inheritdoc />
        public override IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return nodeMetrics
                .Select(StandardMetrics.HeapMemory.Unapply)
                .Where(m => m.HasValue)
                .ToImmutableDictionary(m => m.Value.Address, m =>
                {
                    var capacity = m.Value.HeapMemoryMaxValue.HasValue 
                        ? (m.Value.HeapMemoryMaxValue.Value - m.Value.UsedSmoothValue) / m.Value.HeapMemoryMaxValue.Value
                        : (m.Value.CommittedSmoothValue - m.Value.UsedSmoothValue) / m.Value.CommittedSmoothValue;
                    return capacity;
                });
        }
    }

    /// <summary>
    /// MetricsSelector that uses the combined CPU time metrics and stolen CPU time metrics.
    /// In modern Linux kernels: CpuCombined + CpuStolen + CpuIdle = 1.0  or 100%.
    /// Combined CPU is sum of User + Sys + Nice + Wait times, as percentage.
    /// Stolen CPU is the amount of CPU taken away from this virtual machine by the hypervisor, as percentage.
    ///
    /// Low CPU capacity => small node weight.
    /// </summary>
    public class CpuMetricsSelector : CapacityMetricsSelector
    {
        // TODO: Read factor from reference.conf
        private readonly double _factor = 0.3;
        
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static readonly CpuMetricsSelector Instance = new CpuMetricsSelector();

        public CpuMetricsSelector()
        {
            if (_factor < 0)
                throw new ArgumentException(nameof(_factor), $"factor must be non negative: {_factor}");
        }
        
        /// <inheritdoc />
        public override IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return nodeMetrics
                .Select(StandardMetrics.Cpu.Unapply)
                .Where(m => m.HasValue && m.Value.CpuCombined.HasValue && m.Value.CpuStolen.HasValue)
                .ToImmutableDictionary(m => m.Value.Address, m =>
                {
                    // Arbitrary load rating function which skews in favor of stolen time.
                    var load = m.Value.CpuCombined.Value + m.Value.CpuStolen.Value * (1 + _factor);
                    var capacity = load >= 1 ? 0 : 1 - load;
                    return capacity;
                });
        }
    }

    /// <summary>
    /// MetricsSelector that uses the system load average metrics.
    /// System load average is OS-specific average load on the CPUs in the system,
    /// for the past 1 minute. The system is possibly nearing a bottleneck if the
    /// system load average is nearing number of cpus/cores.
    /// Low load average capacity => small weight.
    /// </summary>
    public class SystemLoadAverageMetricsSelector : CapacityMetricsSelector
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static readonly SystemLoadAverageMetricsSelector Instance = new SystemLoadAverageMetricsSelector();
        
        /// <inheritdoc />
        public override IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return nodeMetrics
                .Select(StandardMetrics.Cpu.Unapply)
                .Where(m => m.HasValue && m.Value.SystemLoadAverage.HasValue)
                .ToImmutableDictionary(m => m.Value.Address, m =>
                {
                    var capacity = 1.0 - Math.Min(1.0, m.Value.SystemLoadAverage.Value / m.Value.Processors);
                    return capacity;
                });
        }
    }

    /// <summary>
    ///  Base class for IMetricsSelector that combines other selectors and aggregates their capacity.
    /// </summary>
    public abstract class MixMetricsSelectorBase : CapacityMetricsSelector
    {
        /// <summary>
        /// Selectors
        /// </summary>
        public ImmutableArray<CapacityMetricsSelector> Selectors { get; }

        protected MixMetricsSelectorBase(ImmutableArray<CapacityMetricsSelector> selectors)
        {
            Selectors = selectors;
        }

        /// <inheritdoc />
        public override IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            var combined = Selectors.SelectMany(s => Capacity(nodeMetrics)).ToImmutableArray();
            // aggregated average of the capacities by address
            var init = ImmutableDictionary<Actor.Address, (double Sum, int Count)>.Empty;
            return combined.Aggregate(init, (acc, pair) =>
            {
                var (address, capacity) = (pair.Key, pair.Value);
                var (sum, count) = acc[address];
                return acc.Add(address, (Sum: sum + capacity, Count: count + 1));
            }).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Sum / pair.Value.Count);
        }
    }

    /// <summary>
    /// MetricsSelector that combines other selectors and aggregates their capacity
    /// values. By default it uses <see cref="HeapMetricsSelector"/>,
    /// <see cref="CpuMetricsSelector"/>, and <see cref="SystemLoadAverageMetricsSelector"/>
    /// </summary>
    public class MixMetricsSelector : MixMetricsSelectorBase
    {
        /// <inheritdoc />
        public MixMetricsSelector(ImmutableArray<CapacityMetricsSelector> selectors) : base(selectors)
        {
        }
        
        /// <summary>
        /// Singleton instance of the default MixMetricsSelector, which uses <see cref="HeapMetricsSelector"/>,
        /// <see cref="CpuMetricsSelector"/>, and <see cref="SystemLoadAverageMetricsSelector"/>
        /// </summary>
        public static readonly MixMetricsSelector Instance = new MixMetricsSelector(
            ImmutableArray.Create<CapacityMetricsSelector>(
                HeapMetricsSelector.Instance,
                CpuMetricsSelector.Instance,
                SystemLoadAverageMetricsSelector.Instance)
        );
    }
}