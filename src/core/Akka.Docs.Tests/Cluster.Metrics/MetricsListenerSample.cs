//-----------------------------------------------------------------------
// <copyright file="MetricsListenerSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Metrics.Events;
using Akka.Cluster.Metrics.Serialization;
using Akka.Event;
using Akka.Util;

namespace Akka.Cluster.Metrics.Tests
{
    public class MetricsListener : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ClusterMetrics _metricsExtension = ClusterMetrics.Get(Context.System);
        
        public MetricsListener()
        {
            Receive<ClusterMetricsChanged>(clusterMetrics =>
            {
                foreach (var nodeMetrics in clusterMetrics.NodeMetrics)
                {
                    if (nodeMetrics.Address.Equals(_cluster.SelfAddress))
                    {
                        LogMemory(nodeMetrics);
                        LogCpu(nodeMetrics);
                    }
                }
            });
        }

        // Subscribe unto ClusterMetricsEvent events.
        protected override void PreStart()
        {
            base.PreStart();
            
            _metricsExtension.Subscribe(Self);
        }
        
        // Unsubscribe from ClusterMetricsEvent events.
        protected override void PostStop()
        {
            base.PostStop();
            
            _metricsExtension.Unsubscribe(Self);
        }

        private void LogMemory(NodeMetrics nodeMetrics)
        {
            Option<StandardMetrics.Memory> memory = StandardMetrics.ExtractMemory(nodeMetrics);
            if (memory.HasValue)
                _log.Info("Used memory: {0} Mb", memory.Value.Used / 1024 / 1024);
        }

        private void LogCpu(NodeMetrics nodeMetrics)
        {
            Option<StandardMetrics.Cpu> cpu = StandardMetrics.ExtractCpu(nodeMetrics);
            if (cpu.HasValue)
                _log.Info("Cpu load: {0}% ({1} processors)", cpu.Value.TotalUsage / 100, cpu.Value.ProcessorsNumber);
        }
    }
}
