//-----------------------------------------------------------------------
// <copyright file="StandardMetrics.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Cluster.Metrics.Serialization;
using Akka.Util;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// Definitions of the built-in standard metrics.
    ///
    /// The following extractors and data structures makes it easy to consume the <see cref="NodeMetrics"/>
    /// in for example load balancers.
    /// </summary>
    public static class StandardMetrics
    {
        /// <summary>
        /// Total memory allocated to the currently running process (<see cref="GC.GetTotalMemory"/>)
        /// </summary>
        public const string MemoryUsed = "MemoryUsed";
        /// <summary>
        /// Memory, available for the process (<see cref="Process.VirtualMemorySize64"/>)
        /// </summary>
        public const string MemoryAvailable = "MemoryAvailable";
        /// <summary>
        /// Memory limit recommended for current process (<see cref="Process.MaxWorkingSet"/>)
        /// </summary>
        public const string MaxMemoryRecommended = "MaxMemoryRecommended";
        
        /// <summary>
        /// Number of available processors
        /// </summary>
        public const string Processors = "Processors";
        /// <summary>
        /// Contains CPU usage by current process
        /// </summary>
        public const string CpuProcessUsage = "CpuProcessUsage";
        /// <summary>
        /// Contains CPU usage by all processes
        /// </summary>
        public const string CpuTotalUsage = "CpuTotalUsage";

        /// <summary>
        /// Extract <see cref="Memory"/> data from nodeMetrics, if the nodeMetrics
        /// contains necessary memory metrics, otherwise it returns <see cref="Option{T}.None"/>.
        /// </summary>
        public static Option<Memory> ExtractMemory(NodeMetrics nodeMetrics)
        {
            return Memory.Decompose(nodeMetrics).Select(data =>
            {
                return new Memory(data.Address, data.Timestamp, data.UsedSmoothValue, data.AvailableSmoothValue, data.MaxRecommendedSmoothValue);
            });
        }

        /// <summary>
        /// Extract <see cref="Cpu"/> data from nodeMetrics, if the nodeMetrics
        /// contains necessary CPU metrics, otherwise it returns <see cref="Option{T}.None"/>.
        /// </summary>
        public static Option<Cpu> ExtractCpu(NodeMetrics nodeMetrics)
        {
            return Cpu.Decompose(nodeMetrics).Select(data =>
            {
                return new Cpu(data.Address, data.Timestamp, data.CpuProcessUsage, data.CpuTotalUsage, data.Processors);
            });
        }

        /// <summary>
        /// Allocated memory metric
        /// </summary>
        public sealed class Memory
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
            /// The current process allocated memory (in bytes) (<see cref="GC.GetTotalMemory"/>)
            /// </summary>
            public double Used { get; }
            /// <summary>
            /// Memory available for current process (in bytes) (<see cref="Process.VirtualMemorySize64"/>)
            /// </summary>
            public double Available { get; }
            /// <summary>
            /// Max memory recommended for process (in bytes) (<see cref="Process.MaxWorkingSet"/>)
            /// </summary>
            public Option<double> MaxRecommended { get; }

            /// <summary>
            /// Given a NodeMetrics it returns the HeapMemory data if the nodeMetrics contains necessary heap metrics.
            /// </summary>
            /// <returns>If possible a tuple matching the HeapMemory constructor parameters</returns>
            public static Option<(Actor.Address Address, long Timestamp, double UsedSmoothValue, double AvailableSmoothValue, Option<double> MaxRecommendedSmoothValue)> 
                Decompose(NodeMetrics nodeMetrics)
            {
                var used = nodeMetrics.Metric(MemoryUsed);
                var available = nodeMetrics.Metric(MemoryAvailable);
                
                if (!used.HasValue || !available.HasValue)
                    return Option<(Actor.Address, long, double, double, Option<double>)>.None;

                return (
                    nodeMetrics.Address,
                    nodeMetrics.Timestamp,
                    used.Value.SmoothValue,
                    available.Value.SmoothValue,
                    nodeMetrics.Metric(MaxMemoryRecommended).Select(v => v.SmoothValue)
                );
            }

            /// <summary>
            /// Creates instance of <see cref="StandardMetrics.Memory"/>
            /// </summary>
            /// <param name="address">Address index of the node the metrics are gathered at</param>
            /// <param name="timestamp">The time of sampling, in milliseconds since midnight, January 1, 1970 UTC</param>
            /// <param name="used">Total memory allocated to the currently running process (in bytes)</param>
            /// <param name="available">Memory available for current process (in bytes)</param>
            /// <param name="max">Max memory recommended for process (in bytes)</param>
            public Memory(Actor.Address address, long timestamp, double used, double available, Option<double> max)
            {
                if (used <= 0)
                    throw new ArgumentException(nameof(used), $"{used} expected to be > 0 bytes");
                if (available <= 0)
                    throw new ArgumentException(nameof(used), $"{available} expected to be > 0 bytes");
                if (max.HasValue && max.Value < 0)
                    throw new ArgumentException(nameof(max), $"{max.Value} expected to be > 0 bytes");
                    
                Address = address;
                Timestamp = timestamp;
                Used = used;
                Available = available;
                MaxRecommended = max;
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
            /// CPU usage by current process in percentage (in [0, 1] range)
            /// </summary>
            public double ProcessUsage { get; }
            /// <summary>
            /// CPU usage by all processes in percentage (in [0, 1] range)
            /// </summary>
            public double TotalUsage { get; }
            /// <summary>
            /// The number of available processors
            /// </summary>
            public int ProcessorsNumber { get; }

            /// <summary>
            /// Given a NodeMetrics it returns the Cpu data if the nodeMetrics contains necessary heap metrics.
            /// </summary>
            /// <returns>If possible a tuple matching the Cpu constructor parameters</returns>
            public static Option<(Actor.Address Address, long Timestamp, double CpuProcessUsage, double CpuTotalUsage, int Processors)> Decompose(NodeMetrics nodeMetrics)
            {
                var processors = nodeMetrics.Metric(Processors);
                var cpuProcessUsage = nodeMetrics.Metric(CpuProcessUsage);
                var cpuTotalUsage = nodeMetrics.Metric(CpuTotalUsage);
                
                if (!processors.HasValue || !cpuProcessUsage.HasValue || !cpuTotalUsage.HasValue)
                    return Option<(Actor.Address, long, double, double, int)>.None;

                return (
                    nodeMetrics.Address,
                    nodeMetrics.Timestamp,
                    cpuProcessUsage.Value.SmoothValue,
                    cpuTotalUsage.Value.SmoothValue,
                    (int)processors.Value.Value.LongValue
                );
            }

            /// <summary>
            /// Creates new instance of <see cref="Cpu"/>
            /// </summary>
            /// <param name="address">Address of the node the metrics are gathered at</param>
            /// <param name="timestamp">The time of sampling, in milliseconds since midnight, January 1, 1970 UTC</param>
            /// <param name="cpuProcessUsage">CPU usage by current process in percentage (in [0, 1] range)</param>
            /// <param name="cpuTotalUsage">CPU usage by all processes in percentage (in [0, 1] range)</param>
            /// <param name="processorsNumber">The number of available processors</param>
            public Cpu(Actor.Address address, long timestamp, double cpuProcessUsage, double cpuTotalUsage, int processorsNumber)
            {
                if (cpuProcessUsage < 0 || cpuProcessUsage > 1)
                    throw new ArgumentException(nameof(cpuProcessUsage), $"{nameof(cpuProcessUsage)} must be between [0.0 - 1.0], was {cpuProcessUsage}" );
                if (cpuTotalUsage < 0 || cpuTotalUsage > 1)
                    throw new ArgumentException(nameof(cpuTotalUsage), $"{nameof(cpuTotalUsage)} must be between [0.0 - 1.0], was {cpuTotalUsage}" );
                
                Address = address;
                Timestamp = timestamp;
                ProcessUsage = cpuProcessUsage;
                TotalUsage = cpuTotalUsage;
                ProcessorsNumber = processorsNumber;
            }
        }
    }
}
