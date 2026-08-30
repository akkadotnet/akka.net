//-----------------------------------------------------------------------
// <copyright file="AsyncBoundaryBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Isolates the per-element cost of an Akka.Streams <c>.Async()</c> island boundary
    /// (<c>ActorOutputBoundary</c> → one actor message + <c>OnNext</c> allocation per element →
    /// <c>BatchingActorInputBoundary</c>). See issue #8314.
    ///
    /// The measurement is an A/B against a fully fused single-island baseline that does the *same*
    /// trivial per-element work: the delta between <see cref="Fused_NoBoundary"/> and
    /// <see cref="Single_Async_Boundary"/> is the boundary tax (ns/element and B/element via the
    /// config's <c>MemoryDiagnoser</c>).
    ///
    /// Per-element work is deliberately trivial (identity) so the crossing dominates, and the
    /// element is a reference type so the reported allocation reflects the boundary's own
    /// <c>OnNext</c>/<c>Envelope</c> churn rather than value-type boxing noise.
    ///
    /// <see cref="BufferSize"/> is swept to demonstrate that input-buffer depth is *not* the fix:
    /// the cost is per-element, not per-wakeup, so 1 ≈ 16 ≈ 512 — buffer depth does not move the
    /// per-element tax. (Note: the "buffer=1 craters throughput" pathology from #8314 is a property
    /// of the heavier Artery topology, where a slow consumer stalls on demand round-trips; with
    /// trivial per-element work here the boundary cost dominates regardless of depth.)
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class AsyncBoundaryBenchmarks
    {
        private const int ElementCount = 100_000;

        // Reference-type payload: elements cross the boundary as `object`, so a struct element would
        // add boxing allocation on top of the boundary's own churn. A class isolates the boundary cost.
        private sealed class Payload
        {
            public int Value;
        }

        private ActorSystem _system;
        private ActorMaterializer _materializer;
        private Payload[] _elements;

        /// <summary>
        /// Input-buffer depth applied to the async boundary. 16 is the default; sweeping 1 and 512
        /// shows depth does not move the per-element boundary tax (the cost is per-element, not
        /// per-wakeup).
        /// </summary>
        [Params(16, 512, 1)]
        public int BufferSize;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("async-boundary-bench");
            _materializer = _system.Materializer();
            _elements = Enumerable.Range(0, ElementCount).Select(i => new Payload { Value = i }).ToArray();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        /// <summary>
        /// Baseline: identical graph, fully fused into a single island — no boundary crossing.
        /// (Independent of <see cref="BufferSize"/>; runs once per param value as a noise-floor check.)
        /// </summary>
        [Benchmark(Baseline = true, OperationsPerInvoke = ElementCount)]
        public Task Fused_NoBoundary()
            => Source.From(_elements)
                .Select(p => p)
                .Select(p => p)
                .RunWith(Sink.Ignore<Payload>(), _materializer);

        /// <summary>
        /// One <c>.Async()</c> boundary between two trivial stages. The delta versus
        /// <see cref="Fused_NoBoundary"/> is the per-element boundary tax that #8314 targets.
        /// </summary>
        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Single_Async_Boundary()
            => Source.From(_elements)
                .Select(p => p)
                .Async().AddAttributes(Attributes.CreateInputBuffer(BufferSize, BufferSize))
                .Select(p => p)
                .RunWith(Sink.Ignore<Payload>(), _materializer);
    }

    /// <summary>
    /// Fan-out variant of the #8314 boundary measurement: the canonical
    /// <c>Balance(N) → [.Async() lane]×N → Merge(N)</c> shape used by parallelism/lane topologies
    /// (and by the Artery inbound-lane design). Each lane crosses its own boundary, so this exposes
    /// the *negative* lane-scaling reported in the issue.
    ///
    /// A/B is against the same Balance/Merge topology with fully-fused lanes (no <c>.Async()</c>), so
    /// the delta isolates the boundary crossings from the fan-out machinery itself.
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class AsyncBoundaryFanOutBenchmarks
    {
        private const int ElementCount = 100_000;

        private sealed class Payload
        {
            public int Value;
        }

        private ActorSystem _system;
        private ActorMaterializer _materializer;
        private Payload[] _elements;

        /// <summary>Number of parallel lanes fanned out of the Balance stage.</summary>
        [Params(1, 4, 16)]
        public int LaneCount;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("async-boundary-fanout-bench");
            _materializer = _system.Materializer();
            _elements = Enumerable.Range(0, ElementCount).Select(i => new Payload { Value = i }).ToArray();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        private Task RunFanOut(bool async)
            => RunnableGraph.FromGraph(GraphDsl.Create(Sink.Ignore<Payload>(), (b, sink) =>
            {
                var balance = b.Add(new Balance<Payload>(LaneCount));
                var merge = b.Add(new Merge<Payload>(LaneCount));
                var source = Source.From(_elements).MapMaterializedValue<Task<Done>>(_ => null);

                b.From(source).To(balance.In);

                for (var i = 0; i < LaneCount; i++)
                {
                    var lane = Flow.Create<Payload>().Select(p => p);
                    if (async)
                        lane = lane.Async();
                    b.From(balance.Out(i)).Via(lane).To(merge.In(i));
                }

                b.From(merge.Out).To(sink.Inlet);

                return ClosedShape.Instance;
            })).Run(_materializer);

        /// <summary>
        /// Baseline: <c>Balance(N) → fused lanes → Merge(N)</c>, single island, no boundary crossings.
        /// </summary>
        [Benchmark(Baseline = true, OperationsPerInvoke = ElementCount)]
        public Task Balance_Fused_Lanes() => RunFanOut(async: false);

        /// <summary>
        /// <c>Balance(N) → [.Async() lane]×N → Merge(N)</c>: N boundary crossings. The delta versus
        /// <see cref="Balance_Fused_Lanes"/>, and how it scales with <see cref="LaneCount"/>, is the
        /// signal #8314 tracks.
        /// </summary>
        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Balance_Async_Lanes() => RunFanOut(async: true);
    }
}
