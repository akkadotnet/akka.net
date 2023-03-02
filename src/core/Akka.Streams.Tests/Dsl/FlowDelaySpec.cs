//-----------------------------------------------------------------------
// <copyright file="FlowDelaySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
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
        private const long Epsilon = 100;
        private ActorMaterializer Materializer { get; }

        public FlowDelaySpec(ITestOutputHelper helper) : base("{akka.loglevel = INFO}", helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_deliver_elements_with_some_time_shift()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 10))
                    .Delay(TimeSpan.FromSeconds(1))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.RequestAsync(10);
                var elapsed = await MeasureExecutionTime(() => probe.ExpectNextNAsync(Enumerable.Range(1, 10), 3.Seconds()));
                Log.Info("Expected execution time: 1000 ms, actual: {0} ms", elapsed);
                elapsed.Should().BeGreaterThan(1000 - Epsilon);
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_add_delay_to_initialDelay_if_exists_upstream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 10))
                    .InitialDelay(TimeSpan.FromSeconds(1))
                    .Delay(TimeSpan.FromSeconds(1))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.RequestAsync(10);
                var elapsed = await MeasureExecutionTime(() => probe.ExpectNextNAsync(Enumerable.Range(1, 10), 5.Seconds()));
                Log.Info("Expected execution time: 2000 ms, actual: {0} ms", elapsed);
                elapsed.Should().BeGreaterThan(2000 - Epsilon);
                await probe.ExpectCompleteAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_deliver_element_after_time_passed_from_actual_receiving_element()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                const int expectedMilliseconds = 300;
                
                var probe = Source.From(Enumerable.Range(1, 3))
                    .Delay(TimeSpan.FromMilliseconds(expectedMilliseconds))
                    .RunWith(this.SinkProbe<int>(), Materializer);
                
                await probe.RequestAsync(2);
                var elapsed = await MeasureExecutionTime(() => probe.ExpectNextNAsync(new[] { 1, 2 }, 1.Seconds()));
                Log.Info("Expected execution time: {0} ms, actual: {1} ms", expectedMilliseconds, elapsed);
                elapsed.Should().BeGreaterThan(200);
                await probe.ExpectNoMsgAsync(200.Milliseconds());
                
                await probe.RequestAsync(1);
                elapsed = await MeasureExecutionTime(() => probe.ExpectNextAsync(3, 1.Seconds())); // buffered element
                Log.Info("Expected execution time: instant, actual: {0} ms", elapsed);
                elapsed.Should().BeLessThan(300 + Epsilon);
                await probe.ExpectCompleteAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_deliver_elements_with_delay_for_slow_stream()
        {
            const int expectedMilliseconds = 300;
            
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var p = this.CreateManualPublisherProbe<int>();

                Source.FromPublisher(p)
                    .Delay(TimeSpan.FromMilliseconds(expectedMilliseconds))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = await c.ExpectSubscriptionAsync();
                var pSub = await p.ExpectSubscriptionAsync();
                
                cSub.Request(100);
                pSub.SendNext(1);
                var elapsed = await MeasureExecutionTime(() => c.ExpectNextAsync(1, 1.Seconds()));
                Log.Info("Expected execution time: {0} ms, actual: {1} ms", expectedMilliseconds, elapsed);
                elapsed.Should().BeGreaterThan(expectedMilliseconds - Epsilon);
                
                pSub.SendNext(2);
                elapsed = await MeasureExecutionTime(() => c.ExpectNextAsync(2, 1.Seconds()));
                Log.Info("Expected execution time: {0} ms, actual: {1} ms", expectedMilliseconds, elapsed);
                elapsed.Should().BeGreaterThan(expectedMilliseconds - Epsilon);
                
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

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_drop_tail_for_internal_buffer_if_it_is_full_in_DropTail_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropTail)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await Task.Delay(1.Seconds());
                await probe.RequestAsync(20);
                var result = await probe.ExpectNextNAsync(16).ToListAsync();
                var expected = Enumerable.Range(1, 15).ToList();
                expected.Add(20);
                result.Should().BeEquivalentTo(expected);
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_drop_head_for_internal_buffer_if_it_is_full_in_DropHead_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropHead)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await Task.Delay(1.Seconds());
                await probe.RequestAsync(20);
                var result = await probe.ExpectNextNAsync(16).ToListAsync();
                result.Should().BeEquivalentTo(Enumerable.Range(5, 16));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_clear_all_for_internal_buffer_if_it_is_full_in_DropBuffer_mode()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropBuffer)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.RequestAsync(20);
                var result = await probe.ExpectNextNAsync(4).ToListAsync();
                result.Should().BeEquivalentTo(Enumerable.Range(17, 4));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task A_Delay_must_pass_elements_with_delay_through_normally_in_backpressured_mode()
        {
            const int expectedMilliseconds = 300;
            
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(Enumerable.Range(1, 3))
                    .Delay(TimeSpan.FromMilliseconds(expectedMilliseconds), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(1, 1))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.RequestAsync(5);
                var elapsed1 = await MeasureExecutionTime(() => probe.ExpectNextAsync(1, 1.Seconds()));
                var elapsed2 = await MeasureExecutionTime(() => probe.ExpectNextAsync(2, 1.Seconds()));
                var elapsed3 = await MeasureExecutionTime(() => probe.ExpectNextAsync(3, 1.Seconds()));
                
                Log.Info("Expected execution time 1: {0} ms, actual: {1} ms", expectedMilliseconds, elapsed1);
                Log.Info("Expected execution time 2: {0} ms, actual: {1} ms", expectedMilliseconds, elapsed2);
                Log.Info("Expected execution time 3: {0} ms, actual: {1} ms", expectedMilliseconds, elapsed3);

                elapsed1.Should().BeGreaterThan(expectedMilliseconds - Epsilon);
                elapsed2.Should().BeGreaterThan(expectedMilliseconds - Epsilon);
                elapsed3.Should().BeGreaterThan(expectedMilliseconds - Epsilon);
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

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
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
                await c.ExpectNoMsgAsync(300.Milliseconds());
                pSub.SendNext(17);
                
                var elapsed = await MeasureExecutionTime(() => c.ExpectNextAsync(1, 1.Seconds()));
                Log.Info("Expected execution time: instant, actual: {0} ms", elapsed);
                elapsed.Should().BeLessThan(Epsilon);
                
                // fail will terminate despite of non empty internal buffer
                pSub.SendError(new Exception());
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
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

                var elapsed = await MeasureExecutionTime(async () => await ExpectMsgAsync<Done>(5.Seconds()));
                Log.Info("Expected execution time: 2500 ms, actual: {0} ms", elapsed);
                elapsed.Should().BeGreaterThan(2500 - Epsilon);

                // With a buffer large enough to hold all arriving elements, delays don't add up 
                // task is intentionally not awaited
                task = Source.From(Enumerable.Range(1, 100))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(100, 100))
                    .RunWith(Sink.Ignore<int>(), Materializer)
                    .PipeTo(TestActor, success: () => Done.Instance);

                elapsed = await MeasureExecutionTime(async () => await ExpectMsgAsync<Done>(5.Seconds()));
                Log.Info("Expected execution time: 1000 ms, actual: {0} ms", elapsed);
                elapsed.Should().BeLessThan(1000 + Epsilon);

                // Delays that are already present are preserved when buffer is large enough 
                // task is intentionally not awaited
                task = Source.Tick(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), NotUsed.Instance)
                    .Take(10)
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(10, 10))
                    .RunWith(Sink.Ignore<NotUsed>(), Materializer)
                    .PipeTo(TestActor, success: () => Done.Instance);

                elapsed = await MeasureExecutionTime(async () => await ExpectMsgAsync<Done>(5.Seconds()));
                Log.Info("Expected execution time: 1000 ms, actual: {0} ms", elapsed);
                elapsed.Should().BeGreaterThan(1000 - Epsilon);
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

