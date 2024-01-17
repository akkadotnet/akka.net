﻿//-----------------------------------------------------------------------
// <copyright file="FlowAggregateAsyncSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowAggregateAsyncSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowAggregateAsyncSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private static IEnumerable<int> Input => Enumerable.Range(1, 100);
        private static int Expected => Input.Sum();
        private static Source<int, NotUsed> InputSource => Source.From(Input);

        private static Source<int, NotUsed> AggregateSource
            => InputSource.AggregateAsync(0, (sum, i) => Task.Run(() => sum + i));

        private const int FlowDelayInMs = 100;

        private static Flow<int, int, NotUsed> AggregateFlow
            => Flow.Create<int>().AggregateAsync(0, (sum, i) => Task.Run(() =>
            {
                Thread.Sleep(FlowDelayInMs);
                return sum + i;
            }));

        private static Sink<int, Task<int>> AggregateSink
            => Sink.AggregateAsync<int, int>(0, (sum, i) => Task.Run(() => sum + i));



        [Fact]
        public async Task A_AggregateAsync_must_work_when_using_Source_AggregateAsync()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var task = AggregateSource.RunWith(Sink.First<int>(), Materializer);
                var complete = await task.ShouldCompleteWithin(3.Seconds());
                complete.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_work_when_using_Sink_AggregateAsync()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var task = InputSource.RunWith(AggregateSink, Materializer);
                var complete = await task.ShouldCompleteWithin(3.Seconds());
                complete.Should().Be(Expected);
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task A_AggregateAsync_must_work_when_using_Flow_AggregateAsync()
        {
            var flowTimeout = TimeSpan.FromMilliseconds(FlowDelayInMs*Input.Count()) + TimeSpan.FromSeconds(3);
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var task = InputSource.Via(AggregateFlow).RunWith(Sink.First<int>(), Materializer);
                var complete = await task.ShouldCompleteWithin(flowTimeout);
                complete.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_work_when_using_Source_AggregateAsync_and_Flow_AggregateAsync_and_Sink_AggregateAsync()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var task = AggregateSource.Via(AggregateFlow).RunWith(AggregateSink, Materializer);
                var complete = await task.ShouldCompleteWithin(3.Seconds());
                complete.Should().Be(Expected);
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_propagate_an_error()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var error = new TestException("buh");
                var future = InputSource.Select(x =>
                {
                    if (x > 50)
                        throw error;
                    return x;
                }).RunAggregateAsync(NotUsed.Instance, (notused, _) => Task.FromResult(notused), Materializer);

                (await Awaiting(() => future.WaitAsync(RemainingOrDefault)).Should().ThrowAsync<TestException>())
                    .And.Should().Be(error);
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_complete_task_with_failure_when_Aggregating_functions_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var error = new TestException("buh");
                var future = InputSource.RunAggregateAsync(0, (x, y) =>
                {
                    if (x > 50)
                    {
                        var completion = new TaskCompletionSource<int>();
                        completion.SetException(error);
                        return completion.Task;
                    }
                    return Task.Run(() => x + y);
                }, Materializer);

                (await Awaiting(() => future.WaitAsync(RemainingOrDefault)).Should().ThrowAsync<TestException>())
                    .And.Should().Be(error);
            }, Materializer);
        }

        [Fact]
        public void A_AggregateAsync_must_not_blow_up_with_high_request_count()
        {
            var probe = this.CreateManualSubscriberProbe<long>();

            Source.From(Enumerable.Range(1, 10000))
                .AggregateAsync(1L, (a, b) => Task.Run(() => a + b))
                .RunWith(Sink.AsPublisher<long>(true), Materializer)
                .Subscribe(probe);

            var subscription = probe.ExpectSubscription();
            subscription.Request(int.MaxValue);
            probe.ExpectNext(50005001L);
            probe.ExpectComplete();
        }

        [Fact]
        public async Task A_AggregateAsync_must_signal_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var probe = this.CreateSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 5)).AggregateAsync(0, (_, n) => Task.Run(() =>
                {
                    if (n == 3)
                        throw new Exception("err1");
                    return n;
                })).To(Sink.FromSubscriber(probe)).Run(Materializer);

                var subscription = probe.ExpectSubscription();
                subscription.Request(100);
                probe.ExpectError().InnerException.Message.Should().Be("err1");
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_signal_error_from_AggregateAsync()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var c = this.CreateManualSubscriberProbe<int>();

                Source.From(Enumerable.Range(1, 5)).AggregateAsync(0, (_, n) =>
                {
                    if (n == 3)
                        throw new Exception("err2");

                    return Task.FromResult(n);
                }).To(Sink.FromSubscriber(c)).Run(Materializer);

                var subscription = c.ExpectSubscription();
                subscription.Request(10);
                c.ExpectError().Message.Should().Be("err2");
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_resume_after_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var probe = this.CreateSubscriberProbe<(int, int)>();
                Source.From(Enumerable.Range(1, 5)).AggregateAsync((0, 1), (t, n) =>
                    {
                        var i = t.Item1;
                        var res = t.Item2;
                        return Task.Run(() =>
                        {
                            if (n == 3)
                                throw new Exception("err3");

                            return (n, i + res * n);
                        });
                    })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .To(Sink.FromSubscriber(probe))
                    .Run(Materializer);

                var subscription = probe.ExpectSubscription();
                subscription.Request(10);
                probe.ExpectNext((5, 74));
                probe.ExpectComplete();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_restart_after_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var probe = this.CreateSubscriberProbe<(int, int)>();
                Source.From(Enumerable.Range(1, 5)).AggregateAsync((0, 1), (t, n) =>
                {
                    var i = t.Item1;
                    var res = t.Item2;
                    return Task.Run(() =>
                    {
                        if (n == 3)
                            throw new Exception("err3");

                        return (n, i + res * n);
                    });
                })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                    .To(Sink.FromSubscriber(probe))
                    .Run(Materializer);

                var subscription = probe.ExpectSubscription();
                subscription.Request(10);
                probe.ExpectNext((5, 24));
                probe.ExpectComplete();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_resume_after_multiple_failures()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var tasks = new []
                {
                    Task.FromException<string>(new Exception("failure1")),
                    Task.FromException<string>(new Exception("failure2")),
                    Task.FromException<string>(new Exception("failure3")),
                    Task.FromException<string>(new Exception("failure4")),
                    Task.FromException<string>(new Exception("failure5")),
                    Task.FromResult("happy!")
                };

                var result = await Source.From(tasks)
                    .AggregateAsync(string.Empty, (_, t) => t)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.First<string>(), Materializer).ShouldCompleteWithin(3.Seconds());
                    
                result.Should().Be("happy!");
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_finish_after_task_failure()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var complete = await Source.From(Enumerable.Range(1, 3)).AggregateAsync(1, (_, n) => Task.Run(() =>
                    {
                        if (n == 3)
                            throw new Exception("err3b");
                        return n;
                    }))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .Grouped(10)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer)
                    .ShouldCompleteWithin(3.Seconds());
                complete.Should().BeEquivalentTo(2);
            }, Materializer);
        }

        [Fact]
        public void A_AggregateAsync_must_resume_when_AggregateAsync_throws()
        {
            var probe = this.CreateSubscriberProbe<(int, int)>();
            Source.From(Enumerable.Range(1, 5)).AggregateAsync((0, 1), (t, n) =>
            {
                var i = t.Item1;
                var res = t.Item2;
                if (n == 3)
                    throw new Exception("err4");
                return Task.Run(() => (n, i + res * n));
            })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .To(Sink.FromSubscriber(probe))
                .Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(10);
            probe.ExpectNext((5, 74));
            probe.ExpectComplete();
        }

        [Fact]
        public void A_AggregateAsync_must_restart_when_AggregateAsync_throws()
        {
            var probe = this.CreateSubscriberProbe<(int, int)>();
            Source.From(Enumerable.Range(1, 5)).AggregateAsync((0, 1), (t, n) =>
                {
                    var i = t.Item1;
                    var res = t.Item2;
                    if (n == 3)
                        throw new Exception("err4");
                    return Task.Run(() => (n, i + res*n));
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .To(Sink.FromSubscriber(probe))
                .Run(Materializer);

            var subscription = probe.ExpectSubscription();
            subscription.Request(10);
            probe.ExpectNext((5, 24));
            probe.ExpectComplete();
        }

        [Fact]
        public void A_AggregateAsync_must_signal_NPE_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.From(new[] {"a", "b"})
                .AggregateAsync("", (_, _) => Task.FromResult<string>(null))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var subscription = c.ExpectSubscription();
            subscription.Request(10);
            c.ExpectError().Message.Should().StartWith(ReactiveStreamsCompliance.ElementMustNotBeNullMsg);
        }

        [Fact]
        public void A_AggregateAsync_must_resume_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.From(new[] {"a", "b", "c"})
                .AggregateAsync("", (str, element) =>
                {
                    if (element == "b")
                        return Task.FromResult<string>(null);
                    
                    return Task.FromResult(str + element);
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var subscription = c.ExpectSubscription();
            subscription.Request(10);
            c.ExpectNext("ac"); // 1: "" + "a"; 2: null => resume "a"; 3: "a" + "c"
            c.ExpectComplete();
        }

        [Fact]
        public void A_AggregateAsync_must_restart_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.From(new[] { "a", "b", "c" })
                .AggregateAsync("", (str, element) =>
                {
                    if (element == "b")
                        return Task.FromResult<string>(null);

                    return Task.FromResult(str + element);
                }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);

            var subscription = c.ExpectSubscription();
            subscription.Request(10);
            c.ExpectNext("c"); // 1: "" + "a"; 2: null => restart ""; 3: "" + "c"
            c.ExpectComplete();
        }

        [Fact]
        public async Task A_AggregateAsync_must_handle_cancel_properly()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var pub = this.CreateManualPublisherProbe<int>();
                var sub = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(pub)
                    .AggregateAsync(0, (_, n) => Task.FromResult(n))
                    .RunWith(Sink.FromSubscriber(sub), Materializer);

                var upstream = pub.ExpectSubscription();
                upstream.ExpectRequest();

                sub.ExpectSubscription().Cancel();

                upstream.ExpectCancellation();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_complete_task_and_return_zero_given_an_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var task = Source.From(Enumerable.Empty<int>())
                    .RunAggregateAsync(0, (acc, element) => Task.FromResult(acc + element), Materializer);
                var complete = await task.ShouldCompleteWithin(RemainingOrDefault);
                complete.ShouldBe(0);
            }, Materializer);
        }

        [Fact]
        public async Task A_AggregateAsync_must_complete_task_and_return_zero_and_item_given_a_stream_of_one_item()
        {
            await this.AssertAllStagesStoppedAsync(async() =>
            {
                var task = Source.Single(100)
                    .RunAggregateAsync(5, (acc, element) => Task.FromResult(acc + element), Materializer);
                var complete = await task.ShouldCompleteWithin(RemainingOrDefault);
                complete.ShouldBe(105);
            }, Materializer);
        }
    }
}
