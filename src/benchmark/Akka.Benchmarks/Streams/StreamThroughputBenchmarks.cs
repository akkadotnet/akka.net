//-----------------------------------------------------------------------
// <copyright file="StreamThroughputBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class StreamThroughputBenchmarks
    {
        private const int ElementCount = 100_000;

        private ActorSystem _system;
        private ActorMaterializer _materializer;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("stream-throughput-bench");
            _materializer = _system.Materializer();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Linear_Pipeline_Throughput()
        {
            return Source.From(Enumerable.Range(0, ElementCount))
                .Select(x => x + 1)
                .Select(x => x * 2)
                .RunWith(Sink.Ignore<int>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Deep_Pipeline_5_Stages_Throughput()
        {
            return Source.From(Enumerable.Range(0, ElementCount))
                .Select(x => x + 1)
                .Select(x => x * 2)
                .Select(x => x - 1)
                .Select(x => x + 3)
                .Select(x => x * 4)
                .RunWith(Sink.Ignore<int>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Merge_Fan_In_Throughput()
        {
            var half = ElementCount / 2;
            var source1 = Source.From(Enumerable.Range(0, half));
            var source2 = Source.From(Enumerable.Range(half, half));

            return Source.Combine(source1, source2, i => new Merge<int>(i))
                .RunWith(Sink.Ignore<int>(), _materializer);
        }

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Batch_Fan_In_Throughput()
        {
            return Source.From(Enumerable.Range(0, ElementCount))
                .Batch(10, i => new List<int> { i }, (list, i) => { list.Add(i); return list; })
                .SelectMany(x => x)
                .RunWith(Sink.Ignore<int>(), _materializer);
        }
    }
}
