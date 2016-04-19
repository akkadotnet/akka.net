﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using NBench;
using NBench.PerformanceCounters;

namespace Akka.Tests.Performance.Util
{
    /// <summary>
    /// Testing to see if the use of delegates inside <see cref="StandardOutWriter"/>
    /// results in allocations.
    /// </summary>
    public class StandardOutWriterMemoryBenchmark
    {
        private Counter _consoleWriteThroughputCounter;
        private const string ConsoleWriteThroughputCounterName = "StandardOutWrites";
        private const string InputStr = "W"; // want to avoid string allocations for this spec

        [PerfSetup]
        public void SetUp(BenchmarkContext context)
        {
            _consoleWriteThroughputCounter = context.GetCounter(ConsoleWriteThroughputCounterName);
        }

        [PerfBenchmark(Description = "Testing to see if the design of the StandardOutWriter produces allocations",
            RunMode = RunMode.Throughput,
            NumberOfIterations = 13, TestMode = TestMode.Measurement, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(ConsoleWriteThroughputCounterName)]
        [MemoryAssertion(MemoryMetric.TotalBytesAllocated, MustBe.LessThan, ByteConstants.SixtyFourKb)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        [PerformanceCounterMeasurement(".NET CLR Memory", "# of Pinned Objects", InstanceName = NBenchPerformanceCounterConstants.CurrentProcessName, UnitName = "objects")]
        [PerformanceCounterMeasurement(".NET CLR Memory", "# Bytes in all Heaps", InstanceName = NBenchPerformanceCounterConstants.CurrentProcessName, UnitName = "bytes")]
        [PerformanceCounterMeasurement(".NET CLR Memory", "# GC Handles", InstanceName = NBenchPerformanceCounterConstants.CurrentProcessName, UnitName = "handles")]
        public void StressTestStandardOutWriter(BenchmarkContext context)
        {
            StandardOutWriter.WriteLine(InputStr, ConsoleColor.Black, ConsoleColor.DarkGreen);
            _consoleWriteThroughputCounter.Increment();
        }
    }
}
