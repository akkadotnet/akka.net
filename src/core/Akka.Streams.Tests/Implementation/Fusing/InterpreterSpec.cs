//-----------------------------------------------------------------------
// <copyright file="InterpreterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnNext;
using Cancel = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.Cancel;
using OnError = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnError;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnComplete;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.RequestOne;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class InterpreterSpec : GraphInterpreterSpecKit
    {
        /*
         * These tests were written for the previous version of the interpreter, the so called OneBoundedInterpreter.
         * These stages are now properly emulated by the GraphInterpreter and many of the edge cases were relevant to
         * the execution model of the old one. Still, these tests are very valuable, so please do not remove.
         */

        public InterpreterSpec(ITestOutputHelper output = null) : base(output)
        {
        }

        private static readonly Take<int> TakeOne = new Take<int>(1);
        private static readonly Take<int> TakeTwo = new Take<int>(2);

        [Fact]
        public void Interpreter_should_implement_map_correctly()
        {
            WithOneBoundedSetup(new Select<int, int>(x => x + 1),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_chain_of_maps_correctly()
        {
            WithOneBoundedSetup(new[]
            {
                new Select<int, int>(x => x + 1),
                new Select<int, int>(x => x * 2),
                new Select<int, int>(x => x + 1)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new OnNext(3));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(5));

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_work_with_only_boundary_ops()
        {
            WithOneBoundedSetup(new IStage<int, int>[0],
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new OnNext(0));

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_one_to_many_many_to_one_chain_correctly()
        {
            WithOneBoundedSetup(new IGraphStageWithMaterializedValue<FlowShape<int, int>, object>[]
            {
                new Doubler<int>(),
                new Where<int>(x => x != 0)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_many_to_one_one_to_many_chain_correctly()
        {
            WithOneBoundedSetup(new IGraphStageWithMaterializedValue<FlowShape<int, int>, object>[]
            {
                new Where<int>(x => x != 0),
                new Doubler<int>()
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.Cancel();
                    lastEvents().Should().BeEquivalentTo(new Cancel());
                });
        }

        [Fact]
        public void Interpreter_should_implement_take()
        {
            WithOneBoundedSetup(TakeTwo,
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new OnNext(0));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1), new Cancel(), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_take_inside_a_chain()
        {
            WithOneBoundedSetup<int, int>(new IGraphStageWithMaterializedValue<Shape, object>[]
            {
                new Where<int>(x => x != 0),
                TakeTwo,
                new Select<int, int>(x => x + 1)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(3), new Cancel(), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_fold()
        {
            WithOneBoundedSetup(new Aggregate<int, int>(0, (agg, x) => agg + x),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnNext(3), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_fold_with_proper_cancel()
        {
            WithOneBoundedSetup(new Aggregate<int, int>(0, (agg, x) => agg + x),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.Cancel();
                    lastEvents().Should().BeEquivalentTo(new Cancel());
                });
        }

        [Fact]
        public void Interpreter_should_work_if_fold_completes_while_not_in_a_push_position()
        {
            WithOneBoundedSetup(new Aggregate<int, int>(0, (agg, x) => agg + x),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    upstream.OnComplete();
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(0), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_grouped()
        {
            WithOneBoundedSetup(new Grouped<int>(3),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(new[] { 0, 1, 2 }));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(3);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnNext(new[] { 3 }), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_batch_conflate()
        {
            WithOneBoundedSetup<int>(new Batch<int, int>(1L, e => 0L, e => e, (agg, x) => agg + x),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEmpty();

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new OnNext(0), new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(3));

                    downstream.RequestOne();
                    lastEvents().Should().BeEmpty();

                    upstream.OnNext(4);
                    lastEvents().Should().BeEquivalentTo(new OnNext(4), new RequestOne());

                    downstream.Cancel();
                    lastEvents().Should().BeEquivalentTo(new Cancel());
                });
        }

        [Fact]
        public void Interpreter_should_implement_expand()
        {
            WithOneBoundedSetup<int>(new Expand<int, int>(e => Enumerable.Repeat(e, int.MaxValue).GetEnumerator()),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne(), new OnNext(0));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(0));

                    upstream.OnNext(1);
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne(), new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_work_with_batch_batch_conflate_conflate()
        {
            WithOneBoundedSetup<int>(new IGraphStageWithMaterializedValue<Shape, object>[]
            {
                new Batch<int, int>(1L, e => 0L, e => e, (agg, x) => agg + x),
                new Batch<int, int>(1L, e => 0L, e => e, (agg, x) => agg + x)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEmpty();

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne(), new OnNext(0));

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(3));

                    downstream.RequestOne();
                    lastEvents().Should().BeEmpty();

                    upstream.OnNext(4);
                    lastEvents().Should().BeEquivalentTo(new RequestOne(), new OnNext(4));

                    downstream.Cancel();
                    lastEvents().Should().BeEquivalentTo(new Cancel());
                });
        }

        [Fact]
        public void Interpreter_should_work_with_expand_expand()
        {
            WithOneBoundedSetup<int>(new IGraphStageWithMaterializedValue<Shape, object>[]
            {
                new Expand<int, int>(e => Enumerable.Range(e, 100).GetEnumerator()),
                new Expand<int, int>(e => Enumerable.Range(e, 100).GetEnumerator())
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(0));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    upstream.OnNext(10);
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(2), new RequestOne()); // one element is still in the pipeline

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(10));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(11));

                    upstream.OnComplete();
                    downstream.RequestOne();
                    // This is correct! If you don't believe, run the interpreter with Debug on
                    lastEvents().Should().BeEquivalentTo(new OnNext(12), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_batch_expand_conflate_expand()
        {
            WithOneBoundedSetup<int>(new IGraphStageWithMaterializedValue<Shape, object>[]
            {
                new Batch<int, int>(1L, e => 0L, e => e, (agg, x) => agg + x),
                new Expand<int, int>(e => Enumerable.Repeat(e, int.MaxValue).GetEnumerator())
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(0));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));

                    downstream.Cancel();
                    lastEvents().Should().BeEquivalentTo(new Cancel());
                });
        }

        [Fact]
        public void Interpreter_should_implement_doubler_batch_doubler_conflate()
        {
            WithOneBoundedSetup<int>(new IGraphStageWithMaterializedValue<Shape, object>[]
            {
                new Doubler<int>(),
                new Batch<int, int>(1L, e => 0L, e => e, (agg, x) => agg + x)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(6));
                });
        }

        // Note, the new interpreter has no jumpback table, still did not want to remove the test
        [Fact]
        public void Interpreter_should_work_with_jumpback_table_and_completed_elements()
        {
            WithOneBoundedSetup(new IGraphStageWithMaterializedValue<FlowShape<int, int>, object>[]
            {
                new Select<int, int>(x => x),
                new Select<int, int>(x => x),
                new KeepGoing<int>(),
                new Select<int, int>(x => x),
                new Select<int, int>(x => x)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));

                    upstream.OnComplete();
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));
                });
        }

        [Fact]
        public void Interpreter_should_work_with_PushAndFinish_if_upstream_completes_with_PushAndFinish()
        {
            WithOneBoundedSetup(new PushFinishStage<int>(),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNextAndComplete(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_work_with_PushAndFinish_if_indirect_upstream_completes_with_PushAndFinish()
        {
            WithOneBoundedSetup(new IGraphStageWithMaterializedValue<FlowShape<int, int>, object>[]
            {
                new Select<int, int>(x => x),
                new PushFinishStage<int>(),
                new Select<int, int>(x => x)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNextAndComplete(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_work_with_PushAndFinish_if_upstream_completes_with_PushAndFinish_and_downstream_immediately_pulls()
        {
            WithOneBoundedSetup(new IGraphStageWithMaterializedValue<FlowShape<int, int>, object>[]
            {
                new PushFinishStage<int>(),
                new Aggregate<int, int>(0, (x, y) => x + y)
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNextAndComplete(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_report_error_if_pull_is_called_while_op_is_terminating()
        {
            WithOneBoundedSetup(new PullWhileOpIsTerminating<int>(),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    EventFilter.Exception<ArgumentException>(new Regex(".*Cannot pull a closed port.*"))
                        .ExpectOne(upstream.OnComplete);

                    var ev = lastEvents();
                    ev.Should().NotBeEmpty();

                    ev.Where(e => !((e as OnError)?.Cause is ArgumentException)).Should().BeEmpty();
                });
        }

        [Fact]
        public void Interpreter_should_implement_take_take()
        {
            WithOneBoundedSetup(new[]
            {
                TakeOne,
                TakeOne
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEquivalentTo(new Cancel(), new OnNext(1), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_implement_take_take_with_PushAndFinish_from_upstream()
        {
            WithOneBoundedSetup(new[]
            {
                TakeOne,
                TakeOne
            },
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNextAndComplete(1);
                    lastEvents().Should().BeEquivalentTo(new OnNext(1), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_should_not_allow_AbsorbTermination_from_OnDownstreamFinish()
        {
            // This test must be kept since it tests the compatibility layer, which while is deprecated it is still here.
            WithOneBoundedSetup(ToGraphStage(new InvalidAbsorbTermination<int>()),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    EventFilter.Exception<NotSupportedException>(
                        "It is not allowed to call AbsorbTermination() from OnDownstreamFinish.")
                        .ExpectOne(() =>
                        {
                            downstream.Cancel();
                            lastEvents().Should().BeEquivalentTo(new Cancel());
                        });
                });
        }

        public class Doubler<T> : SimpleLinearGraphStage<T>
        {
            #region Logic

            private sealed class Logic : GraphStageLogic
            {
                private T _latest;
                private bool _oneMore;

                public Logic(Doubler<T> stage) : base(stage.Shape)
                {
                    // Called when the output port has received a pull, and therefore ready to emit an element, i.e. GraphStageLogic.Push()
                    // is now allowed to be called on this port.
                    SetHandler(stage.Shape.Outlet, onPull: () =>
                    {
                        if (_oneMore)
                        {
                            Push(stage.Shape.Outlet, _latest);
                            _oneMore = false;
                        }
                        else
                            Pull(stage.Shape.Inlet);
                    });

                    SetHandler(stage.Shape.Inlet, onPush: () =>
                    {
                        _latest = Grab(stage.Shape.Inlet);
                        _oneMore = true;
                        Push(stage.Shape.Outlet, _latest);
                    });
                }
            }
            #endregion
            
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        public class KeepGoing<T> : SimpleLinearGraphStage<T>
        {
            #region Logic

            private sealed class Logic : GraphStageLogic
            {
                private T _lastElement;

                public Logic(KeepGoing<T> stage) : base(stage.Shape)
                {
                    // Called when the output port has received a pull, and therefore ready to emit an element, i.e. GraphStageLogic.Push()
                    // is now allowed to be called on this port.
                    SetHandler(stage.Shape.Outlet, onPull: () =>
                    {
                        if (IsClosed(stage.Shape.Inlet))
                            Push(stage.Shape.Outlet, _lastElement);
                        else
                            Pull(stage.Shape.Inlet);
                    });

                    SetHandler(stage.Shape.Inlet, onPush: () =>
                    {
                        _lastElement = Grab(stage.Shape.Inlet);
                        Push(stage.Shape.Outlet, _lastElement);
                    }, onUpstreamFinish: () => { });
                }
            }
            #endregion

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        public class PushFinishStage<T> : SimpleLinearGraphStage<T>
        {
            private sealed class Logic : GraphStageLogic
            {
                private readonly PushFinishStage<T> _stage;

                public Logic(PushFinishStage<T> stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, onPush: () =>
                    {
                        Push(stage.Outlet, Grab(stage.Inlet));
                        CompleteStage();
                    }, onUpstreamFinish: () => FailStage(new TestException("Cannot happen")));
                }

                public override void PostStop() => _stage._onPostStop();
            }

            private readonly Action _onPostStop;

            public PushFinishStage(Action onPostStop = null)
            {
                _onPostStop = onPostStop ?? (() => { });
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        public class PullWhileOpIsTerminating<T> : SimpleLinearGraphStage<T>
        {
            private sealed class Logic : GraphStageLogic
            {
                public Logic(PullWhileOpIsTerminating<T> stage) : base(stage.Shape)
                {
                    SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, onPush: () => Pull(stage.Inlet), onUpstreamFinish: () =>
                    {
                        if (!HasBeenPulled(stage.Inlet))
                            Pull(stage.Inlet);
                    });
                }
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        [Obsolete("Please use GraphStage instead.")]
        public class InvalidAbsorbTermination<T> : PushPullStage<T, T>
        {
            public override ISyncDirective OnPush(T element, IContext<T> context)
            {
                return context.Push(element);
            }

            public override ISyncDirective OnPull(IContext<T> context)
            {
                return context.Pull();
            }

            public override ITerminationDirective OnDownstreamFinish(IContext<T> context)
            {
                return context.AbsorbTermination();
            }
        }
    }
}
