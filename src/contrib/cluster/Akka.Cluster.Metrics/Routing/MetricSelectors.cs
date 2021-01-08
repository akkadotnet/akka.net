//-----------------------------------------------------------------------
// <copyright file="MetricSelectors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Util;
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
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<IMetricsSelector>();

            var selectorTypeName = config.GetString("metrics-selector");
            switch (selectorTypeName)
            {
                case "mix": return MixMetricsSelector.Instance;
                case "memory": return MemoryMetricsSelector.Instance;
                case "cpu": return CpuMetricsSelector.Instance;
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
            return capacity.ToImmutableDictionary(pair => pair.Key, pair => (int)Math.Round(pair.Value / divisor, MidpointRounding.AwayFromZero));
        }
        
        /// <inheritdoc />
        public IImmutableDictionary<Actor.Address, int> Weights(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return Weights(Capacity(nodeMetrics));
        }
    }

    /// <summary>
    /// MetricsSelector that uses the memory metrics.
    /// Less memory available => small weight.
    /// </summary>
    public class MemoryMetricsSelector : CapacityMetricsSelector
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static readonly MemoryMetricsSelector Instance = new MemoryMetricsSelector();
        
        /// <inheritdoc />
        public override IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return nodeMetrics
                .Select(StandardMetrics.Memory.Decompose)
                .Where(m => m.HasValue)
                .ToImmutableDictionary(m => m.Value.Address, m =>
                {
                    if (m.Value.MaxRecommendedSmoothValue.HasValue &&
                        m.Value.MaxRecommendedSmoothValue.Value > m.Value.AvailableSmoothValue)
                    {
                        return (m.Value.MaxRecommendedSmoothValue.Value - m.Value.UsedSmoothValue) / m.Value.MaxRecommendedSmoothValue.Value;
                    }

                    return (m.Value.AvailableSmoothValue - m.Value.UsedSmoothValue) / m.Value.AvailableSmoothValue;
                });
        }
    }

    /// <summary>
    /// MetricsSelector that uses the CPU usage metrics.
    /// Low CPU capacity => small node weight.
    /// </summary>
    public class CpuMetricsSelector : CapacityMetricsSelector
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static readonly CpuMetricsSelector Instance = new CpuMetricsSelector();

        public CpuMetricsSelector()
        {
        }
        
        /// <inheritdoc />
        public override IImmutableDictionary<Actor.Address, double> Capacity(IImmutableSet<NodeMetrics> nodeMetrics)
        {
            return nodeMetrics
                .Select(StandardMetrics.Cpu.Decompose)
                .Where(m => m.HasValue)
                .ToImmutableDictionary(m => m.Value.Address, m =>
                {
                    var load = m.Value.CpuTotalUsage;
                    var capacity = load >= 1 ? 0 : 1 - load;
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
            var combined = Selectors.SelectMany(s => s.Capacity(nodeMetrics)).ToImmutableArray();
            // aggregated average of the capacities by address
            var init = ImmutableDictionary<Actor.Address, (double Sum, int Count)>.Empty;
            return combined.Aggregate(init, (acc, pair) =>
            {
                var (address, capacity) = (pair.Key, pair.Value);
                var (sum, count) = acc.GetValueOrDefault(address, (0.0, 0));
                var elem = (Sum: sum + capacity, Count: count + 1);
                return acc.ContainsKey(address) ? acc.SetItem(address, elem) : acc.Add(address, elem);
            }).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Sum / pair.Value.Count);
        }
    }

    /// <summary>
    /// MetricsSelector that combines other selectors and aggregates their capacity
    /// values. By default it uses <see cref="MemoryMetricsSelector"/> and
    /// <see cref="CpuMetricsSelector"/>
    /// </summary>
    public class MixMetricsSelector : MixMetricsSelectorBase
    {
        /// <inheritdoc />
        public MixMetricsSelector(ImmutableArray<CapacityMetricsSelector> selectors) : base(selectors)
        {
        }
        
        /// <summary>
        /// Singleton instance of the default MixMetricsSelector, which uses <see cref="MemoryMetricsSelector"/> and
        /// <see cref="CpuMetricsSelector"/>
        /// </summary>
        public static readonly MixMetricsSelector Instance = new MixMetricsSelector(
            ImmutableArray.Create<CapacityMetricsSelector>(
                MemoryMetricsSelector.Instance,
                CpuMetricsSelector.Instance)
        );
    }
}
