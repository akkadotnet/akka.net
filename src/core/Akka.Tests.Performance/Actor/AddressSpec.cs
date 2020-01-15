//-----------------------------------------------------------------------
// <copyright file="AddressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    /// <summary>
    /// Tests for verifying the throughput and memory of various <see cref="Address"/>-related
    /// operations.
    /// </summary>
    public class AddressSpec
    {
        private Counter _parseThroughput;
        private const string ParseThroughputCounterName = "ParseOp";
        private const double MinimumAcceptableOperationsPerSecond = 1000000.0d; //million op / second

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _parseThroughput = context.GetCounter(ParseThroughputCounterName);
        }

        [PerfBenchmark(Description = "Tests how quickly `Address.Parse` can run on a LOCAL address", RunMode = RunMode.Throughput, NumberOfIterations = 13, RunTimeMilliseconds = 1000, TestMode = TestMode.Measurement)]
        [CounterThroughputAssertion(ParseThroughputCounterName, MustBe.GreaterThan, MinimumAcceptableOperationsPerSecond)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void Parse_local_throughput(BenchmarkContext context)
        {
            Address.Parse("akka.tcp://sys@localhost:9091");
            _parseThroughput.Increment();
        }

        [PerfBenchmark(Description = "Tests how quickly `Address.Parse` can run on a REMOTE address", RunMode = RunMode.Throughput, NumberOfIterations = 13, RunTimeMilliseconds = 1000, TestMode = TestMode.Measurement)]
        [CounterThroughputAssertion(ParseThroughputCounterName, MustBe.GreaterThan, MinimumAcceptableOperationsPerSecond)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void Parse_remote_throughput(BenchmarkContext context)
        {
            Address.Parse("akka://sys");
            _parseThroughput.Increment();
        }

        [PerfBenchmark(Description = "Tests how much memory 100,000 `Address` instances consume",
            RunMode = RunMode.Iterations, NumberOfIterations = 13, RunTimeMilliseconds = 1000,
            TestMode = TestMode.Measurement)]
        [CounterMeasurement(ParseThroughputCounterName)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void Memory_footprint(BenchmarkContext context)
        {
            var actorPaths = new Address[100000];
            for (var i = 0; i < 100000;)
            {
                actorPaths[i] = new Address("akka", "foo", "localhost", 9091);
                ++i;
                _parseThroughput.Increment();
            }
        }
    }
}
