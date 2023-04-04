//-----------------------------------------------------------------------
// <copyright file="SinkForeachAsyncSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using Nito.AsyncEx;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SinkForeachAsyncSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SinkForeachAsyncSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_ForeachAsync_must_handle_empty_source()
        {
            var p = Source.From(new List<int>()).RunWith(Sink.ForEachAsync<int>(3, _ => Task.CompletedTask), Materializer);
            (await p.ShouldCompleteWithin(RemainingOrDefault)).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_be_able_to_run_elements_in_parallel()
        {
            var probe = CreateTestProbe();
            var latch = Enumerable.Range(1, 4)
                .Select(i => (i, new TestLatch(1)))
                .ToDictionary(t => t.i, t => t.Item2);

            var sink = Sink.ForEachAsync<int>(4, n =>
            {
                latch[n].Ready(RemainingOrDefault);
                probe.Ref.Tell(n);
                return Task.CompletedTask;
            });

            var p = Source.From(Enumerable.Range(1, 4)).RunWith(sink, Materializer);

            latch[1].CountDown();
            probe.ExpectMsg(1);
            latch[2].CountDown();
            probe.ExpectMsg(2);
            latch[3].CountDown();
            probe.ExpectMsg(3);
            latch[4].CountDown();
            probe.ExpectMsg(4);

            (await p.ShouldCompleteWithin(TimeSpan.FromSeconds(4))).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_back_pressure_upstream_elements_when_downstream_is_slow()
        {
            var probe = CreateTestProbe();
            var latch = Enumerable.Range(1, 4)
                .Select(i => (i, new TestLatch(1)))
                .ToDictionary(t => t.i, t => t.Item2);

            var sink = Sink.ForEachAsync<Func<int>>(1, async n =>
            {
                latch[n()].Ready(RemainingOrDefault);
                probe.Ref.Tell(n());
                await Task.Delay(2000);
            });

            var oneCalled = false;
            var twoCalled = false;
            var threeCalled = false;
            var fourCalled = false;

            int One()
            {
                oneCalled = true;
                return 1;
            }

            int Two()
            {
                twoCalled = true;
                return 2;
            }

            int Three()
            {
                threeCalled = true;
                return 3;
            }

            int Four()
            {
                fourCalled = true;
                return 4;
            }

            var p = Source.From(new List<Func<int>> { One, Two, Three, Four }).RunWith(sink, Materializer);

            latch[1].CountDown();
            probe.ExpectMsg(1);

            twoCalled.ShouldBeFalse();
            threeCalled.ShouldBeFalse();
            fourCalled.ShouldBeFalse();

            probe.ExpectNoMsg(TimeSpan.FromSeconds(2));

            latch[2].CountDown();
            probe.ExpectMsg(2);

            threeCalled.ShouldBeFalse();
            fourCalled.ShouldBeFalse();

            probe.ExpectNoMsg(TimeSpan.FromSeconds(2));

            latch[3].CountDown();
            probe.ExpectMsg(3);

            fourCalled.ShouldBeFalse();

            probe.ExpectNoMsg(TimeSpan.FromSeconds(2));

            latch[4].CountDown();
            probe.ExpectMsg(4);

            (await p.ShouldCompleteWithin(TimeSpan.FromSeconds(4))).Should().Be(Done.Instance);

            oneCalled.ShouldBeTrue();
            twoCalled.ShouldBeTrue();
            threeCalled.ShouldBeTrue();
            fourCalled.ShouldBeTrue();
        }

        [Fact]
        public async Task A_ForeachAsync_must_produce_elements_in_the_order_they_are_ready()
        {
            var probe = CreateTestProbe();
            var latch = Enumerable.Range(1, 4)
                .Select(i => (i, new AsyncCountdownEvent(1)))
                .ToDictionary(t => t.i, t => t.Item2);
            var p = Source.From(Enumerable.Range(1, 4)).RunWith(Sink.ForEachAsync<int>(4, async n =>
            {
                await latch[n].WaitAsync().ShouldCompleteWithin(TimeSpan.FromSeconds(5));
                probe.Ref.Tell(n);
            }), Materializer);

            latch[2].Signal();
            probe.ExpectMsg(2);
            latch[4].Signal();
            probe.ExpectMsg(4);
            latch[3].Signal();
            probe.ExpectMsg(3);

            p.IsCompleted.ShouldBeFalse();

            latch[1].Signal();
            probe.ExpectMsg(1);

            (await p.ShouldCompleteWithin(TimeSpan.FromSeconds(4))).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_not_run_more_functions_in_parallel_then_specified()
        {
            var probe = CreateTestProbe();
            var latch = Enumerable.Range(1, 5)
                .Select(i => (i, new AsyncCountdownEvent(1)))
                .ToDictionary(t => t.i, t => t.Item2);
            var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachAsync<int>(4, async n =>
            {
                probe.Ref.Tell(n);
                await latch[n].WaitAsync().ShouldCompleteWithin(TimeSpan.FromSeconds(5));
            }), Materializer);

            probe.ExpectMsgAllOf(new[] { 1, 2, 3, 4 });
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

            p.IsCompleted.Should().BeFalse();

            Enumerable.Range(1, 4).ForEach(i => latch[i].Signal());

            latch[5].Signal();
            probe.ExpectMsg(5);

            (await p.ShouldCompleteWithin(TimeSpan.FromSeconds(5))).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_resume_after_function_failure()
        {
            var probe = CreateTestProbe();
            var latch = new AsyncCountdownEvent(1);

            var p = Source.From(Enumerable.Range(1, 5)).RunWith(Sink.ForEachAsync<int>(4, async n =>
            {
                if (n == 3)
                    throw new TestException("err1");

                probe.Ref.Tell(n);
                await latch.WaitAsync().ShouldCompleteWithin(TimeSpan.FromSeconds(10));
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider)), Materializer);

            latch.Signal();
            probe.ExpectMsgAllOf(new[] { 1, 2, 4, 5 });

            (await p.ShouldCompleteWithin(TimeSpan.FromSeconds(5))).Should().Be(Done.Instance);
        }

        [Fact]
        public void A_ForeachAsync_must_finish_after_function_failure()
        {
            var probe = CreateTestProbe();
            var element4Latch = new AsyncCountdownEvent(1);
            var errorLatch = new AsyncCountdownEvent(2);

            var p = Source.From(Enumerable.Range(1, int.MaxValue)).RunWith(Sink.ForEachAsync<int>(3, async n =>
            {
                if (n == 3)
                {
                    // Error will happen only after elements 1, 2 has been processed
                    await errorLatch.WaitAsync().ShouldCompleteWithin(TimeSpan.FromSeconds(5));
                    throw new TestException("err2");
                }

                probe.Ref.Tell(n);
                errorLatch.Signal();
                await element4Latch.WaitAsync().ShouldCompleteWithin(TimeSpan.FromSeconds(5)); // Block element 4, 5, 6, ... from entering
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider)), Materializer);

            // Only the first two messages are guaranteed to arrive due to their enforced ordering related to the time
            // of failure.
            probe.ExpectMsgAllOf(new[] { 1, 2 });
            element4Latch.Signal(); // Release elements 4, 5, 6, ...

            var ex = p.Invoking(t => t.Wait(TimeSpan.FromSeconds(1))).Should().Throw<AggregateException>().Which;
            ex.Flatten().InnerException.Should().BeOfType<TestException>();
            ex.Flatten().InnerException.Message.Should().Be("err2");
        }
    }
}
