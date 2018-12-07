#region copyright
//-----------------------------------------------------------------------
// <copyright file="SimpleStreamingComparisonBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Akka.Benchmarks.Streams
{
    [Config(typeof(MonitoringConfig))]
    [SimpleJob(RunStrategy.Monitoring, launchCount: 3, warmupCount: 3, targetCount: 3)]
    public class SimpleStreamingComparisonBenchmarks
    {
        private int EventCount = 10_000_000;
        private ActorSystem _system;
        private IMaterializer _materializer;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create(nameof(SimpleStreamingComparisonBenchmarks));
            _materializer = _system.Materializer();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark]
        public Task AkkaStreamsSimpleLinearStream()
        {
            // this benchmark includes time spend on materialization
            //TODO: move materialization to Setup, so we measure only processing itself?
            return Source.From(Enumerable.Range(1, EventCount))
                .Select(i => i + 1)
                .Where(i => i % 2 == 0)
                .RunSum((acc, i) => acc + i, _materializer);
        }

        [Benchmark]
        public Task<int> AsyncEnumerableSimpleLinearStream()
        {
            return AsyncEnumerable.Range(1, EventCount)
                .Select(i => i + 1)
                .Where(i => i % 2 == 0)
                .Aggregate((acc, i) => acc + i);
        }

        [Benchmark]
        public async Task<int> ObservableSimpleLinearStream()
        {
            var result = await Observable.Range(1, EventCount)
                .Select(i => i + 1)
                .Where(i => i % 2 == 0)
                .Aggregate((acc, i) => acc + i)
                .GetAwaiter();

            return result;
        }

        [Benchmark]
        public async Task DataFlowSimpleLinearStream()
        {
            var sum = 0;

            var plusOne = new TransformBlock<int, int>(i => i + i);
            var even = new TransformBlock<int, int>(i => i + 1);
            var reduce = new ActionBlock<int>(i => Interlocked.Add(ref sum, i));

            plusOne.LinkTo(even, new DataflowLinkOptions { PropagateCompletion = true });
            even.LinkTo(reduce, new DataflowLinkOptions { PropagateCompletion = true });

            for (int i = 0; i < EventCount; i++)
            {
                plusOne.Post(i);
            }

            plusOne.Complete();

            await Task.WhenAll(plusOne.Completion, even.Completion, reduce.Completion);
        }
    }
}