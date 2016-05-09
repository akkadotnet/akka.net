//-----------------------------------------------------------------------
// <copyright file="LifecycleInterpreterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private sealed class PreStartAndPostStopIdentity<T> : PushStage<T, T>
        {
            private readonly Action<ILifecycleContext> _onStart;
            private readonly Action _onStop;
            private readonly Action _onUpstreamCompleted;
            private readonly Action<Exception> _onUpstreamFailed;

            public PreStartAndPostStopIdentity(Action<ILifecycleContext> onStart = null, Action onStop = null,
                Action onUpstreamCompleted = null, Action<Exception> onUpstreamFailed = null)
            {
                _onStart = onStart ?? (_ => {});
                _onStop = onStop ?? (() => { });
                _onUpstreamCompleted = onUpstreamCompleted ?? (() => { });
                _onUpstreamFailed = onUpstreamFailed ?? (_ => { });
            }

            public override void PreStart(ILifecycleContext context) => _onStart(context);

            public override ITerminationDirective OnUpstreamFinish(IContext<T> context)
            {
                _onUpstreamCompleted();
                return base.OnUpstreamFinish(context);
            }

            public override ITerminationDirective OnUpstreamFailure(Exception cause, IContext<T> context)
            {
                _onUpstreamFailed(cause);
                return base.OnUpstreamFailure(cause, context);
            }

            public override void PostStop() => _onStop();

            public override ISyncDirective OnPush(T element, IContext<T> context) => context.Push(element);
        }

        private sealed class PreStartFailer<T> : PushStage<T, T>
        {
            private readonly Action _pleaseThrow;

            public PreStartFailer(Action pleaseThrow = null)
            {
                _pleaseThrow = pleaseThrow ?? (()=> {});
            }

            public override void PreStart(ILifecycleContext context) => _pleaseThrow();

            public override ISyncDirective OnPush(T element, IContext<T> context) => context.Push(element);
        }

        private sealed class PostStopFailer<T> : PushStage<T, T>
        {
            private readonly Func<Exception> _ex;

            public PostStopFailer(Func<Exception> ex = null)
            {
                _ex = ex ?? (() => new Exception());
            }

            public override ITerminationDirective OnUpstreamFinish(IContext<T> context) => context.Finish();
            
            public override ISyncDirective OnPush(T element, IContext<T> context) => context.Push(element);

            public override void PostStop()
            {
                throw _ex();
            }
        }

        // This test is related to issue #17351 (jvm)
        private sealed class PushFinishStage<T> : PushStage<T, T>
        {
            private readonly Action _onPostSTop;

            public PushFinishStage(Action onPostSTop = null)
            {
                _onPostSTop = onPostSTop ?? (()=> {});
            }

            public override ITerminationDirective OnUpstreamFinish(IContext<T> context) => context.Fail(new TestException("Cannot happen"));

            public override void PostStop() => _onPostSTop();

            public override ISyncDirective OnPush(T element, IContext<T> context) => context.PushAndFinish(element);
        }

        [Fact]
        public void Interpreter_must_call_PreStart_in_order_on_stages()
        {
            IStage<string, string>[] ops = {
                new PreStartAndPostStopIdentity<string>(onStart: _ => TestActor.Tell("start-a")),
                new PreStartAndPostStopIdentity<string>(onStart: _ => TestActor.Tell("start-b")),
                new PreStartAndPostStopIdentity<string>(onStart: _ => TestActor.Tell("start-c")),
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
            IStage<string, string>[] ops = {
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
            IStage<string, string>[] ops = {
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
            var op = new PreStartAndPostStopIdentity<string>(onStart: _ => TestActor.Tell("start-a"),
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
            var ops = new IStage<string, string>[]
            {
                new Select<string, string>(x => x, Deciders.StoppingDecider),
                new PreStartFailer<string>(() =>
                {
                    throw new TestException("Boom!");
                }),
                new Select<string, string>(x => x, Deciders.StoppingDecider),
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
            var ops = new IStage<string, string>[]
            {
                new Select<string, string>(x => x, Deciders.StoppingDecider),
                new PushFinishStage<string>(() => TestActor.Tell("stop")), 
                new Select<string, string>(x => x, Deciders.StoppingDecider)
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
            var ops = new IStage<string, string>[]
            {
                new PushFinishStage<string>(() => TestActor.Tell("stop")),
                new Aggregate<string, string>("", (x, y) => x+y, Deciders.StoppingDecider) 
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
