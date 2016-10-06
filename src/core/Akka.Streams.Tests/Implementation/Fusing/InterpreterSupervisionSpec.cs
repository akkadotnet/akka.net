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
                new Grouped<int>(3)
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
                new Grouped<int>(1000)
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

        private Exception TE()
        {
            return new TestException("TEST");
        }

        private IEnumerator<int> ContinuallyThrow()
        {
            Func<int> thrower = () => { throw TE(); };
            yield return thrower();
        }
    }
}