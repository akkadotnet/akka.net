#region copyright
// -----------------------------------------------------------------------
//  <copyright file="SelectAsyncBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class SelectAsyncBenchmarks
    {
        private const int Operations = 100_000;

        [Params(1, 4)]
        public int Parallelism;

        /// <summary>
        /// If true, an immediately completed task will be used. Otherwise an yielded one will be used.
        /// </summary>
        [Params(true, false)]
        public bool CompletedTask;

        private ActorSystem _system;
        private IMaterializer _materializer;
        private Source<int, NotUsed> _source;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create(nameof(SelectAsyncBenchmarks));
            _materializer = _system.Materializer();
            _source = Source.From(Enumerable.Range(1, Operations));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }
        
        [Benchmark(OperationsPerInvoke = Operations)]
        public void SelectAsync()
        {
            var latch = new CountdownEvent(1);

            _source.SelectAsync(Parallelism, Selector())
                .RunWith(new LatchSink<int>(Operations, latch), _materializer);

            AwaitLatch(latch);
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void SelectAsyncUnordered()
        {
            var latch = new CountdownEvent(1);

            _source.SelectAsyncUnordered(Parallelism, Selector())
                .RunWith(new LatchSink<int>(Operations, latch), _materializer);

            AwaitLatch(latch);
        }
        
        private Func<int, Task<int>> Selector()
        {
            if (CompletedTask)
                return i => Task.FromResult(i);
            else
                return async i =>
                {
                    await Task.Yield();
                    return i;
                };
        }

        private void AwaitLatch(CountdownEvent latch)
        {
            if (!latch.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new Exception("Benchmark didn't finished in 30s");
            }
        }
    }
}