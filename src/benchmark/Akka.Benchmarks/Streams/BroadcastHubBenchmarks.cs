//-----------------------------------------------------------------------
// <copyright file="BroadcastHubBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Benchmarks issue #7253's high-consumer BroadcastHub shape.
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class BroadcastHubBenchmarks
    {
        private const int MessageCount = 512;
        private const int BufferSize = 256;

        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka {
  loglevel = WARNING
  stdout-loglevel = WARNING
  log-dead-letters = off
  log-dead-letters-during-shutdown = off
}
");

        private ActorSystem _system;
        private ActorMaterializer _materializer;

        [Params(100, 1000, 5000, 14000)]
        public int ConsumerCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("broadcast-hub-bench", Config);
            _materializer = _system.Materializer();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _materializer?.Dispose();
            _system?.Dispose();
        }

        [Benchmark]
        public Task BroadcastHub_Lockstep_Consumers()
        {
            return RunBroadcastHubScenario(
                sourceFactory: hubSource => hubSource,
                expectedElements: MessageCount);
        }

        [Benchmark]
        public Task BroadcastHub_Filter_Per_Consumer()
        {
            return RunBroadcastHubScenario(
                sourceFactory: (hubSource, consumerIndex) => hubSource.Where(element => element == consumerIndex % MessageCount),
                expectedElements: 1);
        }

        [Benchmark]
        public Task BroadcastHub_Jittered_Consumers()
        {
            return RunBroadcastHubScenario(
                sourceFactory: (hubSource, consumerIndex) =>
                    consumerIndex % 16 == 0
                        ? hubSource.SelectAsync(1, element => Task.FromResult(element))
                        : hubSource,
                expectedElements: MessageCount);
        }

        private Task RunBroadcastHubScenario(Func<Source<int, NotUsed>, Source<int, NotUsed>> sourceFactory, int expectedElements)
            => RunBroadcastHubScenario((hubSource, _) => sourceFactory(hubSource), expectedElements);

        private async Task RunBroadcastHubScenario(Func<Source<int, NotUsed>, int, Source<int, NotUsed>> sourceFactory, int expectedElements)
        {
            var hubSource = Source.From(Enumerable.Range(0, MessageCount))
                .RunWith(BroadcastHub.Sink<int>(ConsumerCount, BufferSize), _materializer);

            var consumerTasks = Enumerable.Range(0, ConsumerCount)
                .Select(consumerIndex => sourceFactory(hubSource, consumerIndex)
                    .RunWith(Sink.Aggregate<int, int>(0, (count, _) => count + 1), _materializer))
                .ToArray();

            var results = await Task.WhenAll(consumerTasks).ConfigureAwait(false);

            for (var i = 0; i < results.Length; i++)
            {
                if (results[i] != expectedElements)
                    throw new InvalidOperationException($"Consumer [{i}] received [{results[i]}] elements, expected [{expectedElements}].");
            }
        }
    }
}
