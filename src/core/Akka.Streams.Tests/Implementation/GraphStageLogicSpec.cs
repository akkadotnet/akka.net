// -----------------------------------------------------------------------
//  <copyright file="GraphStageLogicSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.Streams.Tests.Implementation.Fusing;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation;

public class GraphStageLogicSpec : GraphInterpreterSpecKit
{
    private static readonly Config Config = ConfigurationFactory.ParseString("akka.loglevel = DEBUG");

    public GraphStageLogicSpec(ITestOutputHelper output) : base(output, Config)
    {
        Materializer = ActorMaterializer.Create(Sys);
    }

    private ActorMaterializer Materializer { get; }

    [Fact]
    public async Task A_GraphStageLogic_must_read_N_and_emit_N_before_completing()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            await Source.From(Enumerable.Range(1, 10))
                .Via(new ReadNEmitN(2))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(10)
                .ExpectNext(1, 2)
                .ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_GraphStageLogic_must_read_N_should_not_emit_if_upstream_completes_before_N_is_sent()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            await Source.From(Enumerable.Range(1, 5))
                .Via(new ReadNEmitN(6))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(10)
                .ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_GraphStageLogic_must_read_N_should_not_emit_if_upstream_fails_before_N_is_sent()
    {
        await this.AssertAllStagesStoppedAsync(() =>
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
            return Task.CompletedTask;
        }, Materializer);
    }

    [Fact]
    public async Task
        A_GraphStageLogic_must_read_N_should_provide_elements_read_if_OnComplete_happens_before_N_elements_have_been_seen()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            await Source.From(Enumerable.Range(1, 5))
                .Via(new ReadNEmitRestOnComplete(6))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(10)
                .ExpectNext(1, 2, 3, 4, 5)
                .ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_GraphStageLogic_must_emit_all_things_before_completing()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            await Source.Empty<int>()
                .Via(new Emit1234().Named("testStage"))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(5)
                .ExpectNext(1)
                //emitting with callback gives nondeterminism whether 2 or 3 will be pushed first                                                                             
                .ExpectNextUnordered(2, 3)
                .ExpectNext(4)
                .ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_GraphStageLogic_must_emit_all_things_before_completing_with_two_fused_stages()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var flow = Flow.Create<int>().Via(new Emit1234()).Via(new Emit5678());
            var g = Streams.Implementation.Fusing.Fusing.Aggressive(flow);

            await Source.Empty<int>()
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
                .ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_GraphStageLogic_must_emit_all_things_before_completing_with_three_fused_stages()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var flow = Flow.Create<int>().Via(new Emit1234()).Via(new PassThrough()).Via(new Emit5678());
            var g = Streams.Implementation.Fusing.Fusing.Aggressive(flow);

            await Source.Empty<int>()
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
                .ExpectCompleteAsync();
        }, Materializer);
    }

    [Fact]
    public async Task A_GraphStageLogic_must_emit_properly_after_empty_iterable()
    {
        await this.AssertAllStagesStoppedAsync(() =>
        {
            Source.FromGraph(new EmitEmptyIterable())
                .RunWith(Sink.Seq<int>(), Materializer)
                .Result.Should()
                .HaveCount(1)
                .And.OnlyContain(x => x == 42);
            return Task.CompletedTask;
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
    public async Task A_GraphStageLogic_must_invoke_livecycle_hooks_in_the_right_order()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var g = new LifecycleStage(TestActor);

            await Source.Single(1).Via(g).RunWith(Sink.Ignore<int>(), Materializer);
            await ExpectMsgAsync("preStart");
            await ExpectMsgAsync("pulled");
            await ExpectMsgAsync("postStop");
        }, Materializer);
    }

    [Fact]
    public void A_GraphStageLogic_must_not_double_terminate_a_single_stage()
    {
        WithBaseBuilderSetup(
            new GraphStage<FlowShape<int, int>>[] { new DoubleTerminateStage(TestActor), new PassThrough() },
            async interpreter =>
            {
                interpreter.Complete(interpreter.Connections[0]);
                interpreter.Cancel(interpreter.Connections[1],
                    SubscriptionWithCancelException.NoMoreElementsNeeded.Instance);
                interpreter.Execute(2);

                await ExpectMsgAsync("postStop2");
                await ExpectNoMsgAsync(0);

                interpreter.IsCompleted.Should().BeFalse();
                interpreter.IsSuspended.Should().BeFalse();
                interpreter.IsStageCompleted(interpreter.Logics[0]).Should().BeTrue();
                interpreter.IsStageCompleted(interpreter.Logics[1]).Should().BeFalse();
            });
    }

    private class Emit1234 : GraphStage<FlowShape<int, int>>
    {
        private readonly Inlet<int> _in = new("in");
        private readonly Outlet<int> _out = new("out");

        public Emit1234()
        {
            Shape = new FlowShape<int, int>(_in, _out);
        }

        public override FlowShape<int, int> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Emit1234Logic(this);
        }

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
    }

    private class Emit5678 : GraphStage<FlowShape<int, int>>
    {
        private readonly Inlet<int> _in = new("in");
        private readonly Outlet<int> _out = new("out");

        public Emit5678()
        {
            Shape = new FlowShape<int, int>(_in, _out);
        }

        public override FlowShape<int, int> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Emit5678Logic(this);
        }

        #region internal classes

        private class Emit5678Logic : GraphStageLogic
        {
            public Emit5678Logic(Emit5678 emit) : base(emit.Shape)
            {
                SetHandler(emit._in, () => Push(emit._out, Grab(emit._in)), () =>
                {
                    Emit(emit._out, 5, () => Emit(emit._out, 6));
                    Emit(emit._out, 7, () => Emit(emit._out, 8));
                    CompleteStage();
                });
                SetHandler(emit._out, () => Pull(emit._in));
            }
        }

        #endregion
    }

    private class PassThrough : GraphStage<FlowShape<int, int>>
    {
        public PassThrough()
        {
            Shape = new FlowShape<int, int>(In, Out);
        }

        public Inlet<int> In { get; } = new("in");

        public Outlet<int> Out { get; } = new("out");

        public override FlowShape<int, int> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new PassThroughLogic(this);
        }

        #region internal classes

        private class PassThroughLogic : GraphStageLogic
        {
            public PassThroughLogic(PassThrough emit) : base(emit.Shape)
            {
                SetHandler(emit.In, () => Push(emit.Out, Grab(emit.In)),
                    () => Complete(emit.Out));
                SetHandler(emit.Out, () => Pull(emit.In));
            }
        }

        #endregion
    }

    private class EmitEmptyIterable : GraphStage<SourceShape<int>>
    {
        private readonly Outlet<int> _out = new("out");

        public EmitEmptyIterable()
        {
            Shape = new SourceShape<int>(_out);
        }

        public override SourceShape<int> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new EmitEmptyIterableLogic(this);
        }

        #region internal classes

        private class EmitEmptyIterableLogic : GraphStageLogic
        {
            public EmitEmptyIterableLogic(EmitEmptyIterable emit) : base(emit.Shape)
            {
                SetHandler(emit._out,
                    () => EmitMultiple(emit._out, Enumerable.Empty<int>(), () => Emit(emit._out, 42, CompleteStage)));
            }
        }

        #endregion
    }

    private class ReadNEmitN : GraphStage<FlowShape<int, int>>
    {
        private readonly int _n;

        public ReadNEmitN(int n)
        {
            _n = n;
        }

        public override FlowShape<int, int> Shape { get; } =
            new(new Inlet<int>("readN.in"), new Outlet<int>("readN.out"));

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ReadNEmitNLogic(this);
        }

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
    }

    private class ReadNEmitRestOnComplete : GraphStage<FlowShape<int, int>>
    {
        private readonly int _n;

        public ReadNEmitRestOnComplete(int n)
        {
            _n = n;
        }

        public override FlowShape<int, int> Shape { get; } = new(new Inlet<int>("readN.in"),
            new Outlet<int>("readN.out"));

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ReadNEmitRestOnCompleteLogic(this);
        }

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
                    _ => FailStage(new IllegalStateException("Shouldn't happen!")),
                    e => EmitMultiple(_emit.Shape.Outlet, e.GetEnumerator(), CompleteStage));
            }
        }

        #endregion
    }

    private sealed class RandomLettersSource : GraphStage<SourceShape<string>>
    {
        public RandomLettersSource()
        {
            Shape = new SourceShape<string>(Out);
        }

        private Outlet<string> Out { get; } = new("RandomLettersSource.out");

        public override SourceShape<string> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }

        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            public Logic(RandomLettersSource stage) : base(stage.Shape)
            {
                SetHandler(stage.Out, () =>
                {
                    var c = NextChar(); // ASCII lower case letters

                    Log.Debug($"Randomly generated: {c}");

                    Push(stage.Out, c.ToString());
                });
            }

            private static char NextChar()
            {
                return (char)ThreadLocalRandom.Current.Next('a', 'z' + 1);
            }
        }

        #endregion
    }

    private class LifecycleStage : GraphStage<FlowShape<int, int>>
    {
        private readonly Inlet<int> _in = new("in");
        private readonly Outlet<int> _out = new("out");
        private readonly IActorRef _testActor;

        public LifecycleStage(IActorRef testActor)
        {
            _testActor = testActor;
            Shape = new FlowShape<int, int>(_in, _out);
        }

        public override FlowShape<int, int> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new LifecycleLogic(this, _testActor);
        }

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
    }

    private class DoubleTerminateStage : GraphStage<FlowShape<int, int>>
    {
        private readonly IActorRef _testActor;

        public DoubleTerminateStage(IActorRef testActor)
        {
            _testActor = testActor;
            Shape = new FlowShape<int, int>(In, Out);
        }

        public override FlowShape<int, int> Shape { get; }
        public Inlet<int> In { get; } = new("in");
        public Outlet<int> Out { get; } = new("out");

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new DoubleTerminateLogic(this, _testActor);
        }

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
    }
}