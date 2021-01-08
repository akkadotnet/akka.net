//-----------------------------------------------------------------------
// <copyright file="StandardOutWriterMemoryBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using NBench;

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
            // disable SO so we don't fill up the build log with garbage
            Console.SetOut(new StreamWriter(Stream.Null));
            _consoleWriteThroughputCounter = context.GetCounter(ConsoleWriteThroughputCounterName);
        }

        [PerfBenchmark(Description = "Testing to see if the design of the StandardOutWriter produces allocations",
            RunMode = RunMode.Throughput,
            NumberOfIterations = 13, TestMode = TestMode.Measurement, RunTimeMilliseconds = 1000)]
        [CounterMeasurement(ConsoleWriteThroughputCounterName)]
        [MemoryAssertion(MemoryMetric.TotalBytesAllocated, MustBe.LessThan, ByteConstants.SixtyFourKb)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]

        public void StressTestStandardOutWriter(BenchmarkContext context)
        {
            StandardOutWriter.WriteLine(InputStr, ConsoleColor.Black, ConsoleColor.DarkGreen);
            _consoleWriteThroughputCounter.Increment();
        }

        [PerfCleanup]
        public void CleanUp()
        {
            // cleanup SO
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()));
        }
    }
}
