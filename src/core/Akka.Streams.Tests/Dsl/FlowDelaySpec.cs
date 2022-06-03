//-----------------------------------------------------------------------
// <copyright file="FlowDelaySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Tests.Shared.Internals;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    [Collection(nameof(FlowDelaySpec))] // timing sensitive since it involves hard delays
    public class FlowDelaySpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDelaySpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_deliver_elements_with_some_time_shift()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task =
                    Source.From(Enumerable.Range(1, 10))
                        .Delay(TimeSpan.FromSeconds(1))
                        .Grouped(100)
                        .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                await task.ShouldCompleteWithin(1200.Milliseconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_add_delay_to_initialDelay_if_exists_upstream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 10))
                    .InitialDelay(TimeSpan.FromSeconds(1))
                    .Delay(TimeSpan.FromSeconds(1))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.AsyncBuilder()
                    .Request(10)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(1800))
                    .ExpectNext(1, TimeSpan.FromMilliseconds(600))
                    .ExpectNextN(Enumerable.Range(2, 9))
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_deliver_element_after_time_passed_from_actual_receiving_element()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 3))
                    .Delay(TimeSpan.FromMilliseconds(300))
                    .RunWith(this.SinkProbe<int>(), Materializer);
            
                await probe.AsyncBuilder()
                    .Request(2)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200)) //delay
                    .ExpectNext(1, TimeSpan.FromMilliseconds(200)) //delayed element
                    .ExpectNext(2, TimeSpan.FromMilliseconds(100)) //buffered element
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExecuteAsync();
            
                await probe.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(3) //buffered element
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        // Was marked as racy before async testkit, test rewritten
        [Fact]
        public async Task A_Delay_must_deliver_elements_with_delay_for_slow_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var p = this.CreateManualPublisherProbe<int>();

                Source.FromPublisher(p)
                    .Delay(TimeSpan.FromMilliseconds(300))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = await c.ExpectSubscriptionAsync();
                var pSub = await p.ExpectSubscriptionAsync();
                
                cSub.Request(100);
                pSub.SendNext(1);
                var elapsed = await MeasureExecutionTime(() => c.ExpectNextAsync(1))
                    .ShouldCompleteWithin(1.Seconds());
                elapsed.Should().BeGreaterThan(200);
                
                pSub.SendNext(2);
                elapsed = await MeasureExecutionTime(() => c.ExpectNextAsync(2))
                    .ShouldCompleteWithin(1.Seconds());
                elapsed.Should().BeGreaterThan(200);
                
                pSub.SendComplete();
                await c.ExpectCompleteAsync();
            }, Materializer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task<long> MeasureExecutionTime(Func<Task> task)
        {
            var stopwatch = Stopwatch.StartNew();
            await task();
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        // Was marked as racy before async testkit, test rewritten
        [Fact]
        public async Task A_Delay_must_drop_tail_for_internal_buffer_if_it_is_full_in_DropTail_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropTail)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await Task.Delay(1.Seconds());
                await probe.ExpectSubscriptionAsync();
                await probe.RequestAsync(20);
                var result = await probe.ExpectNextNAsync(16).ToListAsync();
                var expected = Enumerable.Range(1, 15).ToList();
                expected.Add(20);
                result.Should().BeEquivalentTo(expected);
            }, Materializer);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_drop_head_for_internal_buffer_if_it_is_full_in_DropHead_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropHead)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await task.ShouldCompleteWithin(1800.Milliseconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(5, 16));
            }, Materializer);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_clear_all_for_internal_buffer_if_it_is_full_in_DropBuffer_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropBuffer)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await task.ShouldCompleteWithin(1200.Milliseconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(17, 4));
            }, Materializer);
        }

        [Fact(Skip = "Extremely flaky because of the interleaved ExpectNext and ExpectNoMsg with a very tight timing requirement. .Net timer implementation is not consistent enough to maintain accurate timing under heavy CPU load.")]
        public async Task A_Delay_must_pass_elements_with_delay_through_normally_in_backpressured_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                await Source.From(Enumerable.Range(1, 3))
                    .Delay(TimeSpan.FromMilliseconds(300), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(1, 1))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(5)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExpectNext(1, TimeSpan.FromMilliseconds(200))
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExpectNext(2, TimeSpan.FromMilliseconds(200))
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExpectNext(3, TimeSpan.FromMilliseconds(200))
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Delay_must_fail_on_overflow_in_Fail_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var actualError = await Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromMilliseconds(300), DelayOverflowStrategy.Fail)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .AsyncBuilder()
                    .Request(100)
                    .ExpectErrorAsync();

                actualError.Should().BeOfType<BufferOverflowException>();
                actualError.Message.Should().Be("Buffer overflow for Delay combinator (max capacity was: 16)!");
            }, Materializer);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_emit_early_when_buffer_is_full_and_in_EmitEarly_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var p = this.CreateManualPublisherProbe<int>();

                Source.FromPublisher(p)
                    .Delay(TimeSpan.FromSeconds(10), DelayOverflowStrategy.EmitEarly)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = await c.ExpectSubscriptionAsync();
                var pSub = await p.ExpectSubscriptionAsync();
                cSub.Request(20);

                Enumerable.Range(1, 16).ForEach(i => pSub.SendNext(i));
                
                await c.AsyncBuilder()
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(300))
                    .ExecuteAsync();
                pSub.SendNext(17);
                await c.AsyncBuilder()
                    .ExpectNext(1, TimeSpan.FromMilliseconds(100))
                    .ExecuteAsync();
                // fail will terminate despite of non empty internal buffer
                pSub.SendError(new Exception());
            }, Materializer);
        }

        // Was marked as racy before async testkit
        [Fact]
        public async Task A_Delay_must_properly_delay_according_to_buffer_size()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                // With a buffer size of 1, delays add up
                // task is intentionally not awaited
                var task = Source.From(Enumerable.Range(1, 5))
                    .Delay(TimeSpan.FromMilliseconds(500), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(1, 1))
                    .RunWith(Sink.Ignore<int>(), Materializer)
                    .PipeTo(TestActor, success: () => Done.Instance);

                await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
                await ExpectMsgAsync<Done>();
                task.IsCompleted.Should().BeTrue();

                // With a buffer large enough to hold all arriving elements, delays don't add up 
                // task is intentionally not awaited
                task = Source.From(Enumerable.Range(1, 100))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(100, 100))
                    .RunWith(Sink.Ignore<int>(), Materializer)
                    .PipeTo(TestActor, success: () => Done.Instance);

                await ExpectMsgAsync<Done>();
                task.IsCompleted.Should().BeTrue();

                // Delays that are already present are preserved when buffer is large enough 
                // task is intentionally not awaited
                task = Source.Tick(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), NotUsed.Instance)
                    .Take(10)
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(10, 10))
                    .RunWith(Sink.Ignore<NotUsed>(), Materializer)
                    .PipeTo(TestActor, success: () => Done.Instance);

                await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(900));
                await ExpectMsgAsync<Done>();
                task.IsCompleted.Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public async Task A_Delay_must_not_overflow_buffer_when_DelayOverflowStrategy_is_Backpressure()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 6))
                    .Delay(TimeSpan.FromMilliseconds(100), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(2, 2))
                    .Throttle(1, TimeSpan.FromMilliseconds(200), 1, ThrottleMode.Shaping)
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.AsyncBuilder()
                    .Request(10)
                    .ExpectNext(1, 2, 3, 4, 5, 6)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
            
        }
    }
}

