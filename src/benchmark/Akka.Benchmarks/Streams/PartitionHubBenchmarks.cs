#region copyright
// -----------------------------------------------------------------------
//  <copyright file="PartitionHubBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class PartitionHubBenchmarks
    {
        private const int Operations = 100_000;

        [Params(2, 5, 10, 20, 30)]
        public int NrOfStreams;

        private ActorSystem _system;
        private IMaterializer _materializer;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create(nameof(PartitionHubBenchmarks));
            _materializer = _system.Materializer();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void Partition()
        {
            var source = Source.From(Enumerable.Range(1, Operations))
                .RunWith(PartitionHub.Sink<int>((size, i) => i % NrOfStreams, NrOfStreams), _materializer);

            var latch = new CountdownEvent(NrOfStreams);

            for (int i = 0; i < NrOfStreams; i++)
            {
                source.RunWith(new LatchSink<int>(Operations / NrOfStreams, latch), _materializer);
            }

            if (!latch.Wait(TimeSpan.FromSeconds(30)))
            {
                Fail();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Fail() => throw new Exception("Test didn't finished in 30sec");
    }
}