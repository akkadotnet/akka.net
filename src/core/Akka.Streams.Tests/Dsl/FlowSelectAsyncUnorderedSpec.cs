//-----------------------------------------------------------------------
// <copyright file="FlowSelectAsyncUnorderedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InvokeAsExtensionMethod
#pragma warning disable 162

namespace Akka.Streams.Tests.Dsl
{
    [Collection(nameof(FlowSelectAsyncUnorderedSpec))]
    public class FlowSelectAsyncUnorderedSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSelectAsyncUnorderedSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact(Skip ="Racy in Linux")]
        public void A_Flow_with_SelectAsyncUnordered_must_produce_task_elements_in_the_order_they_are_ready()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var latch = Enumerable.Range(0, 4).Select(_ => new TestLatch(1)).ToArray();

                Source.From(Enumerable.Range(0, 4)).SelectAsyncUnordered(4, n => Task.Run(() =>
                {
                    latch[n].Ready(TimeSpan.FromSeconds(5));
                    return n;
                })).To(Sink.FromSubscriber(c)).Run(Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(5);

                latch[1].CountDown();
                c.ExpectNext(1);

                latch[3].CountDown();
                c.ExpectNext(3);

                latch[2].CountDown();
                c.ExpectNext(2);

                latch[0].CountDown();
                c.ExpectNext(0);

                c.ExpectComplete();
            }, Materializer);
            
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void A_Flow_with_SelectAsyncUnordered_must_not_run_more_futures_than_requested_elements()
        {
            var probe = CreateTestProbe();
            var c = this.CreateManualSubscriberProbe<int>();
            Source.From(Enumerable.Range(1, 20))
                .SelectAsyncUnordered(4, n => 
                {
                    if (n%3 == 0)
                    {
                        probe.Ref.Tell(n);
                        return Task.FromResult(n);
                    }

                    return Task.Run(() =>
                    {
                        probe.Ref.Tell(n);
                        return n;
                    });
                })
                .To(Sink.FromSubscriber(c)).Run(Materializer);
            var sub = c.ExpectSubscription();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            probe.ExpectNoMsg(TimeSpan.Zero);
            sub.Request(1);
            var got = new List<int> {c.ExpectNext()};
            probe.ExpectMsgAllOf(1, 2, 3, 4, 5);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            sub.Request(25);
            probe.ExpectMsgAllOf(Enumerable.Range(6, 15).ToArray());
            c.Within(TimeSpan.FromSeconds(3), () =>
            {
                Enumerable.Range(2, 19).ForEach(_ => got.Add(c.ExpectNext()));
                return NotUsed.Instance;
            });
            got.ShouldAllBeEquivalentTo(Enumerable.Range(1, 20));
            c.ExpectComplete();
        }

        [Fact(Skip = "Racy")]
        public void A_Flow_with_SelectAsyncUnordered_must_signal_task_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var latch = new TestLatch(1);
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsyncUnordered(4, n => Task.Run(() =>
                    {
                        if (n == 3)
                            throw new TestException("err1");

                        latch.Ready(TimeSpan.FromSeconds(10));
                        return n;
                    }))
                    .To(Sink.FromSubscriber(c)).Run(Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(10);
                c.ExpectError().InnerException.Message.Should().Be("err1");
                latch.CountDown();
            }, Materializer);
        }


        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_signal_task_failure_asap()
        {
            this.AssertAllStagesStopped(() =>
            {
                var latch = CreateTestLatch();
                var done = Source.From(Enumerable.Range(1, 5))
                    .Select(n =>
                    {
                        if (n != 1)
                            // slow upstream should not block the error
                            latch.Ready(TimeSpan.FromSeconds(10));

                        return n;
                    })
                    .SelectAsyncUnordered(4, n =>
                    {
                        if (n == 1)
                        {
                            var c = new TaskCompletionSource<int>();
                            c.SetException(new Exception("err1"));
                            return c.Task;
                        }

                        return Task.FromResult(n);
                    }).RunWith(Sink.Ignore<int>(), Materializer);

                done.Invoking(d => d.Wait(RemainingOrDefault)).ShouldThrow<Exception>().WithMessage("err1");
                latch.CountDown();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_signal_error_from_SelectAsyncUnordered()
        {
            this.AssertAllStagesStopped(() =>
            {
                var latch = new TestLatch(1);
                var c = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 5))
                    .SelectAsyncUnordered(4, n =>
                    {
                        if (n == 3)
                            throw new TestException("err2");

                        return Task.Run(() =>
                        {
                            latch.Ready(TimeSpan.FromSeconds(10));
                            return n;
                        });
                    })
                    .RunWith(Sink.FromSubscriber(c), Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(10);
                c.ExpectError().Message.Should().Be("err2");
                latch.CountDown();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_resume_after_task_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                this.AssertAllStagesStopped(() =>
                {
                    Source.From(Enumerable.Range(1, 5))
                        .SelectAsyncUnordered(4, n => Task.Run(() =>
                        {
                            if (n == 3)
                                throw new TestException("err3");
                            return n;
                        }))
                        .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                        .RunWith(this.SinkProbe<int>(), Materializer)
                        .Request(10)
                        .ExpectNextUnordered(1, 2, 4, 5)
                        .ExpectComplete();
                }, Materializer);
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_resume_after_multiple_failures()
        {
            this.AssertAllStagesStopped(() =>
            {
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
                    .SelectAsyncUnordered(2, x => x)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.First<string>(), Materializer);

                t.AwaitResult().Should().Be("happy");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_finish_after_task_failure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = Source.From(Enumerable.Range(1, 3))
                    .SelectAsyncUnordered(1, n => Task.Run(() =>
                    {
                        if (n == 3)
                            throw new TestException("err3b");
                        return n;
                    }))
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .Grouped(10)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                t.Result.ShouldAllBeEquivalentTo(new[] {1, 2});
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_resume_when_SelectAsyncUnordered_throws()
        {
            Source.From(Enumerable.Range(1, 5))
                .SelectAsyncUnordered(4, n =>
                {
                    if (n == 3)
                        throw new TestException("err4");
                    return Task.FromResult(n);
                })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(this.SinkProbe<int>(), Materializer)
                .Request(10)
                .ExpectNextUnordered(1, 2, 4, 5)
                .ExpectComplete();
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_signal_NPE_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();

            Source.From(new[] {"a", "b"})
                .SelectAsyncUnordered(4, _ => Task.FromResult(null as string))
                .To(Sink.FromSubscriber(c)).Run(Materializer);

            var sub = c.ExpectSubscription();
            sub.Request(10);
            c.ExpectError().Message.Should().StartWith(ReactiveStreamsCompliance.ElementMustNotBeNullMsg);
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_resume_when_task_is_completed_with_null()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.From(new[] { "a", "b", "c" })
                .SelectAsyncUnordered(4, s => s.Equals("b") ? Task.FromResult(null as string) : Task.FromResult(s))
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .To(Sink.FromSubscriber(c)).Run(Materializer);
            var sub = c.ExpectSubscription();
            sub.Request(10);
            c.ExpectNextUnordered("a", "c");
            c.ExpectComplete();
        }

        [Fact]
        public void A_Flow_with_SelectAsyncUnordered_must_handle_cancel_properly()
        {
            this.AssertAllStagesStopped(() =>
            {
                var pub = this.CreateManualPublisherProbe<int>();
                var sub = this.CreateManualSubscriberProbe<int>();

                Source.FromPublisher(pub)
                    .SelectAsyncUnordered(4, _ => Task.FromResult(0))
                    .RunWith(Sink.FromSubscriber(sub), Materializer);

                var upstream = pub.ExpectSubscription();
                upstream.ExpectRequest();

                sub.ExpectSubscription().Cancel();

                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact(Skip = "Racy on AzureDevOps")]
        public void A_Flow_with_SelectAsyncUnordered_must_not_run_more_futures_than_configured()
        {
            this.AssertAllStagesStopped(() =>
            {
                const int parallelism = 8;
                var counter = new AtomicCounter();
                var queue = new BlockingQueue<(TaskCompletionSource<int>, long)>();
                var cancellation = new CancellationTokenSource();

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
                        .SelectAsyncUnordered(parallelism, _ => deferred())
                        .RunAggregate(0, (c, _) => c + 1, Materializer);

                    task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                    task.Result.Should().Be(n);
                }
                finally
                {
                    cancellation.Cancel(false);
                }
            }, Materializer);
        }
    }
}
