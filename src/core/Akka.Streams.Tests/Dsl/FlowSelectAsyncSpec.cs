﻿//-----------------------------------------------------------------------
// <copyright file="FlowSelectAsyncSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
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
        public async void A_Flow_with_SelectAsync_must_produce_task_elements_in_order()
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
            //Enumerable.Range(1, 50).ForEach(n => c.ExpectNext(n));
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

                foreach (var n in Enumerable.Range(1, 13))
                    await c.ExpectNextAsync(n);
            
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
                    .ExpectNextN(new[]{1, 2})
                    .ExpectErrorAsync()
                    .ShouldCompleteWithin(RemainingOrDefault);
                exception.InnerException!.Message.Should().Be("err1");
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

                await Awaiting(() => done.WaitAsync(RemainingOrDefault)).Should()
                    .ThrowAsync<Exception>()
                    .WithMessage("err1")
                    .ShouldCompleteWithin(RemainingOrDefault);
                
                latch.CountDown();
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
                c.ExpectError().Message.Should().Be("err2");
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
                    .ExpectNextN(new[] { 1, 2 })
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
        public async Task A_Flow_with_SelectAsync_must_resume_after_multiple_failures()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
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

                (await t.WaitAsync(3.Seconds())).Should().Be("happy");
            }, Materializer);
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
        public async Task A_Flow_with_SelectAsync_must_signal_NPE_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();

            Source.From(new[] {"a", "b"})
                .SelectAsync(4, _ => Task.FromResult(null as string))
                .To(Sink.FromSubscriber(c)).Run(Materializer);

            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            c.ExpectError().Message.Should().StartWith(ReactiveStreamsCompliance.ElementMustNotBeNullMsg);
        }

        [Fact]
        public async Task A_Flow_with_SelectAsync_must_resume_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.From(new[] { "a", "b", "c" })
                .SelectAsync(4, s => s.Equals("b") ? Task.FromResult(null as string) : Task.FromResult(s))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .To(Sink.FromSubscriber(c)).Run(Materializer);
            var sub = await c.ExpectSubscriptionAsync();
            sub.Request(10);
            await c.ExpectNextAsync("a");
            await c.ExpectNextAsync("c");
            await c.ExpectCompleteAsync();
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
                var queue = new BlockingQueue<(TaskCompletionSource<int>, long)>();
                var cancellation = new CancellationTokenSource();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(() =>
                {
                    var delay = 500; // 50000 nanoseconds
                    var count = 0;
                    var cont = true;
                    while (cont)
                    {
                        try
                        {
                            var t = queue.Take(cancellation.Token);
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

                Func<Task<int>> deferred = () =>
                {
                    var promise = new TaskCompletionSource<int>();
                    if (counter.IncrementAndGet() > parallelism)
                        promise.SetException(new Exception("parallelism exceeded"));
                    else

                        queue.Enqueue((promise, DateTime.Now.Ticks));
                    return promise.Task;
                };

                try
                {
                    const int n = 10000;
                    var task = Source.From(Enumerable.Range(1, n))
                        .SelectAsync(parallelism, _ => deferred())
                        .RunAggregate(0, (c, _) => c + 1, Materializer);

                    var complete = await task.ShouldCompleteWithin(3.Seconds());
                    complete.Should().Be(n);
                }
                finally
                {
                    cancellation.Cancel(false);
                }
            }, Materializer);
        }
    }
}
