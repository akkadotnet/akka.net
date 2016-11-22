//-----------------------------------------------------------------------
// <copyright file="HubSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Akka.Actor;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class HubSpec : AkkaSpec
    {
        public HubSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = Sys.Materializer();
        }

        public ActorMaterializer Materializer { get; }

        [Fact]
        public void MergeHub_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16).Take(20).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;
                Source.From(Enumerable.Range(1, 10)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(11, 10)).RunWith(sink, Materializer);

                result.AwaitResult().OrderBy(x => x).ShouldAllBeEquivalentTo(Enumerable.Range(1, 20));
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_notify_new_producers_if_consumer_cancels_before_first_producer()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sink = Sink.Cancelled<int>().RunWith(MergeHub.Source<int>(16), Materializer);
                var upstream = this.CreatePublisherProbe<int>();

                Source.FromPublisher(upstream).RunWith(sink, Materializer);

                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_notify_existing_producers_if_consumer_cancels_after_a_few_elements()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16).Take(5).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;
                var upstream = this.CreatePublisherProbe<int>();

                Source.FromPublisher(upstream).RunWith(sink, Materializer);
                for (var i = 1; i < 6; i++)
                    upstream.SendNext(i);

                upstream.ExpectCancellation();
                result.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 5));
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_notify_new_producers_if_consumer_cancels_after_a_few_elements()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16).Take(5).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<int>();

                Source.FromPublisher(upstream1).RunWith(sink, Materializer);
                for (var i = 1; i < 6; i++)
                    upstream1.SendNext(i);

                upstream1.ExpectCancellation();
                result.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 5));

                Source.FromPublisher(upstream2).RunWith(sink, Materializer);
                upstream2.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_respect_the_buffer_size()
        {
            this.AssertAllStagesStopped(() =>
            {
                var downstream = this.CreateManualSubscriberProbe<int>();
                var sink = Sink.FromSubscriber(downstream).RunWith(MergeHub.Source<int>(3), Materializer);

                Source.From(Enumerable.Range(1, 10)).Select(i =>
                {
                    TestActor.Tell(i);
                    return i;
                }).RunWith(sink, Materializer);

                var sub = downstream.ExpectSubscription();
                sub.Request(1);

                // Demand starts from 3
                ExpectMsg(1);
                ExpectMsg(2);
                ExpectMsg(3);
                ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                // One element consumed (it was requested), demand 0 remains at producer
                downstream.ExpectNext(1);

                // Requesting next element, results in next elemetn to be consumed.
                sub.Request(1);
                downstream.ExpectNext(2);

                // Two elements habe been consumed, so threshold of 2 is reached, additional 2 demand is dispatched.
                // There is 2 demand at the producer now

                ExpectMsg(4);
                ExpectMsg(5);
                ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                // Two additional elements have been sent:
                // - 3, 4, 5 are pending
                // - demand is 0 at the producer
                // - next demand batch is after two elements have been consumed again

                // Requesting next gives the next element
                // Demand is not yet refreshed for the producer as there is one more element until threshold is met
                sub.Request(1);
                downstream.ExpectNext(3);

                ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                sub.Request(1);
                downstream.ExpectNext(4);
                ExpectMsg(6);
                ExpectMsg(7);

                sub.Cancel();
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_work_with_long_streams()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16).Take(20000).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;

                Source.From(Enumerable.Range(1, 10000)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(10001, 10000)).RunWith(sink, Materializer);

                result.AwaitResult().OrderBy(x => x).ShouldAllBeEquivalentTo(Enumerable.Range(1, 20000));
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_work_with_long_streams_when_buffer_size_is_1()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(1).Take(20000).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;

                Source.From(Enumerable.Range(1, 10000)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(10001, 10000)).RunWith(sink, Materializer);

                result.AwaitResult().OrderBy(x => x).ShouldAllBeEquivalentTo(Enumerable.Range(1, 20000));
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_work_with_long_streams_when_consumer_is_slower()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16)
                    .Take(2000)
                    .Throttle(10, TimeSpan.FromMilliseconds(1), 200, ThrottleMode.Shaping)
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;

                Source.From(Enumerable.Range(1, 1000)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(1001, 1000)).RunWith(sink, Materializer);

                result.AwaitResult().OrderBy(x => x).ShouldAllBeEquivalentTo(Enumerable.Range(1, 2000));

            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_work_with_long_streams_if_one_of_the_producers_is_slower()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16).Take(2000).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;

                Source.From(Enumerable.Range(1, 1000))
                    .Throttle(10, TimeSpan.FromMilliseconds(1), 100, ThrottleMode.Shaping)
                    .RunWith(sink, Materializer);
                Source.From(Enumerable.Range(1001, 1000)).RunWith(sink, Materializer);

                result.AwaitResult().OrderBy(x => x).ShouldAllBeEquivalentTo(Enumerable.Range(1, 2000));
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_work_with_different_producers_separated_over_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();
                var sink = MergeHub.Source<int>(16)
                    .Grouped(100)
                    .ToMaterialized(Sink.FromSubscriber(downstream), Keep.Left)
                    .Run(Materializer);
                Source.From(Enumerable.Range(1, 100)).RunWith(sink, Materializer);
                downstream.RequestNext().ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));

                Source.From(Enumerable.Range(101, 100)).RunWith(sink, Materializer);
                downstream.RequestNext().ShouldAllBeEquivalentTo(Enumerable.Range(101, 100));

                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void MergeHub_must_keep_working_even_if_one_of_the_producers_fail()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = MergeHub.Source<int>(16).Take(10).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var sink = t.Item1;
                var result = t.Item2;

                EventFilter.Error(contains: "Upstream producer failed with exception").ExpectOne(() =>
                {
                    Source.Failed<int>(new TestException("failing")).RunWith(sink, Materializer);
                    Source.From(Enumerable.Range(1, 10)).RunWith(sink, Materializer);
                });

                result.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(Enumerable.Range(1, 10)).RunWith(BroadcastHub.Sink<int>(8), Materializer);
                source.RunWith(Sink.Seq<int>(), Materializer)
                    .AwaitResult()
                    .ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_send_the_same_elements_to_consumers_attaching_around_the_same_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var t = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(500);
                firstElement.SetResult(1);
                f1.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_send_the_same_prefix_to_consumers_attaching_around_the_same_time_if_one_cancels_earlier()
        {
            this.AssertAllStagesStopped(() =>
            {
                var other = Source.From(Enumerable.Range(2, 19))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var t = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.Take(10).RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(500);
                firstElement.SetResult(1);
                f1.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 20));
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_ensure_that_subsequent_consumers_see_subsequent_elements_without_gap()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(Enumerable.Range(1, 20)).RunWith(BroadcastHub.Sink<int>(8), Materializer);
                source.Take(10)
                    .RunWith(Sink.Seq<int>(), Materializer)
                    .AwaitResult()
                    .ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                source.Take(10)
                    .RunWith(Sink.Seq<int>(), Materializer)
                    .AwaitResult()
                    .ShouldAllBeEquivalentTo(Enumerable.Range(11, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_send_the_same_elements_to_consumers_of_different_speed_attaching_around_the_same_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var t = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source.Throttle(1, TimeSpan.FromMilliseconds(10), 3, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(500);
                firstElement.SetResult(1);
                f1.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_send_the_same_elements_to_consumers_of_attaching_around_the_same_time_if_the_producer_is_slow()
        {
            this.AssertAllStagesStopped(() =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var t = Source.Maybe<int>()
                    .Concat(other)
                    .Throttle(1, TimeSpan.FromMilliseconds(10), 3, ThrottleMode.Shaping)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(500);
                firstElement.SetResult(1);
                f1.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_ensure_that_from_two_different_speed_consumers_the_slower_controls_the_rate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var other = Source.From(Enumerable.Range(2, 19))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var t = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(1), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source
                    .Throttle(1, TimeSpan.FromMilliseconds(10), 1, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);
                // Second cannot be overwhelmed since the first one throttles the overall rate, and second allows a higher rate
                var f2 = source
                    .Throttle(10, TimeSpan.FromMilliseconds(10), 8, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(100);
                firstElement.SetResult(1);
                f1.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 20));
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 20));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_send_the_same_elements_to_consumers_attaching_around_the_same_time_with_a_buffer_size_of_one()
        {
            this.AssertAllStagesStopped(() =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var t = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(1), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(500);
                firstElement.SetResult(1);
                f1.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_be_able_to_implement_a_keep_dropping_if_unsubscribed_policy_with_a_simple_SinkIgnore()
        {
            this.AssertAllStagesStopped(() =>
            {
                var killSwitch = KillSwitches.Shared("test-switch");
                var source = Source.From(Enumerable.Range(1, int.MaxValue))
                    .Via(killSwitch.Flow<int>())
                    .RunWith(BroadcastHub.Sink<int>(8), Materializer);

                // Now the Hub "drops" elements until we attach a new consumer (Source.ignore consumes as fast as possible)
                source.RunWith(Sink.Ignore<int>(), Materializer);

                // Now we attached a subscriber which will block the Sink.ignore to "take away" and drop elements anymore,
                // turning the BroadcastHub to a normal non-dropping mode
                var downstream = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(1);
                var first = downstream.ExpectNext();

                for (var i = first + 1; i < first + 11; i++)
                {
                    downstream.Request(1);
                    downstream.ExpectNext(i);
                }

                downstream.Cancel();
                killSwitch.Shutdown();
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_properly_signal_error_to_consumers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var source = Source.FromPublisher(upstream).RunWith(BroadcastHub.Sink<int>(8), Materializer);

                var downstream1 = this.CreateSubscriberProbe<int>();
                var downstream2 = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream1), Materializer);
                source.RunWith(Sink.FromSubscriber(downstream2), Materializer);

                downstream1.Request(4);
                downstream2.Request(8);

                Enumerable.Range(1, 8).ForEach(x => upstream.SendNext(x));

                downstream1.ExpectNext(1, 2, 3, 4);
                downstream2.ExpectNext(1, 2, 3, 4, 5, 6, 7, 8);

                downstream1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstream2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                upstream.SendError(new TestException("failed"));
                downstream1.ExpectError().Message.Should().Be("failed");
                downstream2.ExpectError().Message.Should().Be("failed");
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_properly_singal_completion_to_consumers_arriving_after_producer_finished()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.Empty<int>().RunWith(BroadcastHub.Sink<int>(8), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                Thread.Sleep(50);

                source.RunWith(Sink.Seq<int>(), Materializer).AwaitResult().Should().BeEmpty();
            }, Materializer);
        }

        [Fact]
        public void BroadcastHub_must_properly_singal_error_to_consumers_arriving_after_producer_finished()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.Failed<int>(new TestException("Fail!"))
                    .RunWith(BroadcastHub.Sink<int>(8), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                Thread.Sleep(50);

                var task = source.RunWith(Sink.Seq<int>(), Materializer);
                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<TestException>();
            }, Materializer);
        }
    }
}
