//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// Default <see cref="ClusterMetricsSupervisor"/> 
    /// </summary>
    public class ClusterMetricsStrategy : OneForOneStrategy
    {
        public ClusterMetricsStrategy(Config config)
            : base(
                maxNrOfRetries: config.GetInt("maxNrOfRetries", 0), 
                withinTimeMilliseconds: (int)config.GetTimeSpan("withinTimeRange", null).TotalMilliseconds, 
                loggingEnabled: config.GetBoolean("loggingEnabled", false),
                localOnlyDecider: MetricsDecider)
        {
        }
        
        private static Directive MetricsDecider(Exception ex)
        {
            switch (ex)
            {
                case ActorInitializationException actorInitializationException:
                    return Directive.Stop;
                case ActorKilledException actorKilledException:
                    return Directive.Stop;
                case DeathPactException deathPactException:
                    return Directive.Stop;
                default:
                    return Directive.Restart;
            }
        }
    }
}
