//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Cluster.Metrics.Configuration
{
    /// <summary>
    /// Metrics extension settings. Documented in: `reference.conf`.
    /// </summary>
    public class ClusterMetricsSettings
    {
        private readonly Config _config;
        
        /// <summary>
        /// Creates instance of <see cref="ClusterMetricsSettings"/>
        /// </summary>
        public ClusterMetricsSettings(Config config)
        {
            _config = config.GetConfig("akka.cluster.metrics");
            if (_config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<ClusterMetricsSettings>("akka.cluster.metrics");

            MetricsDispatcher = _config.GetString("dispatcher");
            PeriodicTasksInitialDelay = _config.GetTimeSpan("periodic-tasks-initial-delay");

            SupervisorName = _config.GetString("supervisor.name");
            SupervisorStrategyProvider = _config.GetString("supervisor.strategy.provider");
            SupervisorStrategyConfiguration = _config.GetConfig("supervisor.strategy.configuration");

            CollectorEnabled = _config.GetBoolean("collector.enabled");
            CollectorProvider = _config.GetString("collector.provider");
            CollectorFallback = _config.GetBoolean("collector.fallback");
            CollectorSampleInterval = 
                Requiring(_config.GetTimeSpan("collector.sample-interval", null), t => t > TimeSpan.Zero, "collector.sample-interval must be > 0");

            CollectorGossipInterval = 
                Requiring(_config.GetTimeSpan("collector.gossip-interval", null), t => t > TimeSpan.Zero, "collector.gossip-interval must be > 0");
            CollectorMovingAverageHalfLife = 
                Requiring(_config.GetTimeSpan("collector.moving-average-half-life", null), t => t > TimeSpan.Zero, "collector.moving-average-half-life must be > 0");
        }

        /// <summary>
        /// Creates new instance of <see cref="ClusterMetricsSettings"/>
        /// </summary>
        public static ClusterMetricsSettings Create(Config config)
        {
            return new ClusterMetricsSettings(config);
        }
        
        // Extension.
        public string MetricsDispatcher { get; }
        public TimeSpan PeriodicTasksInitialDelay { get; }

        // Supervisor.
        public string SupervisorName { get; }
        public string SupervisorStrategyProvider { get; }
        public Config SupervisorStrategyConfiguration { get; }

        // Collector.
        public bool CollectorEnabled { get; }
        public string CollectorProvider { get; }
        public bool CollectorFallback { get; }
        public TimeSpan CollectorSampleInterval { get; }
            
        public TimeSpan CollectorGossipInterval { get; }
            
        public TimeSpan CollectorMovingAverageHalfLife { get; }
            

        private T Requiring<T>(T value, Predicate<T> condition, string reason)
        {
            if (condition(value))
                return value;
            
            throw new ArgumentException(reason);
        }
    }
}
