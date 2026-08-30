//-----------------------------------------------------------------------
// <copyright file="InboundLaneTopologyBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading;
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
    /// Head-to-head of the candidate Artery inbound-lane fan-out topologies, to decide whether
    /// Akka.Streams-based lanes can match an actor-<c>Tell</c> fan-out (issue #8314 research).
    ///
    /// Each element carries a representative, allocation-free per-lane "deserialize" cost (a
    /// dependency-chained LCG spin), so the comparison is about how each topology *parallelizes real
    /// work across lanes*, not about trivial-element boundary micro-cost.
    ///
    /// Topologies:
    /// <list type="bullet">
    /// <item><c>Fused_Inline</c> — single fused island, work inline: the single-core, work-bound
    /// ceiling (baseline; independent of <see cref="LaneCount"/>).</item>
    /// <item><c>PartitionAsync_Lanes</c> — <c>Partition(N) → [.Async() → work]×N → Merge(N)</c>: the
    /// per-element cross-actor boundary topology #8314 benchmarked and (mis)labeled "canonical Pekko."</item>
    /// <item><c>PartitionHub_Lanes</c> — <c>Source → PartitionHub → [work]×N lanes</c>: the topology
    /// JVM Artery actually uses (MergeHub + FixedSizePartitionHub), coordinated by a shared buffer +
    /// amortized doorbell rather than a per-element tell.</item>
    /// <item><c>ActorTell_Lanes</c> — one <c>Tell</c> per element to N lane actors: the (no-backpressure)
    /// ceiling the #8314 issue measured at ~2.73M msg/s.</item>
    /// </list>
    ///
    /// The "win" to prove: <c>PartitionHub_Lanes</c> scales *up* with <see cref="LaneCount"/> toward
    /// <c>ActorTell_Lanes</c>, while <c>PartitionAsync_Lanes</c> saturates (or scales negatively).
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class InboundLaneTopologyBenchmarks
    {
        private const int ElementCount = 100_000;

        // Dependency-chained LCG iterations per element. Not superscalar-parallelizable, so this is a
        // predictable per-element CPU cost (~a few hundred ns) standing in for header/payload decode.
        private const int WorkSpins = 512;

        // Mirrors Pekko Artery's InboundHubBufferSize default.
        private const int HubBufferSize = 256;

        private sealed class Msg
        {
            public int Id;
        }

        private ActorSystem _system;
        private ActorMaterializer _materializer;
        private Msg[] _elements;

        // Sink for the work result so the JIT cannot elide the spin. Concurrent writes across lanes
        // are a benign race — the value is never read for correctness.
        private volatile int _blackhole;

        [Params(1, 4, 8)]
        public int LaneCount;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("inbound-lane-bench");
            _materializer = _system.Materializer();
            _elements = Enumerable.Range(0, ElementCount).Select(i => new Msg { Id = i }).ToArray();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        private static int Spin(int seed)
        {
            var x = seed;
            for (var i = 0; i < WorkSpins; i++)
                x = x * 1664525 + 1013904223;
            return x;
        }

        // The shared per-element work, identical across every topology.
        private Msg DoWork(Msg m)
        {
            _blackhole = Spin(m.Id);
            return m;
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = ElementCount)]
        public Task Fused_Inline()
            => Source.From(_elements)
                .Select(DoWork)
                .RunWith(Sink.Ignore<Msg>(), _materializer);

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task PartitionAsync_Lanes()
            => RunnableGraph.FromGraph(GraphDsl.Create(Sink.Ignore<Msg>(), (b, sink) =>
            {
                var partition = b.Add(new Partition<Msg>(LaneCount, m => m.Id % LaneCount));
                var merge = b.Add(new Merge<Msg>(LaneCount));
                var source = Source.From(_elements).MapMaterializedValue<Task<Done>>(_ => null);

                b.From(source).To(partition.In);
                for (var i = 0; i < LaneCount; i++)
                    b.From(partition.Out(i)).Via(Flow.Create<Msg>().Select(DoWork).Async()).To(merge.In(i));
                b.From(merge.Out).To(sink.Inlet);

                return ClosedShape.Instance;
            })).Run(_materializer);

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public async Task PartitionHub_Lanes()
        {
            // Producer runs into the hub; each lane is a separately materialized consumer stream that
            // drains the shared hub buffer (the JVM Artery shape). startAfterNrOfConsumers gates the
            // producer until all lanes are attached.
            var hubSource = Source.From(_elements)
                .RunWith(
                    PartitionHub.Sink<Msg>((size, m) => m.Id % size, startAfterNrOfConsumers: LaneCount, bufferSize: HubBufferSize),
                    _materializer);

            var tasks = new Task[LaneCount];
            for (var i = 0; i < LaneCount; i++)
                tasks[i] = hubSource.Select(DoWork).RunWith(Sink.Ignore<Msg>(), _materializer);

            await Task.WhenAll(tasks);
        }

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public async Task ActorTell_Lanes()
        {
            var shared = new LaneShared(ElementCount, this);
            var lanes = new IActorRef[LaneCount];
            for (var i = 0; i < LaneCount; i++)
                lanes[i] = _system.ActorOf(Props.Create(() => new LaneActor(shared)));

            // One Tell per element, partitioned like the stream versions. No backpressure — the
            // mailboxes absorb, lanes process in parallel. This is the ceiling.
            for (var i = 0; i < _elements.Length; i++)
            {
                var m = _elements[i];
                lanes[m.Id % LaneCount].Tell(m);
            }

            await shared.Completion.Task;

            foreach (var l in lanes)
                _system.Stop(l);
        }

        private sealed class LaneShared
        {
            public long Count;
            public readonly int Total;
            public readonly InboundLaneTopologyBenchmarks Owner;
            public readonly TaskCompletionSource<Done> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public LaneShared(int total, InboundLaneTopologyBenchmarks owner)
            {
                Total = total;
                Owner = owner;
            }
        }

        private sealed class LaneActor : UntypedActor
        {
            private readonly LaneShared _shared;

            public LaneActor(LaneShared shared) => _shared = shared;

            protected override void OnReceive(object message)
            {
                if (message is Msg m)
                {
                    _shared.Owner.DoWork(m);
                    if (Interlocked.Increment(ref _shared.Count) == _shared.Total)
                        _shared.Completion.TrySetResult(Done.Instance);
                }
            }
        }
    }
}
