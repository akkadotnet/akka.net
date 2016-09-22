//-----------------------------------------------------------------------
// <copyright file="InterpreterSupervisionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Cancel = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.Cancel;
using Decider = Akka.Streams.Supervision.Decider;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnComplete;
using OnError = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnError;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.OnNext;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup.RequestOne;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class InterpreterSupervisionSpec : GraphInterpreterSpecKit
    {
        // ReSharper disable InconsistentNaming
        private readonly Decider stoppingDecider = Deciders.StoppingDecider;
        private readonly Decider resumingDecider = Deciders.ResumingDecider;
        private readonly Decider restartingDecider = Deciders.RestartingDecider;

        public InterpreterSupervisionSpec(ITestOutputHelper output = null) : base(output)
        {
        }

        [Fact]
        public void Interpreter_error_handling_should_handle_external_failure()
        {
            WithOneBoundedSetup(new Select<int, int>(x => x + 1, stoppingDecider),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    upstream.OnError(TE());
                    lastEvents().Should().BeEquivalentTo(new OnError(TE()));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_emit_failure_when_op_throws()
        {
            WithOneBoundedSetup(new Select<int, int>(x => { if (x == 0) throw TE(); return x; }, stoppingDecider),
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(2));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(0); // boom
                    lastEvents().Should().BeEquivalentTo(new Cancel(), new OnError(TE()));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_emit_failure_when_op_throws_in_middle_of_chain()
        {
            WithOneBoundedSetup(new IStage<int, int>[] {
                new Select<int, int>(x => x + 1, stoppingDecider),
                new Select<int, int>(x => { if (x == 0) throw TE(); return x + 10; }, stoppingDecider),
                new Select<int, int>(x => x + 100, stoppingDecider)
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(113));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(-1); // boom
                    lastEvents().Should().BeEquivalentTo(new Cancel(), new OnError(TE()));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_resume_when_Map_throws_in_middle_of_chain()
        {
            WithOneBoundedSetup(new IStage<int, int>[] {
                new Select<int, int>(x => x + 1, resumingDecider),
                new Select<int, int>(x => { if (x == 0) throw TE(); return x + 10; }, resumingDecider),
                new Select<int, int>(x => x + 100, resumingDecider)
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(113));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(-1); // boom
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(3);
                    lastEvents().Should().BeEquivalentTo(new OnNext(114));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_resume_when_Map_throws_before_Grouped()
        {
            WithOneBoundedSetup<int>(new IGraphStageWithMaterializedValue<Shape, object>[] {
                ToGraphStage(new Select<int, int>(x => x + 1, resumingDecider)),
                ToGraphStage(new Select<int, int>(x => { if (x == 0) throw TE(); return x + 10; }, resumingDecider)),
                ToGraphStage(new Grouped<int>(3))
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(-1); // boom
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(3);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(4);
                    lastEvents().Should().BeEquivalentTo(new OnNext(new[] {13, 14, 15}));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_complete_after_resume_when_Map_throws_before_Grouped()
        {
            WithOneBoundedSetup<int>(new IGraphStageWithMaterializedValue<Shape, object>[] {
                ToGraphStage(new Select<int, int>(x => x + 1, resumingDecider)),
                ToGraphStage(new Select<int, int>(x => { if (x == 0) throw TE(); return x + 10; }, resumingDecider)),
                ToGraphStage(new Grouped<int>(1000))
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(-1); // boom
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(3);
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnComplete();
                    lastEvents().Should().BeEquivalentTo(new OnNext(new[] {13, 14}), new OnComplete());
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_restart_when_OnPush_throws()
        {
            var stage = new RestartTestStage(onPush: (_, element, context) =>
            {
                if (element <= 0) throw TE();
                return null;
            });
            WithOneBoundedSetup(new IStage<int, int>[] {
                new Select<int, int>(x => x + 1, resumingDecider),
                stage,
                new Select<int, int>(x => x + 100, resumingDecider)
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(103));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(-1); // boom
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(3);
                    lastEvents().Should().BeEquivalentTo(new OnNext(104));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_restart_when_OnPush_throws_after_context_Push()
        {
            var stage = new RestartTestStage(onPush: (_, element, context) =>
            {
                var result = context.Push(element);
                if (element <= 0) throw TE();
                return result;
            });
            WithOneBoundedSetup(new IStage<int, int>[] {
                new Select<int, int>(x => x + 1, resumingDecider),
                stage,
                new Select<int, int>(x => x + 100, resumingDecider)
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(103));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(-1); // boom
                    // The element has been pushed before the exception, there is no way back
                    lastEvents().Should().BeEquivalentTo(new OnNext(100));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(3);
                    lastEvents().Should().BeEquivalentTo(new OnNext(104));
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_fail_when_OnPull_throws()
        {
            var stage = new RestartTestStage(onPull: (stg, context) =>
            {
                if (stg.Sum < 0) throw TE();
                return null;
            });
            WithOneBoundedSetup(new IStage<int, int>[] {
                new Select<int, int>(x => x + 1, resumingDecider),
                stage,
                new Select<int, int>(x => x + 100, resumingDecider)
            },
                (lastEvents, upstream, downstream) =>
                {
                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(2);
                    lastEvents().Should().BeEquivalentTo(new OnNext(103));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne());
                    upstream.OnNext(-5); // this will trigger failure of next requestOne (pull)
                    lastEvents().Should().BeEquivalentTo(new OnNext(99));

                    downstream.RequestOne(); // boom
                    lastEvents().Should().BeEquivalentTo(new OnError(TE()), new Cancel());
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_fail_when_Expand_seed_throws()
        {
            WithOneBoundedSetup<int>(new Expand<int, int>(x => { if (x == 2) throw TE(); return new List<int> {x}.Concat(Enumerable.Repeat(-Math.Abs(x), 100)).GetEnumerator(); }),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne(), new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(-1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(-1));

                    upstream.OnNext(2); // boom
                    lastEvents().Should().BeEquivalentTo(new OnError(TE()), new Cancel());
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_fail_when_Expand_extrapolate_throws()
        {
            WithOneBoundedSetup<int>(new Expand<int, int>(x => { if (x == 2) return ContinuallyThrow(); return new List<int> {x}.Concat(Enumerable.Repeat(-Math.Abs(x), 100)).GetEnumerator(); }),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEquivalentTo(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new RequestOne(), new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnNext(-1));

                    upstream.OnNext(2); // boom
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().BeEquivalentTo(new OnError(TE()), new Cancel());
                });
        }

        [Fact]
        public void Interpreter_error_handling_should_fail_when_OnPull_throws_before_pushing_all_generated_elements()
        {
            Action<Decider, bool> test = (decider, absorbTermination) =>
            {
                WithOneBoundedSetup(new OneToManyTestStage(decider, absorbTermination),
                    (lastEvents, upstream, downstream) =>
                    {
                        downstream.RequestOne();
                        lastEvents().Should().BeEquivalentTo(new RequestOne());

                        upstream.OnNext(1);
                        lastEvents().Should().BeEquivalentTo(new OnNext(1));

                        if (absorbTermination)
                        {
                            upstream.OnComplete();
                            lastEvents().Should().BeEmpty();
                        }

                        downstream.RequestOne();
                        lastEvents().Should().BeEquivalentTo(new OnNext(2));

                        downstream.RequestOne();
                        // 3 => boom
                        if (absorbTermination)
                            lastEvents().Should().BeEquivalentTo(new OnError(TE()));
                        else
                            lastEvents().Should().BeEquivalentTo(new OnError(TE()), new Cancel());
                    });
            };

            test(resumingDecider, false);
            test(restartingDecider, false);
            test(resumingDecider, true);
            test(restartingDecider, true);

        }

        private Exception TE()
        {
            return new TestException("TEST");
        }

        public class RestartTestStage : PushPullStage<int, int>
        {
            public int Sum;
            private readonly Func<RestartTestStage, int, IContext<int>, ISyncDirective> _onPush;
            private readonly Func<RestartTestStage, IContext<int>, ISyncDirective> _onPull;

            public RestartTestStage(Func<RestartTestStage, int, IContext<int>, ISyncDirective> onPush = null, Func<RestartTestStage, IContext<int>, ISyncDirective> onPull = null)
            {
                _onPush = onPush;
                _onPull = onPull;
            }

            public override ISyncDirective OnPush(int element, IContext<int> context)
            {
                var result = _onPush?.Invoke(this, element, context);
                if (result != null)
                    return result;
                Sum += element;
                return context.Push(Sum);
            }

            public override ISyncDirective OnPull(IContext<int> context)
            {
                var result = _onPull?.Invoke(this, context);
                if (result != null)
                    return result;
                return context.Pull();
            }

            public override Directive Decide(Exception cause)
            {
                return Directive.Restart;
            }

            public override IStage<int, int> Restart()
            {
                Sum = 0;
                return this;
            }
        }

        private IEnumerator<int> ContinuallyThrow()
        {
            Func<int> thrower = () => { throw TE(); };
            yield return thrower();
        }

        public class OneToManyTestStage : PushPullStage<int, int>
        {
            private readonly Decider _decider;
            private readonly bool _absorbTermination;
            private Queue<int> _buffer;

            public OneToManyTestStage(Decider decider, bool absorbTermination)
            {
                _decider = decider;
                _absorbTermination = absorbTermination;
                _buffer = new Queue<int>();
            }

            public override ISyncDirective OnPush(int element, IContext<int> context)
            {
                _buffer = new Queue<int>(new [] {element + 1, element + 2, element + 3});
                return context.Push(element);
            }

            public override ISyncDirective OnPull(IContext<int> context)
            {
                if (_buffer.Count == 0 && context.IsFinishing)
                    return context.Finish();
                if (_buffer.Count == 0)
                    return context.Pull();
                var element = _buffer.Dequeue();
                if (element == 3) throw new TestException("TEST");
                return context.Push(element);
            }

            public override ITerminationDirective OnUpstreamFinish(IContext<int> context)
            {
                return _absorbTermination ? context.AbsorbTermination() : context.Finish();
            }

            // note that resume will be turned into failure in the Interpreter if exception is thrown from OnPull
            public override Directive Decide(Exception cause)
            {
                return _decider(cause);
            }

            public override IStage<int, int> Restart()
            {
                return new OneToManyTestStage(_decider, _absorbTermination);
            }
        }
    }
}