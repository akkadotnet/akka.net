//-----------------------------------------------------------------------
// <copyright file="MetricsCollectorBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Metrics.Collectors;
using Akka.Cluster.Metrics.Configuration;
using Akka.Cluster.Metrics.Helpers;
using Akka.Event;
using Akka.Util;
using Akka.Configuration;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// Factory to create configured <see cref="IMetricsCollector"/>
    ///
    /// Metrics collector instantiation priority order:
    /// 1. Provided custom collector
    /// 2. Internal <see cref="DefaultCollector"/>
    /// </summary>
    public class MetricsCollectorBuilder
    {
        public IMetricsCollector Build(ActorSystem system)
        {
            var log = Logging.GetLogger(system, GetType());
            var settings = ClusterMetricsSettings.Create(system.Settings.Config);

            var collectorCustom = settings.CollectorProvider;
            var defaultCollector = typeof(DefaultCollector).AssemblyQualifiedName;

            var useCustom = !settings.CollectorFallback;
            var useInternal = settings.CollectorFallback && string.IsNullOrEmpty(settings.CollectorProvider);
            
            Try<IMetricsCollector> Create(string provider)
            {
                log.Debug("Trying {0}", provider);
                return DynamicAccess.CreateInstanceFor<IMetricsCollector>(provider, system);
            }

            Try<IMetricsCollector> collector;
            if (useCustom)
                collector = Create(collectorCustom);
            else if (useInternal)
                collector = Create(defaultCollector);
            else // Use complete fall back chain.
                collector = Create(collectorCustom).OrElse(Create(defaultCollector));

            return collector.Recover(ex => throw new ConfigurationException($"Could not create metrics collector: {ex}")).Get();
        }
    }
}
