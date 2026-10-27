//-----------------------------------------------------------------------
// <copyright file="SinkForeachAsyncSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            (await p.WaitAsync(RemainingOrDefault)).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_be_able_to_run_elements_in_parallel()
        {
            var started = CreateTestProbe();
            var probe = CreateTestProbe();
            var gates = Enumerable.Range(1, 4)
                .ToDictionary(i => i, _ => new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously));

            var sink = Sink.ForEachAsync<int>(4, async n =>
            {
                started.Ref.Tell(n);
                await gates[n].Task;
                probe.Ref.Tell(n);
            });

            var p = Source.From(Enumerable.Range(1, 4)).RunWith(sink, Materializer);

            await started.ExpectMsgAllOfAsync(new[] { 1, 2, 3, 4 }).ToListAsync();

            foreach (var n in new[] { 4, 3, 2, 1 })
            {
                gates[n].SetResult(Done.Instance);
                await probe.ExpectMsgAsync(n);
            }

            (await p.WaitAsync(RemainingOrDefault)).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_back_pressure_upstream_elements_when_downstream_is_slow()
        {
            var probe = CreateTestProbe();
            var gates = Enumerable.Range(1, 4)
                .ToDictionary(i => i, _ => new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously));

            // The callback must not block before its first await: it runs on the stream actor's thread.
            var sink = Sink.ForEachAsync<Func<int>>(1, async n =>
            {
                var i = n();
                probe.Ref.Tell(i);
                await gates[i].Task;
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

            // Each element stays in flight until its gate opens, so the next one can not start before then.
            await probe.ExpectMsgAsync(1);

            twoCalled.ShouldBeFalse();
            threeCalled.ShouldBeFalse();
            fourCalled.ShouldBeFalse();

            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            gates[1].SetResult(Done.Instance);
            await probe.ExpectMsgAsync(2);

            threeCalled.ShouldBeFalse();
            fourCalled.ShouldBeFalse();

            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            gates[2].SetResult(Done.Instance);
            await probe.ExpectMsgAsync(3);

            fourCalled.ShouldBeFalse();

            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            gates[3].SetResult(Done.Instance);
            await probe.ExpectMsgAsync(4);

            gates[4].SetResult(Done.Instance);
            (await p.WaitAsync(RemainingOrDefault)).Should().Be(Done.Instance);

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
                await latch[n].WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                probe.Ref.Tell(n);
            }), Materializer);

            latch[2].Signal();
            await probe.ExpectMsgAsync(2);
            latch[4].Signal();
            await probe.ExpectMsgAsync(4);
            latch[3].Signal();
            await probe.ExpectMsgAsync(3);

            p.IsCompleted.ShouldBeFalse();

            latch[1].Signal();
            await probe.ExpectMsgAsync(1);

            (await p.WaitAsync(RemainingOrDefault)).Should().Be(Done.Instance);
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
                await latch[n].WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }), Materializer);

            await probe.ExpectMsgAllOfAsync(new[] { 1, 2, 3, 4 }).ToListAsync();
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            p.IsCompleted.Should().BeFalse();

            Enumerable.Range(1, 4).ForEach(i => latch[i].Signal());

            latch[5].Signal();
            await probe.ExpectMsgAsync(5);

            (await p.WaitAsync(RemainingOrDefault)).Should().Be(Done.Instance);
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
                await latch.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider)), Materializer);

            latch.Signal();
            await probe.ExpectMsgAllOfAsync(new[] { 1, 2, 4, 5 }).ToListAsync();

            (await p.WaitAsync(RemainingOrDefault)).Should().Be(Done.Instance);
        }

        [Fact]
        public async Task A_ForeachAsync_must_finish_after_function_failure()
        {
            var probe = CreateTestProbe();
            var element4Latch = new AsyncCountdownEvent(1);
            var errorLatch = new AsyncCountdownEvent(2);

            var p = Source.From(Enumerable.Range(1, int.MaxValue)).RunWith(Sink.ForEachAsync<int>(3, async n =>
            {
                if (n == 3)
                {
                    // Error will happen only after elements 1, 2 has been processed
                    await errorLatch.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    throw new TestException("err2");
                }

                probe.Ref.Tell(n);
                errorLatch.Signal();
                await element4Latch.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)); // Block element 4, 5, 6, ... from entering
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.StoppingDecider)), Materializer);

            // Only the first two messages are guaranteed to arrive due to their enforced ordering related to the time
            // of failure.
            await probe.ExpectMsgAllOfAsync(new[] { 1, 2 }).ToListAsync();
            element4Latch.Signal(); // Release elements 4, 5, 6, ...

            var ex = await p.Awaiting(t => t.WaitAsync(RemainingOrDefault)).Should().ThrowAsync<TestException>();
            ex.Which.Message.Should().Be("err2");
        }
    }
}
