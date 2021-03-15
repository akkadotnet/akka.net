//-----------------------------------------------------------------------
// <copyright file="MetricListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Metrics;
using Akka.Cluster.Metrics.Events;
using Akka.Cluster.Metrics.Serialization;
using Akka.Event;
using Akka.Util.Internal;

namespace Samples.Cluster.Metrics.Common
{
    public class MetricListener : ReceiveActor
    {
        private readonly ClusterMetrics _extension;

        public MetricListener()
        {
            _extension = ClusterMetrics.Get(Context.System);

            var log = Context.GetLogger();
            var cluster = Akka.Cluster.Cluster.Get(Context.System);

            Receive<ClusterMetricsChanged>(clusterMetrics =>
            {
                clusterMetrics.NodeMetrics
                    .ForEach(nodeMetrics =>
                    {
                        foreach (var metric in nodeMetrics.Metrics)
                        {
                            switch (metric.Name)
                            {
                                case StandardMetrics.MemoryUsed:
                                    log.Info($"{nodeMetrics.Address}: Used memory: {metric.Value.DoubleValue / 1024 / 1024} MB");
                                    break;
                                case StandardMetrics.CpuTotalUsage:
                                    log.Info($"{nodeMetrics.Address}: CPU Total load: {metric.Value}");
                                    break;
                                case StandardMetrics.CpuProcessUsage:
                                    log.Info($"{nodeMetrics.Address}: CPU Process load: {metric.Value}");
                                    break;
                            }
                        }
                    });
            });

            Receive<ClusterEvent.CurrentClusterState>(_ =>
            {
                // Ignored
            });
        }

        // Subscribe unto ClusterMetricsEvent events.
        protected override void PreStart() => _extension.Subscribe(Self);

        // Unsubscribe from ClusterMetricsEvent events.
        protected override void PostStop() => _extension.Unsubscribe(Self);
    }
}
