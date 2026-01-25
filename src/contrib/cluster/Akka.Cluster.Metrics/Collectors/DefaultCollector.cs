//-----------------------------------------------------------------------
// <copyright file="DefaultCollector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        private TimeSpan _lastCpuMeasure;
        private bool _firstSample = true;
        private ImmutableDictionary<int, TimeSpan> _lastCpuTimings = ImmutableDictionary<int, TimeSpan>.Empty;

        public DefaultCollector(Address address)
        {
            _address = address;
            _cpuWatch = new Stopwatch();
        }
        
        public DefaultCollector(ActorSystem system) 
            : this(Cluster.Get(system).SelfAddress)
        {
        }

        
        public void Dispose()
        {
            _cpuWatch.Stop();
        }

        /// <inheritdoc />
        public NodeMetrics Sample()
        {
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                var metrics = new List<NodeMetrics.Types.Metric>();
                // Get memory used - managed heap size with fallbacks for known .NET runtime bugs
                var memoryUsedValue = GetMemoryUsedBytes(process);
                var totalMemory = NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, memoryUsedValue);
                if (totalMemory.HasValue)
                    metrics.Add(totalMemory.Value);
                    
                var availableMemory = NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, process.WorkingSet64 + process.PagedMemorySize64);
                if(availableMemory.HasValue)
                    metrics.Add(availableMemory.Value);

                var processorCount = NodeMetrics.Types.Metric.Create(StandardMetrics.Processors, Environment.ProcessorCount);
                if(processorCount.HasValue)
                    metrics.Add(processorCount.Value);

                try
                {
                    if (process.MaxWorkingSet != IntPtr.Zero)
                    {
                        var workingSet = NodeMetrics.Types.Metric.Create(StandardMetrics.MaxMemoryRecommended, process.MaxWorkingSet.ToInt64());
                        if(workingSet.HasValue)
                            metrics.Add(workingSet.Value);
                    }
                }
                catch (Exception)
                {
                    // MaxWorkingSet may throw on some platforms (e.g., Linux/Mono)
                    // Ignore and continue without this metric
                }

                var (processCpuUsage, totalCpuUsage) = GetCpuUsages(process.Id);
                
                // CPU % by process
                var cpuUsage = NodeMetrics.Types.Metric.Create(StandardMetrics.CpuProcessUsage, processCpuUsage);
                if(cpuUsage.HasValue)
                    metrics.Add(cpuUsage.Value);
                
                // CPU % by all processes that are used for overall CPU capacity calculation
                var totalCpu = NodeMetrics.Types.Metric.Create(StandardMetrics.CpuTotalUsage, totalCpuUsage);
                metrics.Add(totalCpu.Value);
            
                return new NodeMetrics(_address, DateTime.UtcNow.ToTimestamp(), metrics);
            }
        }
        
        private (double ProcessUsage, double TotalUsage) GetCpuUsages(int currentProcessId)
        {
            Process[] processes = null;
            
            try
            {
                TimeSpan measureStartTime = TimeSpan.Zero;
                TimeSpan measureEndTime;
                ImmutableDictionary<int, TimeSpan> currentCpuTimings;
                
                // If this is first time we get timings, have to wait for some time to collect initial values
                if (_firstSample)
                {
                    _firstSample = false;
                    _cpuWatch.Start();
                    processes = GetProcesses();
                    _lastCpuTimings = GetTotalProcessorTimes(processes);
                    Thread.Sleep(500);
                    // Sample iteration time: start next sample time BEFORE we collect "old" metric
                    _lastCpuMeasure = _cpuWatch.Elapsed;
                    processes.ForEach(p => p.Refresh());
                    // Sample iteration time: stop current sample time AFTER we collect "new" metric
                    measureEndTime = _cpuWatch.Elapsed;
                    currentCpuTimings = GetTotalProcessorTimes(processes);
                }
                else
                {
                    // Now start is before we collected metric last time
                    measureStartTime = _lastCpuMeasure; 
                    // Sample iteration time: start next sample time BEFORE we collect "old" metric
                    _lastCpuMeasure = _cpuWatch.Elapsed;
                    processes = GetProcesses();
                    // Sample iteration time: stop current sample time AFTER we collect "new" metric
                    measureEndTime = _cpuWatch.Elapsed;
                    currentCpuTimings = GetTotalProcessorTimes(processes);
                }
                
                var totalMsPassed = (measureEndTime - measureStartTime).TotalMilliseconds;
                var cpuUsagePercentages = currentCpuTimings
                    .Where(u => _lastCpuTimings.ContainsKey(u.Key))
                    .ToImmutableDictionary(u => u.Key, u =>
                    {
                        var timeForProcess = (u.Value - _lastCpuTimings[u.Key]).TotalMilliseconds;
                        return  Math.Min(timeForProcess / (Environment.ProcessorCount * totalMsPassed), 1);
                    });

                _lastCpuTimings = currentCpuTimings;
            
                return (cpuUsagePercentages.GetValueOrDefault(currentProcessId, 0), cpuUsagePercentages.Values.DefaultIfEmpty().Sum());
            }
            finally
            {
                processes?.ForEach(p => p.Dispose());
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
        /// <summary>
        /// Gets the memory used (managed heap size) for the current process.
        /// Handles a known .NET runtime bug where GC.GetTotalMemory() can occasionally
        /// return negative values due to gen0 fragmentation calculations.
        /// Falls back to GCMemoryInfo.HeapSizeBytes or process private memory if needed.
        /// </summary>
        /// <remarks>
        /// See: https://github.com/dotnet/runtime/issues/106712
        /// The bug was introduced in .NET 7 with the regions GC feature and causes
        /// gen0 fragmentation to sometimes be calculated as larger than gen0 size,
        /// resulting in a negative total memory value. As of .NET 9, this is unfixed.
        /// </remarks>
        private static long GetMemoryUsedBytes(Process process)
        {
#if NET6_0_OR_GREATER
            // Use GC.GetTotalMemory(false) to avoid forcing a blocking GC.
            // GC.GetTotalMemory(true) can be extremely slow in containerized environments.
            var memoryUsed = GC.GetTotalMemory(false);

            // Known .NET runtime bug: GC.GetTotalMemory() can return negative values
            // when gen0 fragmentation exceeds gen0 size. Calling GC.GetTotalMemory(true)
            // fixes it, but we avoid that for performance. Instead, we fall back to
            // GCMemoryInfo.HeapSizeBytes when the value is invalid.
            // See: https://github.com/dotnet/runtime/issues/106712
            if (memoryUsed <= 0)
            {
                // Fall back to GCMemoryInfo.HeapSizeBytes which is the total heap size
                var gcMemoryInfo = GC.GetGCMemoryInfo();
                if (gcMemoryInfo.Index > 0 && gcMemoryInfo.HeapSizeBytes > 0)
                {
                    memoryUsed = gcMemoryInfo.HeapSizeBytes;
                }
                else
                {
                    // Final fallback: use private memory size (includes managed + some unmanaged)
                    memoryUsed = process.PrivateMemorySize64;
                }
            }

            return memoryUsed;
#else
            // For .NET Framework / netstandard2.0, GC.GetTotalMemory should work reliably.
            // The bug is specific to .NET 7+ regions GC feature.
            var memoryUsed = GC.GetTotalMemory(false);

            // Still add a fallback for safety
            if (memoryUsed <= 0)
            {
                memoryUsed = process.PrivateMemorySize64;
            }

            return memoryUsed;
#endif
        }

    }
}
