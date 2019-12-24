// //-----------------------------------------------------------------------
// // <copyright file="ClusterMetticsSettings.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

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
        
        private ClusterMetricsSettings(Config config)
        {
            _config = config.GetConfig("akka.cluster.metrics");
        }

        /// <summary>
        /// Creates new instance of <see cref="ClusterMetricsSettings"/>
        /// </summary>
        public static ClusterMetricsSettings Create(Config config)
        {
            return new ClusterMetricsSettings(config);
        }
        
        // Extension.
        public string MetricsDispatcher => _config.GetString("dispatcher");
        public TimeSpan PeriodicTasksInitialDelay => _config.GetTimeSpan("periodic-tasks-initial-delay");
        public string NativeLibraryExtractFolder => _config.GetString("native-library-extract-folder");

        // Supervisor.
        public string SupervisorName => _config.GetString("supervisor.name");
        public string SupervisorStrategyProvider => _config.GetString("supervisor.strategy.provider");
        public Config SupervisorStrategyConfiguration => _config.GetConfig("supervisor.strategy.configuration");

        // Collector.
        public bool CollectorEnabled => _config.GetBoolean("collector.enabled");
        public string CollectorProvider => _config.GetString("collector.provider");
        public bool CollectorFallback => _config.GetBoolean("collector.fallback");
        public TimeSpan CollectorSampleInterval => 
            Requiring(_config.GetTimeSpan("collector.sample-interval"), t => t > TimeSpan.Zero, "collector.sample-interval must be > 0");
        public TimeSpan CollectorGossipInterval =>
            Requiring(_config.GetTimeSpan("collector.gossip-interval"), t => t > TimeSpan.Zero, "collector.gossip-interval must be > 0");
        public TimeSpan CollectorMovingAverageHalfLife =>
            Requiring(_config.GetTimeSpan("collector.moving-average-half-life"), t => t > TimeSpan.Zero, "collector.moving-average-half-life must be > 0");

        private T Requiring<T>(T value, Predicate<T> condition, string reason)
        {
            if (condition(value))
                return value;
            
            throw new ArgumentException(reason);
        }
    }
}