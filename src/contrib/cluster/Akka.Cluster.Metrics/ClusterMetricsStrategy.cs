//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
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
                maxNrOfRetries: config.GetInt("maxNrOfRetries"), 
                withinTimeMilliseconds: (int)config.GetTimeSpan("withinTimeRange").TotalMilliseconds, 
                loggingEnabled: config.GetBoolean("loggingEnabled"),
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
