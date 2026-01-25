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
            // Note: We no longer force a GC here because GC.Collect() can be slow in
            // containerized environments. Instead, GetAvailableMemoryBytes() checks
            // GCMemoryInfo.Index and falls back to process.WorkingSet64 if no GC has occurred.
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

                // Use GC.GetTotalMemory(false) to avoid forcing a blocking GC.
                // GC.GetTotalMemory(true) can be extremely slow (seconds to minutes) in containerized
                // or memory-pressured environments like CI/CD, potentially causing test timeouts.
                // The non-forcing version returns a reasonably accurate approximation.
                var memoryUsedValue = GC.GetTotalMemory(false);
                var totalMemory = NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryUsed, memoryUsedValue);
                if (totalMemory.HasValue)
                    metrics.Add(totalMemory.Value);

                // Get available memory - the amount of memory available for the process/runtime
                var availableMemoryValue = GetAvailableMemoryBytes(process);
                var availableMemory = NodeMetrics.Types.Metric.Create(StandardMetrics.MemoryAvailable, availableMemoryValue);
                if (availableMemory.HasValue)
                    metrics.Add(availableMemory.Value);

                // Diagnostic: If either critical metric is missing, throw with details
                // This helps diagnose CI failures where metrics silently fail to be created
                if (!totalMemory.HasValue || !availableMemory.HasValue)
                {
                    var workingSet = process.WorkingSet64;
#if NET6_0_OR_GREATER
                    var gcInfo = GC.GetGCMemoryInfo();
                    throw new InvalidOperationException(
                        $"Failed to create memory metrics. " +
                        $"MemoryUsed: value={memoryUsedValue}, created={totalMemory.HasValue}. " +
                        $"MemoryAvailable: value={availableMemoryValue}, created={availableMemory.HasValue}. " +
                        $"Diagnostics: WorkingSet64={workingSet}, GCMemoryInfo.Index={gcInfo.Index}, " +
                        $"TotalAvailableMemoryBytes={gcInfo.TotalAvailableMemoryBytes}, " +
                        $"HighMemoryLoadThresholdBytes={gcInfo.HighMemoryLoadThresholdBytes}");
#else
                    throw new InvalidOperationException(
                        $"Failed to create memory metrics. " +
                        $"MemoryUsed: value={memoryUsedValue}, created={totalMemory.HasValue}. " +
                        $"MemoryAvailable: value={availableMemoryValue}, created={availableMemory.HasValue}. " +
                        $"Diagnostics: WorkingSet64={workingSet}");
#endif
                }

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
        /// Gets the available memory bytes for the current process/runtime.
        /// Uses GCMemoryInfo on .NET 6+ for accurate container-aware values,
        /// falls back to process working set on older frameworks.
        /// </summary>
        private static long GetAvailableMemoryBytes(Process process)
        {
#if NET6_0_OR_GREATER
            try
            {
                // Use GCMemoryInfo for accurate, container-aware memory information.
                // TotalAvailableMemoryBytes represents the total memory available to the GC,
                // respecting container cgroup limits. This is the correct semantic for
                // "available memory" in the context of cluster load balancing.
                var gcMemoryInfo = GC.GetGCMemoryInfo();

                // If Index is 0, no GC has occurred yet and all values will be 0.
                // Fall back to process working set in this case.
                // See: https://learn.microsoft.com/en-us/dotnet/api/system.gc.getgcmemoryinfo
                if (gcMemoryInfo.Index == 0)
                {
                    return process.WorkingSet64;
                }

                var availableBytes = gcMemoryInfo.TotalAvailableMemoryBytes;

                // TotalAvailableMemoryBytes can return 0 or negative on some platforms
                // (e.g., ARM32, certain container configurations).
                // Fall back to HighMemoryLoadThresholdBytes which represents the threshold
                // before the GC considers memory pressure.
                if (availableBytes <= 0)
                {
                    availableBytes = gcMemoryInfo.HighMemoryLoadThresholdBytes;
                }

                // If still invalid, fall back to process working set
                if (availableBytes <= 0)
                {
                    availableBytes = process.WorkingSet64;
                }

                return availableBytes;
            }
            catch
            {
                // If GC memory info throws for any reason, fall back to working set
                return process.WorkingSet64;
            }
#else
            // For .NET Framework / netstandard2.0, GCMemoryInfo is not available.
            // Use process working set as the best available approximation.
            // This represents the physical memory currently in use by the process.
            return process.WorkingSet64;
#endif
        }
    }
}
