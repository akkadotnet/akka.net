//-----------------------------------------------------------------------
// <copyright file="ActorPathEqualitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    /// <summary>
    /// Compares the performance of <see cref="ActorPath.Equals(object)"/> and <see cref="ActorPath.GetHashCode"/>
    /// </summary>
    public class ActorPathEqualitySpec
    {
        private const string EqualsCounterName = "EqualsOp";

        private const double MinimumAcceptableOperationsPerSecond = 1000000.0d; //million op / second

        private static readonly RootActorPath RootAddress = new RootActorPath(Address.AllSystems);
        private static readonly ActorPath TopLevelActorPath = RootAddress / "user" / "foo1";
        private Counter _equalsThroughput;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _equalsThroughput = context.GetCounter(EqualsCounterName);
        }

        [PerfBenchmark(
            Description =
                "Tests how quickly ActorPath.Equals can perform against two equal paths (worst case performance)",
            RunMode = RunMode.Throughput, NumberOfIterations = 13, RunTimeMilliseconds = 1000,
            TestMode = TestMode.Measurement)]
        [CounterThroughputAssertion(EqualsCounterName, MustBe.GreaterThan, MinimumAcceptableOperationsPerSecond
        )]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void ActorPath_Equals_throughput(BenchmarkContext context)
        {
            var b = TopLevelActorPath.Equals(TopLevelActorPath);
            _equalsThroughput.Increment();
        }

        [PerfBenchmark(
            Description =
                "Tests how quickly ActorPath.Equals can perform against two equal paths (worst case performance)",
            RunMode = RunMode.Throughput, NumberOfIterations = 13, RunTimeMilliseconds = 1000,
            TestMode = TestMode.Measurement)]
        [CounterThroughputAssertion(EqualsCounterName, MustBe.GreaterThan, MinimumAcceptableOperationsPerSecond
        )]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void ActorPath_GetHashCode_throughput(BenchmarkContext context)
        {
            var b = TopLevelActorPath.GetHashCode();
            _equalsThroughput.Increment();
        }
    }
}
