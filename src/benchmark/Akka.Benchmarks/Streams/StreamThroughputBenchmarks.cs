//-----------------------------------------------------------------------
// <copyright file="StreamThroughputBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
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
        private const int PayloadBytes = 128;

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private IRunnableGraph<Task<(int Messages, long Bytes)>> _restartSequenceGraph = null!;
        private IRunnableGraph<Task<(int Messages, long Bytes)>> _restartReferenceGraph = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            Console.WriteLine("Stream throughput GC: server={0}, runtime={1}, processors={2}",
                GCSettings.IsServerGC, Environment.Version, Environment.ProcessorCount);
            foreach (var setting in GC.GetConfigurationVariables())
                Console.WriteLine("Stream throughput GC config: {0}={1}", setting.Key, setting.Value);
            _system = ActorSystem.Create("stream-throughput-bench");
            _materializer = _system.Materializer();
            var payload = new byte[PayloadBytes];
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)i;

            _restartSequenceGraph = BuildRestartGraph(new ReadOnlySequence<byte>(payload),
                static sequence => sequence.IsSingleSegment && MatchesPayload(sequence.FirstSpan));
            _restartReferenceGraph = BuildRestartGraph(payload,
                static bytes => MatchesPayload(bytes));
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            _materializer.Dispose();
            await _system.Terminate();
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

        /// <summary>
        /// Sends prebuilt sequences through one RestartFlow subsource ingress and subsink egress.
        /// Graph lifecycle costs are amortized over the delivered batch; no restart is triggered.
        /// </summary>
        [Benchmark(OperationsPerInvoke = ElementCount)]
        public async Task RestartFlow_Sequence_Handoff_Throughput()
        {
            ValidateRestartDelivery(await _restartSequenceGraph.Run(_materializer));
        }

        /// <summary>Uses a prebuilt reference payload through the same RestartFlow handoffs.</summary>
        [Benchmark(OperationsPerInvoke = ElementCount)]
        public async Task RestartFlow_Reference_Handoff_Throughput()
        {
            ValidateRestartDelivery(await _restartReferenceGraph.Run(_materializer));
        }

        private static IRunnableGraph<Task<(int Messages, long Bytes)>> BuildRestartGraph<T>(
            T payload, Func<T, bool> matchesPayload)
        {
            return Source.Repeat(payload).Take(ElementCount)
                .Via(RestartFlow.WithBackoff(
                    static () => Flow.Create<T>().Select(static element => element),
                    RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0)))
                .ToMaterialized(Sink.Aggregate<T, (int Messages, long Bytes)>(default,
                    (totals, element) =>
                    {
                        if (!matchesPayload(element))
                            throw new InvalidOperationException("RestartFlow changed benchmark payload content.");
                        return (totals.Messages + 1, totals.Bytes + PayloadBytes);
                    }), Keep.Right);
        }

        private static bool MatchesPayload(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != PayloadBytes)
                return false;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != (byte)i)
                    return false;
            }
            return true;
        }

        private static void ValidateRestartDelivery((int Messages, long Bytes) totals)
        {
            if (totals.Messages != ElementCount || totals.Bytes != (long)ElementCount * PayloadBytes)
                throw new InvalidOperationException(
                    $"Incomplete RestartFlow delivery: {totals.Messages} messages, {totals.Bytes} bytes.");
        }
    }
}
