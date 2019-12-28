// //-----------------------------------------------------------------------
// // <copyright file="StandardMetrics.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Util;

namespace Akka.Cluster.Metrics.Serialization
{
    /// <summary>
    /// Definitions of the built-in standard metrics.
    ///
    /// The following extractors and data structures makes it easy to consume the <see cref="NodeMetrics"/>
    /// in for example load balancers.
    /// </summary>
    public static class StandardMetrics
    {
        // Constants for the heap related Metric names
        public const string HeapMemoryUsed = "heap-memory-used";
        public const string HeapMemoryCommitted = "heap-memory-committed";
        public const string HeapMemoryMax = "heap-memory-max";
        
        // Constants for the cpu related Metric names
        public const string SystemLoadAverageName = "system-load-average";
        public const string Processors = "processors";
        // In latest Linux kernels: CpuCombined + CpuStolen + CpuIdle = 1.0  or 100%.
        /** Sum of User + Sys + Nice + Wait. See `org.hyperic.sigar.CpuPerc` */
        public const string CpuCombinedName = "cpu-combined";

        /** The amount of CPU 'stolen' from this virtual machine by the hypervisor for other tasks (such as running another virtual machine). */
        public const string CpuStolenName = "cpu-stolen";

        /** Amount of CPU time left after combined and stolen are removed. */
        public const string CpuIdleName = "cpu-idle";

        /// <summary>
        /// The amount of used and committed memory will always be &lt;= max if max is defined.
        /// A memory allocation may fail if it attempts to increase the used memory such that used &gt; committed
        /// even if used &lt;= max is true (e.g. when the system virtual memory is low).
        /// </summary>
        public sealed class HeapMemory
        {
            /// <summary>
            /// Address of the node the metrics are gathered at
            /// </summary>
            public Actor.Address Address { get; }
            /// <summary>
            /// The time of sampling, in milliseconds since midnight, January 1, 1970 UTC
            /// </summary>
            public long Timestamp { get; }
            /// <summary>
            /// The current sum of heap memory used from all heap memory pools (in bytes)
            /// </summary>
            public decimal Used { get; }
            /// <summary>
            /// The current sum of heap memory guaranteed to be available to the runtime
            /// from all heap memory pools (in bytes). Committed will always be greater than or equal to used.
            /// </summary>
            public decimal Committed { get; }
            /// <summary>
            /// The maximum amount of memory (in bytes) that can be used for runtime memory management.
            /// Can be undefined on some OS.
            /// </summary>
            public Option<decimal> Max { get; }

            /// <summary>
            /// Given a NodeMetrics it returns the HeapMemory data if the nodeMetrics contains necessary heap metrics.
            /// </summary>
            /// <returns>If possible a tuple matching the HeapMemory constructor parameters</returns>
            public static Option<(Actor.Address Address, long Timestamp, double UsedSmoothValue, double CommittedSmoothValue, Option<double> HeapMemoryMaxValue)> 
                Decompose(NodeMetrics nodeMetrics)
            {
                var used = nodeMetrics.Metric(HeapMemoryUsed);
                var committed = nodeMetrics.Metric(HeapMemoryCommitted);
                
                if (!used.HasValue || !committed.HasValue)
                    return Option<(Actor.Address, long, double, double, Option<double>)>.None;

                return (
                    nodeMetrics.Address,
                    nodeMetrics.Timestamp,
                    used.Value.SmoothValue,
                    committed.Value.SmoothValue,
                    nodeMetrics.Metric(HeapMemoryMax).Select(m => m.SmoothValue)
                );
            }

            /// <summary>
            /// Creates instance of <see cref="StandardMetrics.HeapMemoryUsed"/>
            /// </summary>
            /// <param name="address">Address index of the node the metrics are gathered at</param>
            /// <param name="timestamp">The time of sampling, in milliseconds since midnight, January 1, 1970 UTC</param>
            /// <param name="used">The current sum of heap memory used from all heap memory pools (in bytes)</param>
            /// <param name="committed">
            /// The current sum of heap memory guaranteed to be available to the runtime
            /// from all heap memory pools (in bytes). Committed will always be greater than or equal to used.
            /// </param>
            /// <param name="max">
            /// The maximum amount of memory (in bytes) that can be used for runtime memory management.
            /// Can be undefined on some OS.
            /// </param>
            public HeapMemory(Actor.Address address, long timestamp, decimal used, decimal committed, Option<decimal> max)
            {
                if (committed <= 0)
                    throw new ArgumentException(nameof(committed), "committed heap expected to be > 0 bytes");
                if (max.HasValue && max.Value <= 0)
                    throw new ArgumentException(nameof(committed), "max heap expected to be > 0 bytes");
                    
                Address = address;
                Timestamp = timestamp;
                Used = used;
                Committed = committed;
                Max = max;
            }
        }

        /// <summary>
        /// CPU metrics
        /// </summary>
        public sealed class Cpu
        {
            /// <summary>
            /// Address of the node the metrics are gathered at
            /// </summary>
            public Actor.Address Address { get; }
            /// <summary>
            /// The time of sampling, in milliseconds since midnight, January 1, 1970 UTC
            /// </summary>
            public long Timestamp { get; }
            /// <summary>
            /// OS-specific average load on the CPUs in the system, for the past 1 minute,
            /// The system is possibly nearing a bottleneck if the system load average is nearing number of cpus/cores.
            /// </summary>
            public Option<decimal> SystemLoadAverage { get; }
            /// <summary>
            /// Combined CPU sum of User + Sys + Nice + Wait, in percentage ([0.0 - 1.0]. This
            /// metric can describe the amount of time the CPU spent executing code during n-interval and how
            /// much more it could theoretically.
            /// </summary>
            public Option<decimal> CpuCombined { get; }
            /// <summary>
            /// Stolen CPU time, in percentage ([0.0 - 1.0].
            /// </summary>
            public Option<decimal> CpuStolen { get; }
            /// <summary>
            /// The number of available processors
            /// </summary>
            public int ProcessorsNumber { get; }

            /// <summary>
            /// Given a NodeMetrics it returns the Cpu data if the nodeMetrics contains necessary heap metrics.
            /// </summary>
            /// <returns>If possible a tuple matching the Cpu constructor parameters</returns>
            public static Option<(Actor.Address Address, long Timestamp, Option<double> SystemLoadAverage, Option<double> CpuCombined, Option<double> CpuStolen, int Processors)> 
                Decompose(NodeMetrics nodeMetrics)
            {
                var processors = nodeMetrics.Metric(Processors);
                
                if (!processors.HasValue)
                    return Option<(Actor.Address, long, Option<double>, Option<double>, Option<double>, int)>.None;

                return (
                    nodeMetrics.Address,
                    nodeMetrics.Timestamp,
                    nodeMetrics.Metric(SystemLoadAverageName).Select(v => v.SmoothValue),
                    nodeMetrics.Metric(CpuCombinedName).Select(v => v.SmoothValue),
                    nodeMetrics.Metric(CpuStolenName).Select(v => v.SmoothValue),
                    (int)processors.Value.Number.DecimalValue
                );
            }

            /// <summary>
            /// Creates new instance of <see cref="Cpu"/>
            /// </summary>
            /// <param name="address">Address of the node the metrics are gathered at</param>
            /// <param name="timestamp">The time of sampling, in milliseconds since midnight, January 1, 1970 UTC</param>
            /// <param name="systemLoadAverage">OS-specific average load on the CPUs in the system, for the past 1 minute,
            /// The system is possibly nearing a bottleneck if the system load average is nearing number of cpus/cores.</param>
            /// <param name="cpuCombined">Combined CPU sum of User + Sys + Nice + Wait, in percentage ([0.0 - 1.0]. This
            /// metric can describe the amount of time the CPU spent executing code during n-interval and how
            /// much more it could theoretically.</param>
            /// <param name="cpuStolen">Stolen CPU time, in percentage ([0.0 - 1.0].</param>
            /// <param name="processorsNumber">The number of available processors</param>
            public Cpu(Actor.Address address, long timestamp, Option<decimal> systemLoadAverage, Option<decimal> cpuCombined,
                       Option<decimal> cpuStolen, int processorsNumber)
            {
                if (cpuCombined.HasValue && (cpuCombined.Value < 0 || cpuCombined.Value > 1))
                    throw new ArgumentException(nameof(cpuCombined), $"cpuCombined must be between [0.0 - 1.0], was {cpuCombined.Value}" );
                if (cpuStolen.HasValue && (cpuStolen.Value < 0 || cpuStolen.Value > 1))
                    throw new ArgumentException(nameof(cpuCombined), $"cpuStolen must be between [0.0 - 1.0], was {cpuStolen.Value}" );
                
                Address = address;
                Timestamp = timestamp;
                SystemLoadAverage = systemLoadAverage;
                CpuCombined = cpuCombined;
                CpuStolen = cpuStolen;
                ProcessorsNumber = processorsNumber;
            }
        }
    }
}