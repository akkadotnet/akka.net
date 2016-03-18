using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class ActorGraphInterpreterSpec : AkkaSpec
    {
         private readonly ActorMaterializer _materializer;

        public ActorGraphInterpreterSpec(ITestOutputHelper output = null) : base(output)
        {
            _materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async void ActorGraphInterpreter_should_be_able_to_interpret_a_simple_identity_graph_stage()
        {
            await this.AssertAllStagesStopped(async () =>
            {
                var identity = GraphStages.Identity<int>();

                var result = await Source.From(Enumerable.Range(1, 100))
                    .Via(identity)
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), _materializer);

                result.Should().Equal(Enumerable.Range(1, 100));
            }, _materializer);
        }

        [Fact]
        public async void ActorGraphInterpreter_should_be_able_to_reuse_a_simple_identity_graph_stage()
        {
            await this.AssertAllStagesStopped(async () =>
            {
                var identity = GraphStages.Identity<int>();

                var result = await Source.From(Enumerable.Range(1, 100))
                    .Via(identity)
                    .Via(identity)
                    .Via(identity)
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), _materializer);

                result.Should().Equal(Enumerable.Range(1, 100));
            }, _materializer);
        }

        [Fact]
        public async void ActorGraphInterpreter_should_be_able_to_interpret_a_simple_bidi_stage()
        {
            await this.AssertAllStagesStopped(async () =>
            {
                var identityBidi = new IdentityBidiGraphStage();
                var identity = BidiFlow.FromGraph(identityBidi).Join(Flow.Identity<int>().Map(x => x));

                var result = await Source.From(Enumerable.Range(1, 10))
                    .Via(identity)
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), _materializer);

                result.Should().Equal(Enumerable.Range(1, 10));
            }, _materializer);
        }

        [Fact]
        public async void ActorGraphInterpreter_should_be_able_to_interpret_and_reuse_a_simple_bidi_stage()
        {
            await this.AssertAllStagesStopped(async () =>
            {
                var identityBidi = new IdentityBidiGraphStage();
                var identityBidiFlow = BidiFlow.FromGraph(identityBidi);
                var identity = identityBidiFlow.Atop(identityBidiFlow).Atop(identityBidiFlow).Join(Flow.Identity<int>().Map(x => x));

                var result = await Source.From(Enumerable.Range(1, 10))
                    .Via(identity)
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), _materializer);

                result.Should().Equal(Enumerable.Range(1, 10));
            }, _materializer);
        }

        [Fact]
        public async void ActorGraphInterpreter_should_be_able_to_interpret_a_rotated_identity_bidi_stage()
        {
            await this.AssertAllStagesStopped(async () =>
            {
                var rotatedBidi = new RotatedIdentityBidiGraphStage();
                var takeAll = Flow.Identity<int>()
                    .Grouped(200)
                    .ToMaterialized(Sink.First<IEnumerable<int>>(), Keep.Right);

                var g = RunnableGraph<Tuple<Task<IEnumerable<int>>, Task<IEnumerable<int>>>>.FromGraph(
                    GraphDsl.Create(takeAll, takeAll, Keep.Both, (builder, shape1, shape2) =>
                    {
                        var bidi = builder.Add(rotatedBidi);
                        var source1 = builder.Add(Source.From(Enumerable.Range(1, 10)));
                        var source2 = builder.Add(Source.From(Enumerable.Range(1, 100)));

                        builder
                            .From(source1).To(bidi.Inlet1)
                            .To(shape2.Inlet).From(bidi.Outlet2)

                            .From(source2).To(bidi.Inlet2)
                            .To(shape1.Inlet).From(bidi.Outlet1);

                        return ClosedShape.Instance;
                    }));
                var result = g.Run(_materializer);

                var f1 = await result.Item1;
                var f2 = await result.Item2;

                f1.Should().Equal(Enumerable.Range(1, 100));
                f2.Should().Equal(Enumerable.Range(1, 10));
            }, _materializer);
        }

        public class IdentityBidiGraphStage : GraphStage<BidiShape<int, int, int, int>>
        {
            private class Logic : GraphStageLogic
            {
                public Logic(BidiShape<int, int, int, int> shape) : base(shape)
                {
                    SetHandler(shape.Inlet1,
                        onPush: () => Push(shape.Outlet1, Grab(shape.Inlet1)),
                        onUpstreamFinish: () => Complete(shape.Outlet1));

                    SetHandler(shape.Inlet2,
                        onPush: () => Push(shape.Outlet2, Grab(shape.Inlet2)),
                        onUpstreamFinish: () => Complete(shape.Outlet2));

                    SetHandler(shape.Outlet1,
                        onPull: () => Pull(shape.Inlet1),
                        onDownstreamFinish: () => Cancel(shape.Inlet1));

                    SetHandler(shape.Outlet2,
                        onPull: () => Pull(shape.Inlet2),
                        onDownstreamFinish: () => Cancel(shape.Inlet2));
                }
            }

            public Inlet<int> In1 { get; }
            public Inlet<int> In2 { get; }
            public Outlet<int> Out1 { get; }
            public Outlet<int> Out2 { get; }

            public IdentityBidiGraphStage()
            {
                In1 = new Inlet<int>("in1");
                In2 = new Inlet<int>("in2");
                Out1 = new Outlet<int>("out1");
                Out2 = new Outlet<int>("out2");
                Shape = new BidiShape<int, int, int, int>(In1, Out1, In2, Out2);
            }

            public override BidiShape<int, int, int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new Logic(Shape);
            }

            public override string ToString()
            {
                return "IdentityBidi";
            }
        }

        /// <summary>
        /// This is a "rotated" identity BidiStage, as it loops back upstream elements
        /// to its upstream, and loops back downstream elements to its downstream.
        /// </summary>
        public class RotatedIdentityBidiGraphStage : GraphStage<BidiShape<int, int, int, int>>
        {
            private class Logic : GraphStageLogic
            {
                public Logic(BidiShape<int, int, int, int> shape) : base(shape)
                {
                    SetHandler(shape.Inlet1,
                        onPush: () => Push(shape.Outlet2, Grab(shape.Inlet1)),
                        onUpstreamFinish: () => Complete(shape.Outlet2));

                    SetHandler(shape.Inlet2,
                        onPush: () => Push(shape.Outlet1, Grab(shape.Inlet2)),
                        onUpstreamFinish: () => Complete(shape.Outlet1));

                    SetHandler(shape.Outlet1,
                        onPull: () => Pull(shape.Inlet2),
                        onDownstreamFinish: () => Cancel(shape.Inlet2));

                    SetHandler(shape.Outlet2,
                        onPull: () => Pull(shape.Inlet1),
                        onDownstreamFinish: () => Cancel(shape.Inlet1));
                }
            }

            public Inlet<int> In1 { get; }
            public Inlet<int> In2 { get; }
            public Outlet<int> Out1 { get; }
            public Outlet<int> Out2 { get; }

            public RotatedIdentityBidiGraphStage()
            {
                In1 = new Inlet<int>("in1");
                In2 = new Inlet<int>("in2");
                Out1 = new Outlet<int>("out1");
                Out2 = new Outlet<int>("out2");
                Shape = new BidiShape<int, int, int, int>(In1, Out1, In2, Out2);
            }

            public override BidiShape<int, int, int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new Logic(Shape);
            }

            public override string ToString()
            {
                return "IdentityBidi";
            }
        }
    }
}