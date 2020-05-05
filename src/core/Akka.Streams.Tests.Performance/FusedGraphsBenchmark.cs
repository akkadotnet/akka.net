//-----------------------------------------------------------------------
// <copyright file="FusedGraphsBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;
using NBench;

namespace Akka.Streams.Tests.Performance
{
    public class FusedGraphsBenchmark
    {
        private const int ElementCount = 100 * 1000;

        private sealed class MutableElement
        {
            public MutableElement(int value)
            {
                Value = value;
            }

            public int Value { get; set; }
        }

        // Just to avoid allocations and still have a way to do some work in stages. The value itself does not matter
        // so no issues with sharing(the result does not make any sense, but hey)
        private sealed class TestSource : GraphStage<SourceShape<MutableElement>>
        {
            private sealed class Logic : GraphStageLogic
            {
                public Logic(TestSource stage) : base(stage.Shape)
                {
                    var left = ElementCount - 1;
                    SetHandler(stage.Out, onPull: () =>
                    {
                        if (left >= 0)
                        {
                            Push(stage.Out, stage._elements[left]);
                            left--;
                        }
                        else
                            CompleteStage();
                    });
                }
            }

            private readonly MutableElement[] _elements;

            public TestSource(MutableElement[] elements)
            {
                _elements = elements;

                Shape = new SourceShape<MutableElement>(Out);
            }

            private Outlet<MutableElement> Out { get; } = new Outlet<MutableElement>("TestSource.out");

            public override SourceShape<MutableElement> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class CompletionLatch : GraphStageWithMaterializedValue<SinkShape<MutableElement>, TestLatch>
        {
            private sealed class Logic : GraphStageLogic
            {
                private readonly CompletionLatch _stage;

                public Logic(CompletionLatch stage, TestLatch latch) : base(stage.Shape)
                {
                    _stage = stage;

                    var sum = 0;
                    SetHandler(stage.In, onPush: () =>
                    {
                        sum += Grab(stage.In).Value;
                        Pull(stage.In);
                    }, onUpstreamFinish: () =>
                    {
                        // Do not ignore work along the chain
                        // on the jvm:
                        // org.openjdk.jmh.infra.Blackhole.consume(sum)
                        var i = 0;
                        while (i != sum)
                            sum--;

                        latch.CountDown();
                        CompleteStage();
                    });
                }

                public override void PreStart() => Pull(_stage.In);
            }

            public CompletionLatch()
            {
                Shape = new SinkShape<MutableElement>(In);
            }

            private Inlet<MutableElement> In { get; } = new Inlet<MutableElement>("CompletionLatch.in");

            public override SinkShape<MutableElement> Shape { get; }
            public override ILogicAndMaterializedValue<TestLatch> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            {
                var latch = new TestLatch(1);
                var logic = new Logic(this, latch);
                return new LogicAndMaterializedValue<TestLatch>(logic, latch);
            }
        }

        private sealed class IdentityStage : GraphStage<FlowShape<MutableElement, MutableElement>>
        {
            private sealed class Logic : GraphStageLogic
            {
                public Logic(IdentityStage stage) : base(stage.Shape)
                {
                    SetHandler(stage.In, onPush:()=>Push(stage.Out, Grab(stage.In)));
                    SetHandler(stage.Out, onPull: () => Pull(stage.In));
                }
            }

            public IdentityStage()
            {
                Shape = new FlowShape<MutableElement, MutableElement>(In, Out);
            }

            private Outlet<MutableElement> Out { get; } = new Outlet<MutableElement>("IdentityStage.out");
            
            private Inlet<MutableElement> In { get; } = new Inlet<MutableElement>("IdentityStage.in");

            public override FlowShape<MutableElement, MutableElement> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
        
        private ActorSystem _system;
        private MutableElement[] _testElements;
        private ActorMaterializer _materializer;
        private RunnableGraph<TestLatch> _singleIdentity;
        private RunnableGraph<TestLatch> _chainOfIdentities;
        private RunnableGraph<TestLatch> _singleSelect;
        private RunnableGraph<TestLatch> _chainOfSelects;
        private RunnableGraph<TestLatch> _repeatTakeSelectAndAggregate;
        private RunnableGraph<TestLatch> _singleBuffer;
        private RunnableGraph<TestLatch> _chainOfBuffers;
        private RunnableGraph<TestLatch> _broadcastZip;
        private RunnableGraph<TestLatch> _balanceMerge;
        private RunnableGraph<TestLatch> _broadcastZipBalanceMerge;

        private static MutableElement Add(MutableElement x)
        {
            x.Value++;
            return x;
        }

        private static RunnableGraph<TestLatch> Fuse(IRunnableGraph<TestLatch> graph)
            => RunnableGraph.FromGraph(Fusing.Aggressive(graph));

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _system = ActorSystem.Create("Test");
            var settings =
                ActorMaterializerSettings.Create(_system)
                    .WithFuzzingMode(false)
                    .WithSyncProcessingLimit(int.MaxValue)
                    .WithAutoFusing(false); // We fuse manually in this test in the setup

            _materializer = _system.Materializer(settings);
            _testElements = Enumerable.Repeat(0, ElementCount).Select(i => new MutableElement(i)).ToArray();
            var testSource = Source.FromGraph(new TestSource(_testElements));
            var testSink = Sink.FromGraph(new CompletionLatch());
            var identityStage = new IdentityStage();
            
            _singleIdentity = Fuse(testSource.Via(identityStage).ToMaterialized(testSink, Keep.Right));
            _chainOfIdentities =
                Fuse(
                    testSource.Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .Via(identityStage)
                        .ToMaterialized(testSink, Keep.Right));

            _singleSelect = Fuse(testSource.Select(Add).ToMaterialized(testSink, Keep.Right));

            _chainOfSelects = Fuse(
                testSource.Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .Select(Add)
                    .ToMaterialized(testSink, Keep.Right));

            _repeatTakeSelectAndAggregate =
                Fuse(Source.Repeat(new MutableElement(0))
                    .Take(ElementCount)
                    .Select(Add)
                    .Select(Add)
                    .Aggregate(new MutableElement(0), (acc, x) =>
                    {
                        acc.Value += x.Value;
                        return acc;
                    }).ToMaterialized(testSink, Keep.Right));

            _singleBuffer =
                Fuse(testSource.Buffer(10, OverflowStrategy.Backpressure).ToMaterialized(testSink, Keep.Right));

            _chainOfBuffers =
                Fuse(
                    testSource.Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .Buffer(10, OverflowStrategy.Backpressure)
                        .ToMaterialized(testSink, Keep.Right));

            var broadcastZipFLow = Flow.FromGraph(GraphDsl.Create(b =>
            {
                var bcast = b.Add(new Broadcast<MutableElement>(2));
                var zip = b.Add(new Zip<MutableElement, MutableElement>());

                b.From(bcast).To(zip.In0);
                b.From(bcast).To(zip.In1);
                var outlet =
                    b.From(zip.Out).Via(Flow.Create<(MutableElement, MutableElement)>().Select(t => t.Item1));
                return new FlowShape<MutableElement, MutableElement>(bcast.In, outlet.Out);
            }));

            var balanceMergeFlow = Flow.FromGraph(GraphDsl.Create(b =>
            {
                var balance = b.Add(new Balance<MutableElement>(2));
                var merge = b.Add(new Merge<MutableElement>(2));

                b.From(balance).To(merge);
                b.From(balance).To(merge);
               
                return new FlowShape<MutableElement, MutableElement>(balance.In, merge.Out);
            }));

            _broadcastZip = Fuse(testSource.Via(broadcastZipFLow).ToMaterialized(testSink, Keep.Right));

            _balanceMerge = Fuse(testSource.Via(balanceMergeFlow).ToMaterialized(testSink, Keep.Right));

            _broadcastZipBalanceMerge = Fuse(testSource.Via(broadcastZipFLow).Via(balanceMergeFlow).ToMaterialized(testSink, Keep.Right));
        }

        [PerfCleanup]
        public void Cleanup() => _system.Terminate().Wait(TimeSpan.FromSeconds(10));



        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 50)]
        public void SingleIdentity() => _singleIdentity.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Measurement, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 100)]
        public void ChainOfIdentities() => _chainOfIdentities.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 50)]
        public void SingleSelect() => _singleSelect.Run(_materializer).Ready();

        
        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Measurement, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 110)]
        public void ChainOfSelects() => _chainOfSelects.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 60)]
        public void SingleBuffer() => _singleBuffer.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 350)]
        public void ChainOfBuffers() => _chainOfBuffers.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Measurement, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 3500)]
        public void RepeatTakeSelectAndAggregate() => _repeatTakeSelectAndAggregate.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 100)]
        public void BroadcastZip() => _broadcastZip.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 50)]
        public void BalanceMerge() => _balanceMerge.Run(_materializer).Ready();


        [PerfBenchmark(RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 100)]
        public void BroadcastZipBalanceMerge() => _broadcastZipBalanceMerge.Run(_materializer).Ready();
        
    }
}
