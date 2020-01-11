// //-----------------------------------------------------------------------
// // <copyright file="DefaultCollector.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Metrics.Serialization;
using Akka.Util;
using Akka.Util.Extensions;
using Akka.Util.Internal;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Collectors
{
    /// <summary>
    /// Metrics collector that is used by default
    /// </summary>
    public class DefaultCollector : IMetricsCollector
    {
        private readonly Address _address;
        
        private readonly Stopwatch _cpuWatch;
        private TimeSpan _lastCpuMeasure = TimeSpan.Zero;
        private ImmutableDictionary<int, TimeSpan> _lastCpuTimings = ImmutableDictionary<int, TimeSpan>.Empty;

        public DefaultCollector(Address address)
        {
            _address = address;
            _cpuWatch = Stopwatch.StartNew();
        }
        
        public DefaultCollector(ActorSystem system) 
            : this(Cluster.Get(system).SelfAddress)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cpuWatch.Stop();
        }

        /// <inheritdoc />
        public NodeMetrics Sample()
        {
            using (var process = Process.GetCurrentProcess())
            {
                var metrics = new List<NodeMetrics.Types.Metric>()
                {
                    // Memory
                    NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, process.PrivateMemorySize64).Value,
                    // VirtualMemorySize64 is not best idea here...
                    NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, process.VirtualMemorySize64).Value,
                    // CPU Processors
                    NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, Environment.ProcessorCount).Value,
                };

                if (process.MaxWorkingSet != IntPtr.Zero)
                    metrics.Add(NodeMetrics.Types.Metric.Create(StandardMetrics.MaxMemoryRecommended, process.MaxWorkingSet.ToInt64()).Value);

                var (processCpuUsage, totalCpuUsage) = GetCpuUsages(process.Id);
                // CPU % by process
                metrics.Add(NodeMetrics.Types.Metric.Create(StandardMetrics.CpuProcessUsage, processCpuUsage).Value);
                // CPU % by all processes that are used for overall CPU capacity calculation
                metrics.Add(NodeMetrics.Types.Metric.Create(StandardMetrics.CpuTotalUsage, totalCpuUsage).Value);
            
                return new NodeMetrics(_address, DateTime.UtcNow.ToTimestamp(), metrics);
            }
        }
        
        private (double ProcessUsage, double TotalUsage) GetCpuUsages(int currentProcessId)
        {
            var processes = GetProcesses();

            try
            {
                // If this is first time we get timings, have to wait for some time to collect initial values
                if (_lastCpuMeasure == TimeSpan.Zero)
                {
                    _lastCpuMeasure = _cpuWatch.Elapsed;
                    _lastCpuTimings = GetTotalProcessorTimes(processes);
                    Thread.Sleep(500);
                    processes.ForEach(p => p.Refresh());
                }

                var endTime = _cpuWatch.Elapsed;
                var currentCpuTimings = GetTotalProcessorTimes(processes);
                
                var totalMsPassed = (endTime - _lastCpuMeasure).TotalMilliseconds;
                var cpuUsagePercentages = currentCpuTimings
                    .Where(u => _lastCpuTimings.ContainsKey(u.Key))
                    .ToImmutableDictionary(u => u.Key, u =>
                    {
                        var timeForProcess = (u.Value - _lastCpuTimings[u.Key]).TotalMilliseconds;
                        return  timeForProcess / (Environment.ProcessorCount * totalMsPassed);
                    });

                _lastCpuMeasure = endTime;
                _lastCpuTimings = currentCpuTimings;
            
                return (cpuUsagePercentages.GetValueOrDefault(currentProcessId, 0), cpuUsagePercentages.Values.DefaultIfEmpty().Sum());
            }
            finally
            {
                processes.ForEach(p => p.Dispose());
            }
        }

        private Process[] GetProcesses()
        {
            // return Process.GetProcesses();
            return new[] { Process.GetCurrentProcess() }; // Just considering only current process load
        }

        private static ImmutableDictionary<int, TimeSpan> GetTotalProcessorTimes(IEnumerable<Process> processes)
        {
            return processes
                // Skip processes for which access is denied
                .Select(proc => Try<(int Id, TimeSpan Time)>.From(() => (proc.Id, proc.TotalProcessorTime)))
                .Where(result => result.IsSuccess)
                .ToImmutableDictionary(result => result.Get().Id, p => p.Get().Time);
        }
    }
}