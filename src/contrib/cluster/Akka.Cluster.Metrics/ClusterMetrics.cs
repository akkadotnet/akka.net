//-----------------------------------------------------------------------
// <copyright file="ClusterMetrics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster.Metrics.Configuration;
using Akka.Cluster.Metrics.Events;
using Akka.Cluster.Metrics.Helpers;
using Akka.Cluster.Metrics.Serialization;
using Akka.Configuration;
using Akka.Util;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// Cluster metrics extension.
    ///
    /// Cluster metrics is primarily for load-balancing of nodes. It controls metrics sampling
    /// at a regular frequency, prepares highly variable data for further analysis by other entities,
    /// and publishes the latest cluster metrics data around the node ring and local eventStream
    /// to assist in determining the need to redirect traffic to the least-loaded nodes.
    ///
    /// Metrics sampling is delegated to the <see cref="IMetricsCollector"/>.
    ///
    /// Smoothing of the data for each monitored process is delegated to the
    /// <see cref="NodeMetrics.Types.EWMA"/> for exponential weighted moving average.
    /// </summary>
    public class ClusterMetrics : IExtension
    {
        private readonly ExtendedActorSystem _system;
        
        /// <summary>
        /// Cluster metrics settings
        /// </summary>
        public ClusterMetricsSettings Settings { get; }

        /// <summary>
        /// Default HOCON settings for cluster metrics.
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterMetrics>("Akka.Cluster.Metrics.reference.conf");
        }

        /// <summary>
        /// Creates new <see cref="ClusterMetrics"/> for given actor system
        /// </summary>
        /// <param name="system"></param>
        internal ClusterMetrics(ExtendedActorSystem  system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
            
            Settings = ClusterMetricsSettings.Create(_system.Settings.Config);
            
            Supervisor = _system.SystemActorOf(
                Props.Create<ClusterMetricsSupervisor>().WithDispatcher(Settings.MetricsDispatcher).WithDeploy(Deploy.Local), 
                Settings.SupervisorName);
        }

        /// <summary>
        /// Creates new <see cref="ClusterMetrics"/> for given actor system
        /// </summary>
        public static ClusterMetrics Get(ActorSystem system)
        {
            return system.WithExtension<ClusterMetrics, ClusterMetricsExtensionProvider>();
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Supervision strategy.
        /// </summary>
        [InternalApi]
        public ClusterMetricsStrategy Strategy
        {
            get
            {
                return DynamicAccess.CreateInstanceFor<ClusterMetricsStrategy>(Settings.SupervisorStrategyProvider, Settings.SupervisorStrategyConfiguration)
                    .GetOrElse(() =>
                    {
                        _system.Log.Error(
                            $"Configured strategy provider {Settings.SupervisorStrategyProvider} failed to load, " +
                            $"using default {typeof(ClusterMetricsStrategy).Name}.");
                        return new ClusterMetricsStrategy(Settings.SupervisorStrategyConfiguration);
                    })
                    .Get();
            }
        }

        /// <summary>
        /// Supervisor actor.
        /// Accepts subtypes of <see cref="ClusterMetricsSupervisorMetadata.ICollectionControlMessage"/> to manage metrics collection at runtime
        /// </summary>
        public IActorRef Supervisor { get; }

        /// <summary>
        /// Subscribe user metrics listener actor unto <see cref="IClusterMetricsEvent"/>
        /// events published by extension on the system event bus.
        /// </summary>
        public void Subscribe(IActorRef metricsListener)
        {
            _system.EventStream.Subscribe(metricsListener, typeof(IClusterMetricsEvent));
        }

        /// <summary>
        /// Unsubscribe user metrics listener actor from <see cref="IClusterMetricsEvent"/>
        /// events published by extension on the system event bus.
        /// </summary>
        /// <param name="metricsListener"></param>
        public void Unsubscribe(IActorRef metricsListener)
        {
            _system.EventStream.Unsubscribe(metricsListener, typeof(IClusterMetricsEvent));
        }
    }
    
    /// <summary>
    /// ClusterMetricsExtensionProvider
    /// </summary>
    public class ClusterMetricsExtensionProvider: ExtensionIdProvider<ClusterMetrics>
    {
        /// <summary>
        /// Creates new <see cref="ClusterMetrics"/> for given actor system
        /// </summary>
        public override ClusterMetrics CreateExtension(ExtendedActorSystem system)
        {
            var extension = new ClusterMetrics(system);
            return extension;
        }
    }
}
