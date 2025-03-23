//-----------------------------------------------------------------------
// <copyright file="FlowSelectAsyncSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.Tests.TestHelpers;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.Xunit2.Attributes;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Akka.TestKit.Extensions;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions.Extensions;
using static FluentAssertions.FluentActions;
using Directive = Akka.Streams.Supervision.Directive;

// ReSharper disable InvokeAsExtensionMethod
#pragma warning disable 162

namespace Akka.Streams.Tests.Dsl
{
    [Collection(nameof(FlowSelectAsyncSpec))]
    public class FlowSelectAsyncSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSelectAsyncSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_produce_task_elements()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 3))
                    .SelectAsync(4, Task.FromResult)
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = await c.ExpectSubscriptionAsync();

                sub.Request(2);
                await c.ExpectNext(1)
                    .ExpectNext(2)
                    .ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                sub.Request(2);

                await c.ExpectNext(3)
                    .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_produce_task_elements_in_order()
        {
            var c = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Range(1, 50))
                .SelectAsync(4, i =>
                {
                    if (i%3 == 0)
                        return Task.FromResult(i);

                    return Task.Run(() =>
                    {
                        Thread.Sleep(ThreadLocalRandom.Current.Next(1, 10));
                        return i;
                    });
                })
                .RunWith(Sink.FromSubscriber(c), Materializer);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(1000);
            foreach (var n in Enumerable.Range(1, 50))
                await c.ExpectNextAsync(n);
           
            await c.ExpectCompleteAsync();
        }

        // Turning this on in CI/CD for now
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_not_run_more_futures_than_requested_parallelism()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = CreateTestProbe();
                var c = this.CreateManualSubscriberProbe<int>();
                
                Source.From(Enumerable.Range(1, 20))
                    .SelectAsync(8, async n =>  
                    {
                        await Task.Yield();
                        probe.Ref.Tell(n);
                        return n;
                    })
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                sub.Request(1);
                (await probe.ReceiveNAsync(9).ToListAsync()).Should().BeEquivalentTo(Enumerable.Range(1, 9));
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                sub.Request(2);
                (await probe.ReceiveNAsync(2).ToListAsync()).Should().BeEquivalentTo(Enumerable.Range(10, 2));
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                sub.Request(10);
                (await probe.ReceiveNAsync(9).ToListAsync()).Should().BeEquivalentTo(Enumerable.Range(12, 9));
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                await c.ExpectNextNAsync(Enumerable.Range(1, 13));
            
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            }, Materializer).ShouldCompleteWithin(RemainingOrDefault);
        }

        // Turning this on in CI/CD for now
        [Fact]
        public async Task A_Flow_with_parallel_execution_SelectAsync_must_signal_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c = this.CreateManualSubscriberProbe<int>();
                
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(4, async n =>
                    {
                        if (n == 4)
                            throw new TestException("err1");
                        await Task.Delay(10.Seconds());

                        return n;
                    })
                    .To(Sink.FromSubscriber(c)).Run(Materializer);
                
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(10);

                var exception = await c.ExpectErrorAsync();
                exception.InnerException!.Message.Should().Be("err1");
            }, Materializer).ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_signal_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(1, async n =>
                    {
                        await Task.Delay(10);
                        if (n == 3)
                            throw new TestException("err1");

                        return n;
                    })
                    .RunWith(this.SinkProbe<int>(), Materializer);
                
                var exception = await probe.AsyncBuilder()
                    .Request(10)
                    .ExpectNextN([1, 2])
                    .ExpectErrorAsync()
                    .ShouldCompleteWithin(RemainingOrDefault);
                exception.Message.Should().Be("err1");
            }, Materializer);
        }
        
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_signal_task_failure_asap()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var latch = CreateTestLatch();
                var done = Source.From(Enumerable.Range(1, 5))
                    .Select(n =>
                    {
                        if (n != 1)
                            // slow upstream should not block the error
                            latch.Ready(TimeSpan.FromSeconds(10));

                        return n;
                    })
                    .SelectAsync(4, n =>
                    {
                        if (n == 1)
                        {
                            var c = new TaskCompletionSource<int>();
                            c.SetException(new Exception("err1"));
                            return c.Task;
                        }
                        return Task.FromResult(n);
                    }).RunWith(Sink.Ignore<int>(), Materializer);

                await Awaiting(async () => await done).Should()
                    .ThrowAsync<Exception>()
                    .WithMessage("err1")
                    .ShouldCompleteWithin(RemainingOrDefault);
                
                latch.CountDown();
            }, Materializer);
        }
        
         [Fact(DisplayName = "A Flow with SelectAsync that failed mid-stream MUST cause a failure ASAP (stopping strategy)")]
        public async Task A_Flow_with_SelectAsync_must_signal_error_from_SelectAsync_MidStream_Stop()
        {
            var tsa = new TaskCompletionSource<string>();
            var tsb = new TaskCompletionSource<string>();
            var tsc = new TaskCompletionSource<string>();
            var tsd = new TaskCompletionSource<string>();
            var tse = new TaskCompletionSource<string>();
            var tsf = new TaskCompletionSource<string>();

            var input = new []{ tsa, tsb, tsc, tsd, tse , tsf };

            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var probe = Source.From(input)
                    .SelectAsync(5, n => n.Task)
                    .RunWith(this.SinkProbe<string>(), Materializer);

                probe.Request(100);

                // placing the future completion signals here is important
                // the ordering is meant to expose a race between the failure at C and subsequent elements
                tsa.SetResult("A");
                tsb.SetResult("B");
                tsc.SetException(new TestException("Boom at C"));
                tsd.SetResult("D");
                tse.SetResult("E");
                tsf.SetResult("F");

                switch (await probe.ExpectNextOrErrorAsync())
                {
                    case Exception ex:
                        ex.Should().BeOfType<AggregateException>()
                            .Which.InnerException.Should().BeOfType<TestException>()
                            .Which.Message.Should().Be("Boom at C");  // fine, error can over-take elements
                        return;
                    case "A":
                        switch (await probe.ExpectNextOrErrorAsync())
                        {
                            case Exception ex:
                                ex.Should().BeOfType<AggregateException>()
                                    .Which.InnerException.Should().BeOfType<TestException>()
                                    .Which.Message.Should().Be("Boom at C");  // fine, error can over-take elements
                                return;
                            case "B":
                                switch (await probe.ExpectNextOrErrorAsync())
                                {
                                    case Exception ex:
                                        ex.Should().BeOfType<AggregateException>()
                                            .Which.InnerException.Should().BeOfType<TestException>()
                                            .Which.Message.Should().Be("Boom at C");  // fine
                                        return;
                                    case string s:
                                        Assert.Fail($"Got [{s}] yet it caused an exception, should not have happened!");
                                        return;
                                }
                                return;
                            case var unexpected:
                                Assert.Fail($"Unexpected {unexpected}");
                                return;
                        }
                    case var unexpected:
                        Assert.Fail($"Unexpected {unexpected}");
                        return;
                }
            }, Materializer);
        }

        [Fact(DisplayName = "A Flow with SelectAsync that failed mid-stream MUST skip element (resume strategy)")]
        public async Task A_Flow_with_SelectAsync_must_signal_error_from_SelectAsync_MidStream_Result()
        {
            var tsa = new TaskCompletionSource<string>();
            var tsb = new TaskCompletionSource<string>();
            var tsc = new TaskCompletionSource<string>();
            var tsd = new TaskCompletionSource<string>();
            var tse = new TaskCompletionSource<string>();
            var tsf = new TaskCompletionSource<string>();

            var input = new[] { tsa, tsb, tsc, tsd, tse, tsf };

            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(input)
                    .SelectAsync(5, n => n.Task)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.Seq<string>(), Materializer);

                // the problematic ordering:
                tsa.SetResult("A");
                tsb.SetResult("B");
                tsd.SetResult("D");
                tse.SetResult("E");
                tsf.SetResult("F");
                tsc.SetException(new TestException("Boom at C"));

                var elements = await task;
                elements.Should().BeEquivalentTo(new[] { "A", "B", "D", "E", "F" },
                    options => options.WithStrictOrdering());
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_signal_error_from_SelectAsync()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(4, n =>
                    {
                        if (n == 3)
                            throw new TestException("err2");

                        return Task.Run(async () =>
                        {
                            await Task.Delay(10.Seconds());
                            return n;
                        });
                    })
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(10);
                (await c.ExpectErrorAsync()).Message.Should().Be("err2");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_invoke_supervision_strategy_on_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var invoked = false;
                var probe = Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(1, n => Task.Run(() =>
                    {
                        if (n == 3)
                            throw new TestException("err3");
                        return n;
                    }))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(_ =>
                    {
                        invoked = true;
                        return Directive.Stop;
                    }))
                    .RunWith(this.SinkProbe<int>(), Materializer);

                await probe.AsyncBuilder()
                    .Request(10)
                    .ExpectNextN([1, 2])
                    .ExpectErrorAsync();

                invoked.Should().BeTrue();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_after_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(4, n => Task.Run(() =>
                    {
                        if (n == 3)
                            throw new TestException("err3");
                        return n;
                    }))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(10);
                foreach (var i in new[] { 1, 2, 4, 5 })
                    await c.ExpectNextAsync(i);
                await c.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_when_task_already_failed()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(4, n => 
                    {
                        var tcs = new TaskCompletionSource<int>();
                        if (n == 3)
                            tcs.TrySetException(new TestException("err3"));
                        else
                            tcs.TrySetResult(n);
                        return tcs.Task;
                    })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(10);
                foreach (var i in new[] { 1, 2, 4, 5 })
                    await c.ExpectNextAsync(i);
                await c.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_after_multiple_failures()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var futures = new[]
                {
                    Task.Run(() => { throw new TestException("failure1"); return "";}),
                    Task.Run(() => { throw new TestException("failure2"); return "";}),
                    Task.Run(() => { throw new TestException("failure3"); return "";}),
                    Task.Run(() => { throw new TestException("failure4"); return "";}),
                    Task.Run(() => { throw new TestException("failure5"); return "";}),
                    Task.FromResult("happy")
                };

                var t = Source.From(futures)
                    .SelectAsync(2, x => x)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.First<string>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().Be("happy");
                return Task.FromResult(Task.CompletedTask);
            }, Materializer);
        }

        [Fact(DisplayName = "A Flow with SelectAsync must complete without requiring further demand (parallelism = 1)")]
        public async Task CompleteWithoutDemand()
        {
            var probe = Source.Single(1)
                .SelectAsync(1, v => Task.Run(async () =>
                {
                    await Task.Delay(20);
                    return v;
                }))
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(1);
            await probe.ExpectNextAsync(1);
            await probe.ExpectCompleteAsync();
        }

        [Fact(DisplayName = "A Flow with SelectAsync must complete without requiring further demand with completed task (parallelism = 1)")]
        public async Task CompleteWithoutDemandCompletedTask()
        {
            var probe = Source.Single(1)
                .SelectAsync(1, Task.FromResult)
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(1);
            await probe.ExpectNextAsync(1);
            await probe.ExpectCompleteAsync();
        }

        [Fact(DisplayName = "A Flow with SelectAsync must complete without requiring further demand (parallelism = 2)")]
        public async Task CompleteWithoutDemandP2()
        {
            var probe = Source.From(new[] { 1, 2 })
                .SelectAsync(2, v => Task.Run(async () =>
                {
                    await Task.Delay(20);
                    return v;
                }))
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(2);
            await probe.ExpectNextNAsync(2).ToListAsync();
            await probe.ExpectCompleteAsync();
        }

        [Fact(DisplayName = "A Flow with SelectAsync must complete without requiring further demand with completed task (parallelism = 2)")]
        public async Task CompleteWithoutDemandCompletedTaskP2()
        {
            var probe = Source.From(new[] { 1, 2 })
                .SelectAsync(2, Task.FromResult)
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(2);
            await probe.ExpectNextNAsync(2).ToListAsync();
            await probe.ExpectCompleteAsync();
        }
        
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_finish_after_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var t = Source.From(Enumerable.Range(1, 3))
                    .SelectAsync(1, n => Task.Run(() =>
                    {
                        if (n == 3)
                            throw new TestException("err3b");
                        return n;
                    }))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .Grouped(10)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                
                var complete = await t.ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(new[] { 1, 2 });
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_after_task_cancels()
        {
            var c = this.CreateManualSubscriberProbe<int>();
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsync(4, async n =>
                    {
                        await MaybeCancels(n);
                        return n;
                    })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(10);
                foreach (var i in new[] { 1, 2, 4, 5 })
                    await c.ExpectNextAsync(i);
                await c.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_when_SelectAsync_throws()
        {
            var c = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Range(1, 5))
                .SelectAsync(4, n =>
                {
                    if (n == 3)
                        throw new TestException("err4");
                    return Task.FromResult(n);
                })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.FromSubscriber(c), Materializer);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            foreach (var i in new[] { 1, 2, 4, 5 })
                await c.ExpectNextAsync(i);
            await c.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_restart_after_task_throws()
        {
            var c = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Range(1, 5))
                .Select(n => n)
                .SelectAsync(4, n => Task.Run(async () =>
                {
                    await Task.Yield();
                    if(n == 3)
                        throw new TestException("err3");
                    return n;
                }))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.FromSubscriber(c), Materializer);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            foreach (var i in new[] { 1, 2, 4, 5})
                await c.ExpectNextAsync(i);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_restart_when_SelectAsync_task_cancelled()
        {
            var c = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Range(1, 5))
                .Select(n => n)
                .SelectAsync(4, async n =>
                {
                    await MaybeCancels(n);
                    return n;
                })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(Sink.FromSubscriber(c), Materializer);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            foreach (var i in new[] { 1, 2, 4, 5})
                await c.ExpectNextAsync(i);
        }

        private static Task<int> MaybeCancels(int n)
        {
            var tcs = new TaskCompletionSource<int>();
            Task.Run(async () =>
            {
                await Task.Yield();
                if (n == 3)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(n);
            });
            return tcs.Task;
        }
        
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_signal_NPE_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();

            Source.From(["a", "b"])
                .SelectAsync(4, _ => Task.FromResult<string>(null))
                .To(Sink.FromSubscriber(c)).Run(Materializer);

            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            (await c.ExpectErrorAsync()).Message.Should().StartWith(ReactiveStreamsCompliance.ElementMustNotBeNullMsg);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.From(["a", "b", "c"])
                .SelectAsync(4, s => s.Equals("b") ? Task.FromResult<string>(null) : Task.FromResult(s))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .To(Sink.FromSubscriber(c)).Run(Materializer);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            await c.ExpectNextAsync("a");
            await c.ExpectNextAsync("c");
            await c.ExpectCompleteAsync();
        }

        [Fact(DisplayName = "A Flow with SelectAsync must continue emitting after a sequence of nulls")]
        public async Task SelectAsyncNullSequence()
        {
            var flow = Flow.Create<int>()
                .SelectAsync(3, v => v is 0 or >= 100 
                    ? Task.FromResult(v.ToString()) 
                    : Task.FromResult<string>(null));

            var task = Source.From(Enumerable.Range(0, 103))
                .Via(flow)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Seq<string>(), Materializer);

            var result = await task;
            result.Should().BeEquivalentTo(new[]{"0", "100", "101", "102"}, o => o.WithStrictOrdering());
        }

        [Fact(DisplayName = "A Flow with SelectAsync must complete without emitting any elements after a sequence of nulls only")]
        public async Task SelectAsyncAllNullSequence()
        {
            var flow = Flow.Create<int>()
                .SelectAsync(3, _ => Task.FromResult<string>(null));

            var task = Source.From(Enumerable.Range(0, 10))
                .Via(flow)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Seq<string>(), Materializer);

            var result = await task;
            result.Should().BeEmpty();
        }

        [Fact(DisplayName = "A Flow with SelectAsync must complete if future task returning null completed last")]
        public async Task SelectAsyncNullLast()
        {
            var ts1 = new TaskCompletionSource<string>();
            var ts2 = new TaskCompletionSource<string>();
            var ts3 = new TaskCompletionSource<string>();
            var taskSources = new[] { ts1, ts2, ts3 };

            var task = Source.From(taskSources)
                .SelectAsync(2, t => t.Task)
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(Sink.Seq<string>(), Materializer);

            ts1.TrySetResult("1");
            ts3.TrySetResult("3");
            ts2.TrySetResult(null);

            var result = await task;
            result.Should().BeEquivalentTo(new[]{"1", "3"}, o => o.WithStrictOrdering());
        }
        
        [Fact]
        public async Task A_Flow_with_SelectAsync_must_handle_cancel_properly()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var pub = this.CreateManualPublisherProbe<int>();
                var sub = this.CreateManualSubscriberProbe<int>();

                Source.FromPublisher(pub)
                    .SelectAsync(4, _ => Task.FromResult(0))
                    .RunWith(Sink.FromSubscriber(sub), Materializer);

                var upstream = await pub.ExpectSubscriptionAsync();
                await upstream.ExpectRequestAsync();

                (await sub.ExpectSubscriptionAsync()).Cancel();

                await upstream.ExpectCancellationAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task A_Flow_with_SelectAsync_must_not_run_more_futures_than_configured()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                const int parallelism = 8;
                var counter = new AtomicCounter();
                var queue = Channel.CreateUnbounded<(TaskCompletionSource<int>, long)>();
                var cancellation = new CancellationTokenSource();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(async () =>
                {
                    var delay = 500; // 50000 nanoseconds
                    var count = 0;
                    var cont = true;
                    while (cont)
                    {
                        try
                        {
                            var t = await queue.Reader.ReadAsync(cancellation.Token);
                            var promise = t.Item1;
                            var enqueued = t.Item2;
                            var wakeup = enqueued + delay;
                            while (DateTime.Now.Ticks < wakeup) { }
                            counter.Decrement();
                            promise.SetResult(count);
                            count++;
                        }
                        catch
                        {
                            cont = false;
                        }
                    }
                }, cancellation.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                try
                {
                    const int n = 10000;
                    var task = Source.From(Enumerable.Range(1, n))
                        .SelectAsync(parallelism, _ => Deferred())
                        .RunAggregate(0, (c, _) => c + 1, Materializer);

                    var complete = await task.ShouldCompleteWithin(3.Seconds());
                    complete.Should().Be(n);
                }
                finally
                {
                    cancellation.Cancel(false);
                }

                return;

                Task<int> Deferred()
                {
                    var promise = new TaskCompletionSource<int>();
                    if (counter.IncrementAndGet() > parallelism)
                        promise.SetException(new Exception("parallelism exceeded"));
                    else
                    {
                        var wrote = queue.Writer.TryWrite((promise, DateTime.Now.Ticks));
                        if (!wrote)
                            promise.SetException(new Exception("Failed to write to queue"));
                    }
                        
                    return promise.Task;
                }
            }, Materializer);
        }
        
        [Fact(DisplayName = "A Flow with SelectAsync must not invoke the decider twice when SelectAsync throws")]
        public async Task SelectAsyncDeciderFailingSelectAsync()
        {
            var failCount = new AtomicCounter(0);
            var result = await Source.From(new[]{true, false})
                .SelectAsync(1, elem => 
                {
                    if (elem)
                        throw new TestException("this has gone too far");
                    return Task.FromResult(elem);
                })
                .AddAttributes(ActorAttributes.CreateSupervisionStrategy(cause =>
                {
                    switch (cause)
                    {
                        case TestException:
                            failCount.IncrementAndGet();
                            return Directive.Resume;
                        default:
                            return Directive.Stop;
                    }
                }))
                .RunWith(Sink.Seq<bool>(), Materializer);

            result.Count.Should().Be(1);
            result[0].Should().BeFalse();
            failCount.Current.Should().Be(1);
        }
        
        [Theory(DisplayName = "SelectAsync with restart decider should restart")]
        [ClassData(typeof(FailingTaskData<ImmutableList<int>>))]
        public async Task SelectAsyncFailingTaskTest(Func<ImmutableList<int>, Task<NotUsed>> mapFunc)
        {
            var materializer = ActorMaterializer.Create(Sys);
        
            var queue = Source
                .Queue<int>(bufferSize: 5000, overflowStrategy: OverflowStrategy.DropNew)
                .BatchWeighted(
                    max: 100,
                    costFunction: i => i,
                    seed: r => ImmutableList.Create([r]),
                    aggregate: (oldRows, i) => oldRows.Add(i))
                .SelectAsync(
                    parallelism: 3,
                    asyncMapper: mapFunc)
                .AddAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .ToMaterialized(Sink.Ignore<NotUsed>(), Keep.Left)
                .Run(materializer);

            Assert.IsType<QueueOfferResult.Enqueued>(await queue.OfferAsync(1));

            await Task.Delay(500.Milliseconds());
            
            // Materializer should stay alive
            Assert.False(materializer.IsShutdown);
        
            // Stream should still work, it should not throw a `StreamDetachedException` 
            Assert.IsType<QueueOfferResult.Enqueued>(await queue.OfferAsync(1));
        }
    }
}
