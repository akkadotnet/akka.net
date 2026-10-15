//-----------------------------------------------------------------------
// <copyright file="SequenceBoxingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Measures sequence boxing across fused interpreter slots against allocating one reference
    /// carrier per message and forwarding that same carrier. Both graphs create their transport
    /// representation inside the measured path from the same reusable byte array.
    /// </summary>
    /// <remarks>
    /// Results are per delivered message, including graph materialization amortized over the batch.
    /// Zero forwarding stages is the construction/control case; added stages expose the allocation
    /// slope. This isolates interpreter representation costs, not Artery's complete TCP topology,
    /// framing, serialization, async boundaries, or pooled-buffer ownership and disposal.
    /// </remarks>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class SequenceBoxingBenchmarks
    {
        private const int MessageCount = 100_000;
        private const int PayloadBytes = 128;

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private IRunnableGraph<Task<DeliveryTotals>> _sequenceGraph = null!;
        private IRunnableGraph<Task<DeliveryTotals>> _referenceGraph = null!;

        /// <summary>Number of real forwarding stages after representation construction.</summary>
        [Params(0, 1, 4, 8)]
        public int ForwardingStages { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("sequence-boxing-benchmark");
            _materializer = _system.Materializer();
            var payload = new byte[PayloadBytes];
            var source = Source.Repeat(payload).Take(MessageCount);

            _sequenceGraph = BuildGraph(
                source.Select(static bytes => new ReadOnlySequence<byte>(bytes)),
                static sequence => sequence.Length);
            _referenceGraph = BuildGraph(
                source.Select(static bytes => new Frame(new ReadOnlySequence<byte>(bytes))),
                static frame => frame.Payload.Length);
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            _materializer.Dispose();
            await _system.Terminate();
        }

        /// <summary>Value-type sequences box when pushed into interpreter object slots.</summary>
        [Benchmark(Baseline = true, OperationsPerInvoke = MessageCount)]
        public async Task SequenceValues()
        {
            Validate(await _sequenceGraph.Run(_materializer));
        }

        /// <summary>Allocate one carrier per message, then forward that same reference.</summary>
        [Benchmark(OperationsPerInvoke = MessageCount)]
        public async Task ReferenceFrames()
        {
            Validate(await _referenceGraph.Run(_materializer));
        }

        private IRunnableGraph<Task<DeliveryTotals>> BuildGraph<T>(
            Source<T, NotUsed> source, Func<T, long> length)
        {
            for (var i = 0; i < ForwardingStages; i++)
                source = source.Via(new ForwardStage<T>());

            return source.ToMaterialized(
                Sink.Aggregate<T, DeliveryTotals>(default,
                    (totals, element) => new DeliveryTotals(totals.Messages + 1, totals.Bytes + length(element))),
                Keep.Right);
        }

        private static void Validate(DeliveryTotals totals)
        {
            if (totals.Messages != MessageCount || totals.Bytes != (long)MessageCount * PayloadBytes)
                throw new InvalidOperationException(
                    $"Incomplete delivery: {totals.Messages} messages, {totals.Bytes} bytes.");
        }

        private readonly record struct DeliveryTotals(int Messages, long Bytes);

        private sealed class Frame
        {
            public Frame(ReadOnlySequence<byte> payload)
            {
                Payload = payload;
            }

            public ReadOnlySequence<byte> Payload { get; }
        }

        // A custom stage ensures every requested hop is present in the materialized graph,
        // independent of identity Select/Flow optimizations.
        private sealed class ForwardStage<T> : GraphStage<FlowShape<T, T>>
        {
            private readonly Inlet<T> _in = new("sequence-boxing.in");
            private readonly Outlet<T> _out = new("sequence-boxing.out");

            public ForwardStage()
            {
                Shape = new FlowShape<T, T>(_in, _out);
            }

            public override FlowShape<T, T> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new Logic(this);

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly ForwardStage<T> _stage;

                public Logic(ForwardStage<T> stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandlers(stage._in, stage._out, this);
                }

                public override void OnPush() => Push(_stage._out, Grab(_stage._in));

                public override void OnPull() => Pull(_stage._in);
            }
        }
    }
}
