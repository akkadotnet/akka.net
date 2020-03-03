//-----------------------------------------------------------------------
// <copyright file="HubSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

                // Requesting next element, results in next element to be consumed.
                sub.Request(1);
                downstream.ExpectNext(2);

                // Two elements have been consumed, so threshold of 2 is reached, additional 2 demand is dispatched.
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
                var t = MergeHub.Source<int>(16).Take(20000).ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);
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
                var t = MergeHub.Source<int>(1).Take(20000).ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);
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
                var t = MergeHub.Source<int>(16).Take(2000).ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);
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
        public void BroadcastHub_must_remember_completion_for_materialisations_after_completion()
        {
            var t = this.SourceProbe<NotUsed>()
                .ToMaterialized(BroadcastHub.Sink<NotUsed>(), Keep.Both)
                .Run(Materializer);
            var sourceProbe = t.Item1;
            var source = t.Item2;
            var sinkProbe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

            sourceProbe.SendComplete();

            sinkProbe.Request(1).ExpectComplete();

            // Materialize a second time. There was a race here, where we managed to enqueue our Source registration just
            // immediately before the Hub shut down.
            var sink2Probe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

            sink2Probe.Request(1).ExpectComplete();
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
        public void BroadcastHub_must_properly_signal_completion_to_consumers_arriving_after_producer_finished()
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
        public void BroadcastHub_must_properly_signal_error_to_consumers_arriving_after_producer_finished()
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

        [Fact]
        public void PartitionHub_must_work_in_the_happy_case_with_one_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var items = Enumerable.Range(1, 10).ToList();
                var source = Source.From(items)
                    .RunWith(PartitionHub.Sink<int>((size, e) => 0, 0, 8), Materializer);
                var result = source.RunWith(Sink.Seq<int>(), Materializer).AwaitResult();
                result.ShouldAllBeEquivalentTo(items);
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_work_in_the_happy_case_with_two_streams()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(Enumerable.Range(0, 10))
                    .RunWith(PartitionHub.Sink<int>((size, e) => e % size, 2, 8), Materializer);
                var result1 = source.RunWith(Sink.Seq<int>(), Materializer);
                // it should not start publishing until startAfterNrOfConsumers = 2
                Thread.Sleep(50);
                var result2 = source.RunWith(Sink.Seq<int>(), Materializer);
                result1.AwaitResult().ShouldAllBeEquivalentTo(new[] { 0, 2, 4, 6, 8 });
                result2.AwaitResult().ShouldAllBeEquivalentTo(new[] { 1, 3, 5, 7, 9 });
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_be_able_to_use_as_rount_robin_router()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(Enumerable.Range(0, 10))
                    .RunWith(PartitionHub.StatefulSink<int>(() =>
                    {
                        var n = 0L;
                        return ((info, e) =>
                        {
                            n++;
                            return info.ConsumerByIndex((int)n % info.Size);
                        });
                    }, 2, 8), Materializer);
                var result1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var result2 = source.RunWith(Sink.Seq<int>(), Materializer);
                result1.AwaitResult().ShouldAllBeEquivalentTo(new[] { 1, 3, 5, 7, 9 });
                result2.AwaitResult().ShouldAllBeEquivalentTo(new[] { 0, 2, 4, 6, 8 });
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_be_able_to_use_as__sticky_session_rount_robin_router()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(new[] { "usr-1", "usr-2", "usr-1", "usr-3" })
                    .RunWith(PartitionHub.StatefulSink<string>(() =>
                    {
                        var session = new Dictionary<string, long>();
                        var n = 0L;
                        return ((info, e) =>
                        {
                            if (session.TryGetValue(e, out var i) && info.ConsumerIds.Contains(i))
                                return i;
                            n++;
                            var id = info.ConsumerByIndex((int)n % info.Size);
                            session[e] = id;
                            return id;
                        });
                    }, 2, 8), Materializer);
                var result1 = source.RunWith(Sink.Seq<string>(), Materializer);
                var result2 = source.RunWith(Sink.Seq<string>(), Materializer);
                result1.AwaitResult().ShouldAllBeEquivalentTo(new[] { "usr-2" });
                result2.AwaitResult().ShouldAllBeEquivalentTo(new[] { "usr-1", "usr-1", "usr-3" });
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_be_able_to_use_as_fastest_consumer_router()
        {
            this.AssertAllStagesStopped(() =>
            {
                var items = Enumerable.Range(0, 999).ToList();
                var source = Source.From(items)
                    .RunWith(
                        PartitionHub.StatefulSink<int>(() => ((info, i) => info.ConsumerIds.Min(info.QueueSize)), 2, 4),
                        Materializer);
                var result1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var result2 = source.Throttle(10, TimeSpan.FromMilliseconds(100), 10, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);

                result1.AwaitResult().Count.ShouldBeGreaterThan(result2.AwaitResult().Count);
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_route_evenly()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = this.SourceProbe<int>()
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => e % size, 2, 8), Keep.Both)
                    .Run(Materializer);

                var testSource = t.Item1;
                var hub = t.Item2;
                var probe0 = hub.RunWith(this.SinkProbe<int>(), Materializer);
                var probe1 = hub.RunWith(this.SinkProbe<int>(), Materializer);

                probe0.Request(3);
                probe1.Request(10);
                testSource.SendNext(0);
                probe0.ExpectNext(0);
                testSource.SendNext(1);
                probe1.ExpectNext(1);

                testSource.SendNext(2);
                testSource.SendNext(3);
                testSource.SendNext(4);
                probe0.ExpectNext(2);
                probe1.ExpectNext(3);
                probe0.ExpectNext(4);

                // probe1 has not requested more
                testSource.SendNext(5);
                testSource.SendNext(6);
                testSource.SendNext(7);
                probe1.ExpectNext(5);
                probe1.ExpectNext(7);
                probe0.ExpectNoMsg(TimeSpan.FromMilliseconds(50));
                probe0.Request(10);
                probe0.ExpectNext(6);

                testSource.SendComplete();
                probe0.ExpectComplete();
                probe1.ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_route_unevenly()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = this.SourceProbe<int>()
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => (e % 3) % 2, 2, 8), Keep.Both)
                    .Run(Materializer);

                var testSource = t.Item1;
                var hub = t.Item2;
                var probe0 = hub.RunWith(this.SinkProbe<int>(), Materializer);
                var probe1 = hub.RunWith(this.SinkProbe<int>(), Materializer);

                // (_ % 3) % 2
                // 0 => 0
                // 1 => 1
                // 2 => 0
                // 3 => 0
                // 4 => 1

                probe0.Request(10);
                probe1.Request(10);
                testSource.SendNext(0);
                probe0.ExpectNext(0);
                testSource.SendNext(1);
                probe1.ExpectNext(1);
                testSource.SendNext(2);
                probe0.ExpectNext(2);
                testSource.SendNext(3);
                probe0.ExpectNext(3);
                testSource.SendNext(4);
                probe1.ExpectNext(4);

                testSource.SendComplete();
                probe0.ExpectComplete();
                probe1.ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_backpressure()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = this.SourceProbe<int>()
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => 0, 2, 4), Keep.Both)
                    .Run(Materializer);

                var testSource = t.Item1;
                var hub = t.Item2;
                var probe0 = hub.RunWith(this.SinkProbe<int>(), Materializer);
                var probe1 = hub.RunWith(this.SinkProbe<int>(), Materializer);

                probe0.Request(10);
                probe1.Request(10);
                testSource.SendNext(0);
                probe0.ExpectNext(0);
                testSource.SendNext(1);
                probe0.ExpectNext(1);
                testSource.SendNext(2);
                probe0.ExpectNext(2);
                testSource.SendNext(3);
                probe0.ExpectNext(3);
                testSource.SendNext(4);
                probe0.ExpectNext(4);

                testSource.SendComplete();
                probe0.ExpectComplete();
                probe1.ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_ensure_that_from_two_different_speed_consumers_the_slower_controls_the_rate()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = Source.Maybe<int>().ConcatMaterialized(Source.From(Enumerable.Range(1, 19)), Keep.Left)
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => e % size, 2, 1), Keep.Both)
                    .Run(Materializer);
                var firstElement = t.Item1;
                var source = t.Item2;

                var f1 = source.Throttle(1, TimeSpan.FromMilliseconds(10), 1, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);

                // Second cannot be overwhelmed since the first one throttles the overall rate, and second allows a higher rate
                var f2 = source.Throttle(10, TimeSpan.FromMilliseconds(10), 8, ThrottleMode.Enforcing)
                    .RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                Thread.Sleep(100);
                // on the jvm Some 0 is used, unfortunately haven't we used Option<T> for the Maybe source
                // and therefore firstElement.SetResult(0) will complete the source without pushing an element
                // since 0 is the default value for int and if you set the result to default(T) it will ignore
                // the element and complete the source. We should probably fix this in the feature. 
                firstElement.SetResult(50);

                var expectationF1 = Enumerable.Range(1, 18).Where(v => v % 2 == 0).ToList();
                expectationF1.Insert(0, 50);

                f1.AwaitResult().ShouldAllBeEquivalentTo(expectationF1);
                f2.AwaitResult().ShouldAllBeEquivalentTo(Enumerable.Range(1, 19).Where(v => v % 2 != 0));
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_properly_signal_error_to_consumer()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var source = Source.FromPublisher(upstream)
                    .RunWith(PartitionHub.Sink<int>((s, e) => e % s, 2, 8), Materializer);

                var downstream1 = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream1), Materializer);
                var downstream2 = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream2), Materializer);

                downstream1.Request(4);
                downstream2.Request(8);

                Enumerable.Range(0, 16).ForEach(i => upstream.SendNext(i));

                downstream1.ExpectNext(0, 2, 4, 6);
                downstream2.ExpectNext(1, 3, 5, 7, 9, 11, 13, 15);

                downstream1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstream2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                var failure = new TestException("Failed");
                upstream.SendError(failure);

                downstream1.ExpectError().Should().Be(failure);
                downstream2.ExpectError().Should().Be(failure);
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_properly_signal_completion_to_consumers_arriving_after_producer_finished()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.Empty<int>().RunWith(PartitionHub.Sink<int>((s, e) => e % s, 0), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                Thread.Sleep(50);

                source.RunWith(Sink.Seq<int>(), Materializer).AwaitResult().Should().BeEmpty();
            }, Materializer);
        }

        [Fact]
        public void PartitionHub_must_remeber_completion_for_materialisations_after_completion()
        {
            var t = this.SourceProbe<NotUsed>().ToMaterialized(PartitionHub.Sink<NotUsed>((s, e) => 0, 0), Keep.Both)
                .Run(Materializer);
            var sourceProbe = t.Item1;
            var source = t.Item2;
            var sinkProbe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

            sourceProbe.SendComplete();

            sinkProbe.Request(1);
            sinkProbe.ExpectComplete();

            // Materialize a second time. There was a race here, where we managed to enqueue our Source registration just
            // immediately before the Hub shut down.
            var sink2Probe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

            sink2Probe.Request(1);
            sink2Probe.ExpectComplete();
        }

        [Fact]
        public void PartitionHub_must_properly_signal_error_to_consumer_arriving_after_producer_finished()
        {
            this.AssertAllStagesStopped(() =>
            {
                var failure = new TestException("Fail!");
                var source = Source.Failed<int>(failure).RunWith(PartitionHub.Sink<int>((s, e) => 0, 0), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                Thread.Sleep(50);

                Action a = () => source.RunWith(Sink.Seq<int>(), Materializer).AwaitResult();
                a.ShouldThrow<TestException>().WithMessage("Fail!");
            }, Materializer);
        }
    }

}
