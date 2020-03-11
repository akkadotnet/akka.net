//-----------------------------------------------------------------------
// <copyright file="ActorGraphInterpreterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class ActorGraphInterpreterSpec : AkkaSpec
    {
        public ActorMaterializer Materializer { get; }

        public ActorGraphInterpreterSpec(ITestOutputHelper output = null) : base(output)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_interpret_a_simple_identity_graph_stage()
        {
            this.AssertAllStagesStopped(() =>
            {
                var identity = GraphStages.Identity<int>();

                var task = Source.From(Enumerable.Range(1, 100))
                    .Via(identity)
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                
                task.AwaitResult().Should().Equal(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_reuse_a_simple_identity_graph_stage()
        {
            this.AssertAllStagesStopped(() =>
            {
                var identity = GraphStages.Identity<int>();

                var task = Source.From(Enumerable.Range(1, 100))
                    .Via(identity)
                    .Via(identity)
                    .Via(identity)
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                
                task.AwaitResult().Should().Equal(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_interpret_a_simple_bidi_stage()
        {
            this.AssertAllStagesStopped(() =>
            {
                var identityBidi = new IdentityBidiGraphStage();
                var identity = BidiFlow.FromGraph(identityBidi).Join(Flow.Identity<int>().Select(x => x));

                var task = Source.From(Enumerable.Range(1, 10))
                    .Via(identity)
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                
                task.AwaitResult().Should().Equal(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_interpret_and_reuse_a_simple_bidi_stage()
        {
            this.AssertAllStagesStopped(() =>
            {
                var identityBidi = new IdentityBidiGraphStage();
                var identityBidiFlow = BidiFlow.FromGraph(identityBidi);
                var identity = identityBidiFlow.Atop(identityBidiFlow).Atop(identityBidiFlow).Join(Flow.Identity<int>().Select(x => x));

                var task = Source.From(Enumerable.Range(1, 10))
                    .Via(identity)
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                
                task.AwaitResult().Should().Equal(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_interpret_a_rotated_identity_bidi_stage()
        {
            this.AssertAllStagesStopped(() =>
            {
                var rotatedBidi = new RotatedIdentityBidiGraphStage();
                var takeAll = Flow.Identity<int>()
                    .Grouped(200)
                    .ToMaterialized(Sink.First<IEnumerable<int>>(), Keep.Right);

                var tasks = RunnableGraph.FromGraph(
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
                    })).Run(Materializer);
                
                tasks.Item1.AwaitResult().Should().Equal(Enumerable.Range(1, 100));
                tasks.Item2.AwaitResult().Should().Equal(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_report_errors_if_an_error_happens_for_an_already_completed_stage()
        {
            var failyStage = new FailyGraphStage();

            EventFilter.Exception<ArgumentException>(new Regex("Error in stage.*")).ExpectOne(() =>
            {
                Source.FromGraph(failyStage).RunWith(Sink.Ignore<int>(), Materializer).Wait(TimeSpan.FromSeconds(3));
            });
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_able_to_properly_handle_case_where_a_stage_fails_before_subscription_happens()
        {
            // Fuzzing needs to be off, so that the failure can propagate to the output boundary
            // before the ExposedPublisher message.
            var noFuzzMaterializer = ActorMaterializer.Create(Sys,
                ActorMaterializerSettings.Create(Sys).WithFuzzingMode(false));
            this.AssertAllStagesStopped(() =>
            {

                var evilLatch = new CountdownEvent(1);

                // This is a somewhat tricky test setup. We need the following conditions to be met:
                //  - the stage should fail its output port before the ExposedPublisher message is processed
                //  - the enclosing actor (and therefore the stage) should be kept alive until a stray SubscribePending arrives
                //    that has been enqueued after ExposedPublisher message has been enqueued, but before it has been processed
                //
                // To achieve keeping alive the stage for long enough, we use an extra input and output port and instead
                // of failing the stage, we fail only the output port under test.
                //
                // To delay the startup long enough, so both ExposedPublisher and SubscribePending are enqueued, we use an evil
                // latch to delay the preStart() (which in turn delays the enclosing actor's preStart).
                var failyStage = new FailyInPreStartGraphStage(evilLatch);

                var downstream0 = this.CreateSubscriberProbe<int>();
                var downstream1 = this.CreateSubscriberProbe<int>();

                var upstream = this.CreatePublisherProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var faily = b.Add(failyStage);

                    b.From(Source.FromPublisher(upstream)).To(faily.In);
                    b.From(faily.Out0).To(Sink.FromSubscriber(downstream0));
                    b.From(faily.Out1).To(Sink.FromSubscriber(downstream1));

                    return ClosedShape.Instance;
                })).Run(noFuzzMaterializer);

                evilLatch.Signal();
                var ex = downstream0.ExpectSubscriptionAndError();
                ex.Should().BeOfType<TestException>();
                ex.Message.Should().Be("Test failure in PreStart");

                // if an NRE would happen due to unset exposedPublisher (see #19338), this would receive a failure instead
                // of the actual element
                downstream1.Request(1);
                upstream.SendNext(42);
                downstream1.ExpectNext(42);

                upstream.SendComplete();
                downstream1.ExpectComplete();
            }, noFuzzMaterializer);
        }
        
        [Fact]
        public void ActorGraphInterpreter_should_be_to_handle_Publisher_spec_violations_without_leaking()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.Combine(Source.FromPublisher(new FilthyPublisher()), Source.FromPublisher(upstream),
                    count => new Merge<int, int>(count)).RunWith(Sink.FromSubscriber(downstream), Materializer);

                upstream.EnsureSubscription();
                upstream.ExpectCancellation();

                downstream.EnsureSubscription();

                var ex = downstream.ExpectError();
                ex.Should().BeOfType<IllegalStateException>();
                ex.InnerException.Should().BeAssignableTo<ISpecViolation>();
                ex.InnerException.InnerException.Should().BeOfType<TestException>();
                ex.InnerException.InnerException.Message.Should().Be("violating your spec");
            }, Materializer);
        }

        private sealed class FilthyPublisher : IPublisher<int>
        {
            private sealed class Subscription : ISubscription
            {
                public void Request(long n) => throw new TestException("violating your spec");

                public void Cancel()
                {
                }
            }

            public void Subscribe(ISubscriber<int> subscriber) => subscriber.OnSubscribe(new Subscription());
        }

        [Fact]
        public void ActorGraphInterpreter_should_be_to_handle_Subscriber_spec_violations_without_leaking()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream)
                    .AlsoTo(Sink.FromSubscriber(downstream))
                    .RunWith(Sink.FromSubscriber(new FilthySubscriber()), Materializer);
                upstream.SendNext(0);
                downstream.RequestNext(0);

                var ex = downstream.ExpectError();
                ex.Should().BeOfType<IllegalStateException>();
                ex.InnerException.Should().BeAssignableTo<ISpecViolation>();
                ex.InnerException.InnerException.Should().BeOfType<TestException>();
                ex.InnerException.InnerException.Message.Should().Be("violating your spec");
            }, Materializer);
        }

        [Fact]
        public void ActorGraphInterpreter_should_trigger_PostStop_in_all_stages_when_abruptly_terminated_and_no_upstream_boundaries()
        {
            this.AssertAllStagesStopped(() =>
            {
                var materializer = ActorMaterializer.Create(Sys);
                var gotStop = new TestLatch(1);
                var downstream = this.CreateSubscriberProbe<string>();

                Source.Repeat("whatever")
                    .Via(new PostStopSnitchFlow(gotStop))
                    .To(Sink.FromSubscriber(downstream)).Run(materializer);

                downstream.RequestNext();

                materializer.Shutdown();
                gotStop.Ready(RemainingOrDefault);

                downstream.ExpectError().Should().BeOfType<AbruptTerminationException>();
            }, Materializer);
        }

        private sealed class PostStopSnitchFlow : SimpleLinearGraphStage<string>
        {
            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly PostStopSnitchFlow _stage;

                public Logic(PostStopSnitchFlow stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Inlet, this);
                    SetHandler(stage.Outlet, this);
                }

                public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

                public override void OnPull() => Pull(_stage.Inlet);

                public override void PostStop() => _stage._latch.CountDown();
            }

            private readonly TestLatch _latch;

            public PostStopSnitchFlow(TestLatch latch)
            {
                _latch = latch;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class FilthySubscriber : ISubscriber<int>
        {
            public void OnSubscribe(ISubscription subscription) => subscription.Request(1);

            public void OnNext(int element) => throw new TestException("violating your spec");

            public void OnError(Exception cause)
            {
            }

            public void OnComplete()
            {
            }
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

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape);

            public override string ToString() => "IdentityBidi";
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

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape);

            public override string ToString() => "IdentityBidi";
        }

        public class FailyGraphStage : GraphStage<SourceShape<int>>
        {
            private class Logic : GraphStageLogic
            {
                public Logic(SourceShape<int> shape) : base(shape)
                {
                    SetHandler(shape.Outlet,
                        onPull: () =>
                        {
                            CompleteStage();
                            // This cannot be propagated now since the stage is already closed
                            Push(shape.Outlet, -1);
                        });
                }
            }

            public Outlet<int> Out { get; }

            public FailyGraphStage()
            {
                Out = new Outlet<int>("test.out");
                Shape = new SourceShape<int>(Out);
            }

            public override SourceShape<int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape);

            public override string ToString() => "Faily";
        }

        /// <summary>
        /// </summary>
        public class FailyInPreStartGraphStage : GraphStage<FanOutShape<int, int, int>>
        {
            private readonly CountdownEvent _evilLatch;

            private class Logic : GraphStageLogic
            {
                private readonly CountdownEvent _evilLatch;
                private readonly FanOutShape<int, int, int> _shape;

                public Logic(FanOutShape<int, int, int> shape, CountdownEvent evilLatch) : base(shape)
                {
                    _shape = shape;
                    _evilLatch = evilLatch;

                    SetHandler(shape.Out0, IgnoreTerminateOutput); // We fail in PreStart anyway
                    SetHandler(shape.Out1, IgnoreTerminateOutput); // We fail in PreStart anyway
                    PassAlong(shape.In, shape.Out1);
                }

                public override void PreStart()
                {
                    Pull(_shape.In);
                    _evilLatch.Wait(TimeSpan.FromSeconds(3));
                    Fail(_shape.Out0, new TestException("Test failure in PreStart"));
                }
            }

            public Inlet<int> In { get; }
            public Outlet<int> Out0 { get; }
            public Outlet<int> Out1 { get; }

            public FailyInPreStartGraphStage(CountdownEvent evilLatch)
            {
                _evilLatch = evilLatch;
                In = new Inlet<int>("test.in");
                Out0 = new Outlet<int>("test.out0");
                Out1 = new Outlet<int>("test.out1");
                Shape = new FanOutShape<int, int, int>(In, Out0, Out1);
            }

            public override FanOutShape<int, int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, _evilLatch);

            public override string ToString() => "Faily";
        }
    }
}
