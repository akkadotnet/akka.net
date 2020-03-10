//-----------------------------------------------------------------------
// <copyright file="GraphStageLogicSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Streams.Tests.Implementation.Fusing;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation
{
    public class GraphStageLogicSpec : GraphInterpreterSpecKit
    {
        private class Emit1234 : GraphStage<FlowShape<int, int>>
        {
            #region internal classes

            private class Emit1234Logic : GraphStageLogic
            {
                private readonly Emit1234 _emit;

                public Emit1234Logic(Emit1234 emit) : base(emit.Shape)
                {
                    _emit = emit;
                    SetHandler(emit._in, EagerTerminateInput);
                    SetHandler(emit._out, EagerTerminateOutput);
                }

                public override void PreStart()
                {
                    Emit(_emit._out, 1, () => Emit(_emit._out, 2));
                    Emit(_emit._out, 3, () => Emit(_emit._out, 4));
                }
            }

            #endregion

            private readonly Inlet<int> _in = new Inlet<int>("in");
            private readonly Outlet<int> _out = new Outlet<int>("out");

            public Emit1234()
            {
                Shape = new FlowShape<int, int>(_in, _out);
            }

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Emit1234Logic(this);
        }

        private class Emit5678 : GraphStage<FlowShape<int, int>>
        {
            #region internal classes

            private class Emit5678Logic : GraphStageLogic
            {
                public Emit5678Logic(Emit5678 emit) : base(emit.Shape)
                {
                    SetHandler(emit._in, onPush: () => Push(emit._out, Grab(emit._in)), onUpstreamFinish: () =>
                    {
                        Emit(emit._out, 5, () => Emit(emit._out, 6));
                        Emit(emit._out, 7, () => Emit(emit._out, 8));
                        CompleteStage();
                    });
                    SetHandler(emit._out, onPull: () => Pull(emit._in));
                }
            }

            #endregion

            private readonly Inlet<int> _in = new Inlet<int>("in");
            private readonly Outlet<int> _out = new Outlet<int>("out");

            public Emit5678()
            {
                Shape = new FlowShape<int, int>(_in, _out);
            }

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Emit5678Logic(this);
        }

        private class PassThrough : GraphStage<FlowShape<int, int>>
        {
            #region internal classes

            private class PassThroughLogic : GraphStageLogic
            {
                public PassThroughLogic(PassThrough emit) : base(emit.Shape)
                {
                    SetHandler(emit.In, onPush: () => Push(emit.Out, Grab(emit.In)),
                        onUpstreamFinish: () => Complete(emit.Out));
                    SetHandler(emit.Out, onPull: () => Pull(emit.In));
                }
            }

            #endregion

            public Inlet<int> In { get; } = new Inlet<int>("in");

            public Outlet<int> Out { get; } = new Outlet<int>("out");

            public PassThrough()
            {
                Shape = new FlowShape<int, int>(In, Out);
            }

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new PassThroughLogic(this);
        }

        private class EmitEmptyIterable : GraphStage<SourceShape<int>>
        {
            #region internal classes

            private class EmitEmptyIterableLogic : GraphStageLogic
            {
                public EmitEmptyIterableLogic(EmitEmptyIterable emit) : base(emit.Shape)
                {
                    SetHandler(emit._out, () => EmitMultiple(emit._out, Enumerable.Empty<int>(), () => Emit(emit._out, 42, CompleteStage)));
                }
            }
            
            #endregion

            private readonly Outlet<int> _out = new Outlet<int>("out");

            public EmitEmptyIterable()
            {
                Shape = new SourceShape<int>(_out);
            }

            public override SourceShape<int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new EmitEmptyIterableLogic(this);
        }

        private class ReadNEmitN : GraphStage<FlowShape<int, int>>
        {
            private readonly int _n;

            #region internal classes

            private class ReadNEmitNLogic : GraphStageLogic
            {
                private readonly ReadNEmitN _emit;

                public ReadNEmitNLogic(ReadNEmitN emit) : base(emit.Shape)
                {
                    _emit = emit;
                    SetHandler(emit.Shape.Inlet, EagerTerminateInput);
                    SetHandler(emit.Shape.Outlet, EagerTerminateOutput);
                }

                public override void PreStart()
                {
                    ReadMany(_emit.Shape.Inlet, _emit._n,
                        e => EmitMultiple(_emit.Shape.Outlet, e.GetEnumerator(), CompleteStage), _ => { });
                }
            }

            #endregion

            public ReadNEmitN(int n)
            {
                _n = n;
            }

            public override FlowShape<int, int> Shape { get; } = new FlowShape<int, int>(new Inlet<int>("readN.in"),
                new Outlet<int>("readN.out"));

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new ReadNEmitNLogic(this);
        }

        private class ReadNEmitRestOnComplete : GraphStage<FlowShape<int, int>>
        {
            private readonly int _n;

            #region internal classes

            private class ReadNEmitRestOnCompleteLogic : GraphStageLogic
            {
                private readonly ReadNEmitRestOnComplete _emit;

                public ReadNEmitRestOnCompleteLogic(ReadNEmitRestOnComplete emit) : base(emit.Shape)
                {
                    _emit = emit;
                    SetHandler(emit.Shape.Inlet, EagerTerminateInput);
                    SetHandler(emit.Shape.Outlet, EagerTerminateOutput);
                }

                public override void PreStart()
                {
                    ReadMany(_emit.Shape.Inlet, _emit._n,
                        _=> FailStage(new IllegalStateException("Shouldn't happen!")),
                        e=> EmitMultiple(_emit.Shape.Outlet, e.GetEnumerator(), CompleteStage));
                }
            }

            #endregion

            public ReadNEmitRestOnComplete(int n)
            {
                _n = n;
            }

            public override FlowShape<int, int> Shape { get; } = new FlowShape<int, int>(new Inlet<int>("readN.in"),
                new Outlet<int>("readN.out"));

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new ReadNEmitRestOnCompleteLogic(this);
        }

        private sealed class RandomLettersSource : GraphStage<SourceShape<string>>
        {
            #region internal classes

            private sealed class Logic : GraphStageLogic
            {
                public Logic(RandomLettersSource stage) : base(stage.Shape)
                {
                    SetHandler(stage.Out, onPull: () =>
                    {
                        var c = NextChar(); // ASCII lower case letters

                        Log.Debug($"Randomly generated: {c}");

                        Push(stage.Out, c.ToString());
                    });
                }

                private static char NextChar() => (char) ThreadLocalRandom.Current.Next('a', 'z' + 1);
            }

            #endregion

            public RandomLettersSource()
            {
                Shape = new SourceShape<string>(Out);
            }

            private Outlet<string> Out { get; } = new Outlet<string>("RandomLettersSource.out");

            public override SourceShape<string> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private static readonly Config Config = ConfigurationFactory.ParseString("akka.loglevel = DEBUG");

        private ActorMaterializer Materializer { get; }

        public GraphStageLogicSpec(ITestOutputHelper output) : base(output, Config)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void A_GraphStageLogic_must_read_N_and_emit_N_before_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 10))
                .Via(new ReadNEmitN(2))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(10)
                .ExpectNext(1, 2)
                .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_read_N_should_not_emit_if_upstream_completes_before_N_is_sent()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .Via(new ReadNEmitN(6))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(10)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_read_N_should_not_emit_if_upstream_fails_before_N_is_sent()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new ArgumentException("Don't argue like that!");
                Source.From(Enumerable.Range(1, 5))
                    .Select(x =>
                    {
                        if (x > 3)
                            throw error;
                        return x;
                    })
                    .Via(new ReadNEmitN(6))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(10)
                    .ExpectError().Should().Be(error);
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_read_N_should_provide_elements_read_if_OnComplete_happens_before_N_elements_have_been_seen()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .Via(new ReadNEmitRestOnComplete(6))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(10)
                    .ExpectNext(1, 2, 3, 4, 5)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_emit_all_things_before_completing()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .Via(new Emit1234().Named("testStage"))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectNext(1)
                    //emitting with callback gives nondeterminism whether 2 or 3 will be pushed first
                    .ExpectNextUnordered(2, 3)
                    .ExpectNext(4)
                    .ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void A_GraphStageLogic_must_emit_all_things_before_completing_with_two_fused_stages()
        {
            this.AssertAllStagesStopped(() =>
            {
                var flow = Flow.Create<int>().Via(new Emit1234()).Via(new Emit5678());
                var g = Streams.Implementation.Fusing.Fusing.Aggressive(flow);

                Source.Empty<int>()
                    .Via(g)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(9)
                    .ExpectNext(1)
                    //emitting with callback gives nondeterminism whether 2 or 3 will be pushed first
                    .ExpectNextUnordered(2, 3)
                    .ExpectNext(4)
                    .ExpectNext(5)
                    //emitting with callback gives nondeterminism whether 6 or 7 will be pushed first
                    .ExpectNextUnordered(6, 7)
                    .ExpectNext(8)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_emit_all_things_before_completing_with_three_fused_stages()
        {
            this.AssertAllStagesStopped(() =>
            {
                var flow = Flow.Create<int>().Via(new Emit1234()).Via(new PassThrough()).Via(new Emit5678());
                var g = Streams.Implementation.Fusing.Fusing.Aggressive(flow);

                Source.Empty<int>()
                    .Via(g)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(9)
                    .ExpectNext(1)
                    //emitting with callback gives nondeterminism whether 2 or 3 will be pushed first
                    .ExpectNextUnordered(2, 3)
                    .ExpectNext(4)
                    .ExpectNext(5)
                    //emitting with callback gives nondeterminism whether 6 or 7 will be pushed first
                    .ExpectNextUnordered(6, 7)
                    .ExpectNext(8)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_emit_properly_after_empty_iterable()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.FromGraph(new EmitEmptyIterable())
                    .RunWith(Sink.Seq<int>(), Materializer)
                    .Result.Should()
                    .HaveCount(1)
                    .And.OnlyContain(x => x == 42);
            }, Materializer);
        }

        [Fact]
        public void A_GraphStageLogic_must_support_logging_in_custom_graphstage()
        {
            const int n = 10;
            EventFilter.Debug(start: "Randomly generated").Expect(n, () =>
            {
                Source.FromGraph(new RandomLettersSource())
                    .Take(n)
                    .RunWith(Sink.Ignore<string>(), Materializer)
                    .Wait(TimeSpan.FromSeconds(3));
            });
        }

        [Fact]
        public void A_GraphStageLogic_must_invoke_livecycle_hooks_in_the_right_order()
        {
            this.AssertAllStagesStopped(() =>
            {
                var g = new LifecycleStage(TestActor);

                Source.Single(1).Via(g).RunWith(Sink.Ignore<int>(), Materializer);
                ExpectMsg("preStart");
                ExpectMsg("pulled");
                ExpectMsg("postStop");

            }, Materializer);
        }

        private class LifecycleStage : GraphStage<FlowShape<int, int>>
        {
            #region internal class

            private class LifecycleLogic : GraphStageLogic
            {
                private readonly IActorRef _testActor;

                public LifecycleLogic(LifecycleStage stage, IActorRef testActor) : base(stage.Shape)
                {
                    _testActor = testActor;
                    SetHandler(stage._in, EagerTerminateInput);
                    SetHandler(stage._out, () =>
                    {
                        CompleteStage();
                        testActor.Tell("pulled");
                    });
                }

                public override void PreStart()
                {
                    _testActor.Tell("preStart");
                }

                public override void PostStop()
                {
                    _testActor.Tell("postStop");
                }
            }

            #endregion

            private readonly Inlet<int> _in = new Inlet<int>("in");
            private readonly Outlet<int> _out = new Outlet<int>("out");
            private readonly IActorRef _testActor;

            public LifecycleStage(IActorRef testActor)
            {
                _testActor = testActor;
                Shape = new FlowShape<int, int>(_in, _out);
            }

            public override FlowShape<int, int> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new LifecycleLogic(this, _testActor);
        }

        [Fact]
        public void A_GraphStageLogic_must_not_double_terminate_a_single_stage()
        {
            WithBaseBuilderSetup(
                new GraphStage<FlowShape<int, int>>[] {new DoubleTerminateStage(TestActor), new PassThrough()},
                interpreter =>
                {
                    interpreter.Complete(interpreter.Connections[0]);
                    interpreter.Cancel(interpreter.Connections[1]);
                    interpreter.Execute(2);

                    ExpectMsg("postStop2");
                    ExpectNoMsg(0);

                    interpreter.IsCompleted.Should().BeFalse();
                    interpreter.IsSuspended.Should().BeFalse();
                    interpreter.IsStageCompleted(interpreter.Logics[0]).Should().BeTrue();
                    interpreter.IsStageCompleted(interpreter.Logics[1]).Should().BeFalse();
                });
        }

        private class DoubleTerminateStage : GraphStage<FlowShape<int, int>>
        {
            #region internal class

            private class DoubleTerminateLogic : GraphStageLogic
            {
                private readonly IActorRef _testActor;

                public DoubleTerminateLogic(DoubleTerminateStage stage, IActorRef testActor) : base(stage.Shape)
                {
                    _testActor = testActor;
                    SetHandler(stage.In, EagerTerminateInput);
                    SetHandler(stage.Out, EagerTerminateOutput);
                }

                public override void PostStop()
                {
                    _testActor.Tell("postStop2");
                }
            }

            #endregion

            private readonly IActorRef _testActor;

            public DoubleTerminateStage(IActorRef testActor)
            {
                _testActor = testActor;
                Shape = new FlowShape<int, int>(In, Out);
            }

            public override FlowShape<int, int> Shape { get; }
            public Inlet<int> In { get; } = new Inlet<int>("in");
            public Outlet<int> Out { get; } = new Outlet<int>("out");

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new DoubleTerminateLogic(this, _testActor);
        }
    }
}
