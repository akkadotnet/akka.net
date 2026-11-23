//-----------------------------------------------------------------------
// <copyright file="StreamsHandoffBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Cross-thread hand-off paths that Artery's outbound lanes use: several producer streams feeding one
    /// <see cref="MergeHub"/>, and a <see cref="ChannelSource"/> whose reader is empty on every pull (the
    /// light-load request/response shape, where each element waits on the channel's read continuation).
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class StreamsHandoffBenchmarks
    {
        private const int HubElements = 100_000;
        private const int PingPongElements = 10_000;

        private sealed class Payload
        {
            public int Value;
        }

        private ActorSystem _system;
        private ActorMaterializer _materializer;
        private Payload[] _elements;

        [Params(1, 4)]
        public int Producers;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("streams-handoff-bench");
            _materializer = _system.Materializer();
            _elements = Enumerable.Range(0, HubElements).Select(i => new Payload { Value = i }).ToArray();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _materializer.Dispose();
            _system.Terminate().Wait();
        }

        [Benchmark(OperationsPerInvoke = HubElements)]
        public async Task MergeHub_Producers()
        {
            var (sink, done) = MergeHub.Source<Payload>(16)
                .Take(HubElements)
                .ToMaterialized(Sink.Ignore<Payload>(), Keep.Both)
                .Run(_materializer);

            var chunk = HubElements / Producers;
            for (var p = 0; p < Producers; p++)
                Source.From(new ArraySegment<Payload>(_elements, p * chunk, chunk)).RunWith(sink, _materializer);

            await done;
        }

        [Benchmark(OperationsPerInvoke = PingPongElements)]
        public async Task ChannelSource_PingPong()
        {
            var channel = Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
            var turn = new SemaphoreSlim(0);
            var done = ChannelSource.FromReader(channel.Reader)
                .Take(PingPongElements)
                .RunWith(Sink.ForEach<Payload>(_ => turn.Release()), _materializer);

            for (var i = 0; i < PingPongElements; i++)
            {
                channel.Writer.TryWrite(_elements[i]);
                await turn.WaitAsync();
            }

            await done;
        }
    }
}
