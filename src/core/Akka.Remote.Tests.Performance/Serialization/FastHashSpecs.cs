using Akka.Remote.Serialization;
using NBench;

namespace Akka.Remote.Tests.Performance.Serialization
{
    public class FastHashSpecs
    {
        private const string HashCounterName = "hashes";
        private Counter _hashOpCounter;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _hashOpCounter = context.GetCounter(HashCounterName);
        }

        [PerfBenchmark(RunMode = RunMode.Throughput, NumberOfIterations = 13)]
        [CounterMeasurement(HashCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void FastHashSafe(BenchmarkContext context)
        {
            FastHash.OfString(HashCounterName);
            _hashOpCounter.Increment();
        }

        [PerfBenchmark(RunMode = RunMode.Throughput, NumberOfIterations = 13)]
        [CounterMeasurement(HashCounterName)]
        [GcMeasurement(GcMetric.TotalCollections, GcGeneration.AllGc)]
        public void FastHashUnsafe(BenchmarkContext context)
        {
            FastHash.OfStringFast(HashCounterName);
            _hashOpCounter.Increment();
        }
    }
}
