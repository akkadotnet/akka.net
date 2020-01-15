//-----------------------------------------------------------------------
// <copyright file="LifecycleInterpreterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using OnError = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnError;
using Cancel = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.Cancel;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnComplete;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.RequestOne;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnNext;


namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class LifecycleInterpreterSpec : GraphInterpreterSpec
    {
        private sealed class PreStartAndPostStopIdentity<T> : SimpleLinearGraphStage<T>
        {
            #region Logic 

            private sealed class Logic : GraphStageLogic
            {
                private readonly PreStartAndPostStopIdentity<T> _stage;

                public Logic(PreStartAndPostStopIdentity<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, ()=>Pull(stage.Inlet));
                    SetHandler(stage.Inlet, () => Push(stage.Outlet, Grab(stage.Inlet)), () =>
                    {
                        stage._onUpstreamCompleted();
                        CompleteStage();
                    }, ex =>
                    {
                        stage._onUpstreamFailed(ex);
                        FailStage(ex);
                    });
                }

                public override void PreStart() => _stage._onStart();

                public override void PostStop() => _stage._onStop();
            }

            #endregion

            private readonly Action _onStart;
            private readonly Action _onStop;
            private readonly Action _onUpstreamCompleted;
            private readonly Action<Exception> _onUpstreamFailed;

            public PreStartAndPostStopIdentity(Action onStart = null, Action onStop = null,
                Action onUpstreamCompleted = null, Action<Exception> onUpstreamFailed = null)
            {
                _onStart = onStart ?? (() => {});
                _onStop = onStop ?? (() => { });
                _onUpstreamCompleted = onUpstreamCompleted ?? (() => { });
                _onUpstreamFailed = onUpstreamFailed ?? (_ => { });
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "PreStartAndPostStopIdentity";
        }

        private sealed class PreStartFailer<T> : SimpleLinearGraphStage<T>
        {
            #region Logic 

            private sealed class Logic : GraphStageLogic
            {
                private readonly PreStartFailer<T> _stage;

                public Logic(PreStartFailer<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, () => Push(stage.Outlet, Grab(stage.Inlet)));
                }

                public override void PreStart() => _stage._pleaseThrow();
            }

            #endregion

            private readonly Action _pleaseThrow;

            public PreStartFailer(Action pleaseThrow = null)
            {
                _pleaseThrow = pleaseThrow ?? (()=> {});
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "PreStartFailer";
        }

        private sealed class PostStopFailer<T> : SimpleLinearGraphStage<T>
        {
            #region Logic 

            private sealed class Logic : GraphStageLogic
            {
                private readonly PostStopFailer<T> _stage;

                public Logic(PostStopFailer<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, () => Push(stage.Outlet, Grab(stage.Inlet)));
                }

                public override void PostStop() => _stage._ex();
            }

            #endregion

            private readonly Func<Exception> _ex;

            public PostStopFailer(Func<Exception> ex = null)
            {
                _ex = ex ?? (() => new Exception());
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "PostStopFailer";
        }

        // This test is related to issue #17351 (jvm)
        private sealed class PushFinishStage<T> : SimpleLinearGraphStage<T>
        {
            #region Logic 

            private sealed class Logic : GraphStageLogic
            {
                private readonly PushFinishStage<T> _stage;

                public Logic(PushFinishStage<T> stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, () =>
                    {
                        Push(stage.Outlet, Grab(stage.Inlet));
                        CompleteStage();
                    }, () => FailStage(new TestException("Cannot happen")));
                }

                public override void PostStop() => _stage._onPostStop();
            }

            #endregion

            private readonly Action _onPostStop;

            public PushFinishStage(Action onPostSTop = null)
            {
                _onPostStop = onPostSTop ?? (()=> {});
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "PushFinish";
        }

        [Fact]
        public void Interpreter_must_call_PreStart_in_order_on_stages()
        {
            var ops = new []{
                new PreStartAndPostStopIdentity<string>(onStart: () => TestActor.Tell("start-a")),
                new PreStartAndPostStopIdentity<string>(onStart: () => TestActor.Tell("start-b")),
                new PreStartAndPostStopIdentity<string>(onStart: () => TestActor.Tell("start-c")),
            };

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                ExpectMsg("start-a");
                ExpectMsg("start-b");
                ExpectMsg("start-c");
                ExpectNoMsg(300);
                upstream.OnComplete();
            });
        }

        [Fact]
        public void Interpreter_must_call_PostStop_in_order_on_stages_when_upstream_completes()
        {
            var ops = new []{
                new PreStartAndPostStopIdentity<string>(onUpstreamCompleted: () => TestActor.Tell("complete-a"), onStop: ()=> TestActor.Tell("stop-a")),
                new PreStartAndPostStopIdentity<string>(onUpstreamCompleted: () => TestActor.Tell("complete-b"), onStop: ()=> TestActor.Tell("stop-b")),
                new PreStartAndPostStopIdentity<string>(onUpstreamCompleted: () => TestActor.Tell("complete-c"), onStop: ()=> TestActor.Tell("stop-c")),
            };

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                upstream.OnComplete();
                ExpectMsg("complete-a");
                ExpectMsg("stop-a");
                ExpectMsg("complete-b");
                ExpectMsg("stop-b");
                ExpectMsg("complete-c");
                ExpectMsg("stop-c");
                ExpectNoMsg(300);
                upstream.OnComplete();
            });
        }

        [Fact]
        public void Interpreter_must_call_PostStop_in_order_on_stages_when_upstream_OnErrors()
        {
            var op = new PreStartAndPostStopIdentity<string>(onUpstreamFailed: ex => TestActor.Tell(ex.Message),
                onStop: () => TestActor.Tell("stop-c"));

            WithOneBoundedSetup(op, (lastEvents, upstream, downstream) =>
            {
                var msg = "Boom! Boom! Boom!";
                upstream.OnError(new TestException(msg));
                ExpectMsg(msg);
                ExpectMsg("stop-c");
                ExpectNoMsg(300);
            });
        }

        [Fact]
        public void Interpreter_must_call_PostStop_in_order_on_stages_when_downstream_cancels()
        {
            var ops = new []{
                new PreStartAndPostStopIdentity<string>(onStop: ()=> TestActor.Tell("stop-a")),
                new PreStartAndPostStopIdentity<string>(onStop: ()=> TestActor.Tell("stop-b")),
                new PreStartAndPostStopIdentity<string>(onStop: ()=> TestActor.Tell("stop-c")),
            };

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                downstream.Cancel();
                ExpectMsg("stop-c");
                ExpectMsg("stop-b");
                ExpectMsg("stop-a");
                ExpectNoMsg(300);
            });
        }

        [Fact]
        public void Interpreter_must_call_PreStart_before_PostStop()
        {
            var op = new PreStartAndPostStopIdentity<string>(onStart: () => TestActor.Tell("start-a"),
                onStop: () => TestActor.Tell("stop-a"));


            WithOneBoundedSetup(op, (lastEvents, upstream, downstream) =>
            {
                ExpectMsg("start-a");
                ExpectNoMsg(300);
                upstream.OnComplete();
                ExpectMsg("stop-a");
                ExpectNoMsg(300);
            });
        }

        [Fact]
        public void Interpreter_must_call_OnError_when_PreStart_fails()
        {
            var op = new PreStartFailer<string>(() =>
            {
                throw new TestException("Boom!");
            });
            
            WithOneBoundedSetup(op, (lastEvents, upstream, downstream) =>
            {
                var events = lastEvents().ToArray();
                events[0].Should().Be(new Cancel());
                events[1].Should().BeOfType<OnError>();
                ((OnError) events[1]).Cause.Should().BeOfType<TestException>();
                ((OnError) events[1]).Cause.Message.Should().Be("Boom!");
            });
        }

        [Fact]
        public void Interpreter_must_not_blow_up_when_PostStop_fails()
        {
            var op = new PostStopFailer<string>(() =>
            {
                throw new TestException("Boom!");
            });

            WithOneBoundedSetup(op, (lastEvents, upstream, downstream) =>
            {
                upstream.OnComplete();
                lastEvents().Should().Equal(new OnComplete());
            });
        }

        [Fact]
        public void Interpreter_must_call_OnError_when_PreStart_fails_with_stages_after()
        {
            var ops = new IGraphStageWithMaterializedValue<FlowShape<string, string>, object>[]
            {
                new Select<string, string>(x => x),
                new PreStartFailer<string>(() =>
                {
                    throw new TestException("Boom!");
                }),
                new Select<string, string>(x => x),
            };

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                var events = lastEvents().ToArray();
                events[0].Should().Be(new Cancel());
                events[1].Should().BeOfType<OnError>();
                ((OnError)events[1]).Cause.Should().BeOfType<TestException>();
                ((OnError)events[1]).Cause.Message.Should().Be("Boom!");
            });
        }

        [Fact]
        public void Interpreter_must_continue_wit_stream_shutdown_when_PostStop_fails()
        {
            var op = new PostStopFailer<string>(() =>
            {
                throw new TestException("Boom!");
            });

            WithOneBoundedSetup(op, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();

                upstream.OnComplete();
                lastEvents().Should().Equal(new OnComplete());
            });
        }

        [Fact]
        public void Interpreter_must_call_PostStop_when_PushAndFinish_called_if_upstream_completes_with_PushAndFinish()
        {
            var op = new PushFinishStage<string>(() => TestActor.Tell("stop"));

            WithOneBoundedSetup(op, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();

                downstream.RequestOne();
                lastEvents().Should().Equal(new RequestOne());

                upstream.OnNextAndComplete("foo");
                lastEvents().Should().Equal(new OnNext("foo"), new OnComplete());
                ExpectMsg("stop");
            });
        }

        [Fact]
        public void Interpreter_must_call_PostStop_when_PushAndFinish_called_with_PushAndFinish_if_indirect_upsteam_completes_with_PushAndFinish()
        {
            var ops = new IGraphStageWithMaterializedValue<FlowShape<string, string>, object>[]
            {
                new Select<string, string>(x => x),
                new PushFinishStage<string>(() => TestActor.Tell("stop")), 
                new Select<string, string>(x => x)
            };

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();

                downstream.RequestOne();
                lastEvents().Should().Equal(new RequestOne());

                upstream.OnNextAndComplete("foo");
                lastEvents().Should().Equal(new OnNext("foo"), new OnComplete());
                ExpectMsg("stop");
            });
        }

        [Fact]
        public void Interpreter_must_call_PostStop_when_PushAndFinish_called_with_PushAndFinish_if_upsteam_completes_with_PushAndFinish_and_downstream_immediately_pulls()
        {
            var ops = new IGraphStageWithMaterializedValue<FlowShape<string, string>, object>[]
            {
                new PushFinishStage<string>(() => TestActor.Tell("stop")),
                new Aggregate<string, string>("", (x, y) => x+y)
            };

            WithOneBoundedSetup(ops, (lastEvents, upstream, downstream) =>
            {
                lastEvents().Should().BeEmpty();

                downstream.RequestOne();
                lastEvents().Should().Equal(new RequestOne());

                upstream.OnNextAndComplete("foo");
                lastEvents().Should().Equal(new OnNext("foo"), new OnComplete());
                ExpectMsg("stop");
            });
        }
    }
}
