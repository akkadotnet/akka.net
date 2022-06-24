//-----------------------------------------------------------------------
// <copyright file="HubSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Akka.Actor;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using Akka.Util.Internal;
using FluentAssertions.Extensions;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class HubSpec : AkkaSpec
    {
        public HubSpec(ITestOutputHelper helper) : base(FullDebugConfig, helper)
        {
            Materializer = Sys.Materializer();
        }

        private ActorMaterializer Materializer { get; }

        [Fact]
        public async Task MergeHub_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16).Take(20).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                Source.From(Enumerable.Range(1, 10)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(11, 10)).RunWith(sink, Materializer);

                var result = await task.ShouldCompleteWithin(3.Seconds());
                result.OrderBy(x => x).Should().BeEquivalentTo(Enumerable.Range(1, 20));
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_notify_new_producers_if_consumer_cancels_before_first_producer()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var sink = Sink.Cancelled<int>().RunWith(MergeHub.Source<int>(16), Materializer);
                var upstream = this.CreatePublisherProbe<int>();

                Source.FromPublisher(upstream).RunWith(sink, Materializer);

                await upstream.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_notify_existing_producers_if_consumer_cancels_after_a_few_elements()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16).Take(5).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var upstream = this.CreatePublisherProbe<int>();

                Source.FromPublisher(upstream).RunWith(sink, Materializer);
                await upstream.AsyncBuilder()
                    .SendNext(Enumerable.Range(1, 5))
                    .ExpectCancellation()
                    .ExecuteAsync();
                
                var result = await task.ShouldCompleteWithin(3.Seconds());
                result.Should().BeEquivalentTo(Enumerable.Range(1, 5));
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_notify_new_producers_if_consumer_cancels_after_a_few_elements()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16).Take(5).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);
                var upstream1 = this.CreatePublisherProbe<int>();
                var upstream2 = this.CreatePublisherProbe<int>();

                Source.FromPublisher(upstream1).RunWith(sink, Materializer);
                await upstream1.AsyncBuilder()
                    .SendNext(Enumerable.Range(1, 5))
                    .ExpectCancellation()
                    .ExecuteAsync();

                var result = await task.ShouldCompleteWithin(3.Seconds());
                result.Should().BeEquivalentTo(Enumerable.Range(1, 5));

                Source.FromPublisher(upstream2).RunWith(sink, Materializer);
                await upstream2.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_respect_the_buffer_size()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var downstream = this.CreateManualSubscriberProbe<int>();
                var sink = Sink.FromSubscriber(downstream).RunWith(MergeHub.Source<int>(3), Materializer);

                Source.From(Enumerable.Range(1, 10)).Select(i =>
                {
                    TestActor.Tell(i);
                    return i;
                }).RunWith(sink, Materializer);

                var sub = await downstream.ExpectSubscriptionAsync();
                sub.Request(1);

                // Demand starts from 3
                await ExpectMsgAsync(1);
                await ExpectMsgAsync(2);
                await ExpectMsgAsync(3);
                await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                // One element consumed (it was requested), demand 0 remains at producer
                await downstream.ExpectNextAsync(1);

                // Requesting next element, results in next element to be consumed.
                sub.Request(1);
                await downstream.ExpectNextAsync(2);

                // Two elements have been consumed, so threshold of 2 is reached, additional 2 demand is dispatched.
                // There is 2 demand at the producer now

                await ExpectMsgAsync(4);
                await ExpectMsgAsync(5);
                await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                // Two additional elements have been sent:
                // - 3, 4, 5 are pending
                // - demand is 0 at the producer
                // - next demand batch is after two elements have been consumed again

                // Requesting next gives the next element
                // Demand is not yet refreshed for the producer as there is one more element until threshold is met
                sub.Request(1);
                await downstream.ExpectNextAsync(3);

                await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                sub.Request(1);
                await downstream.ExpectNextAsync(4);
                await ExpectMsgAsync(6);
                await ExpectMsgAsync(7);

                sub.Cancel();
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_work_with_long_streams()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16).Take(20000).ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);

                Source.From(Enumerable.Range(1, 10000)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(10001, 10000)).RunWith(sink, Materializer);

                var result = await task.ShouldCompleteWithin(3.Seconds());
                result.OrderBy(x => x).Should().BeEquivalentTo(Enumerable.Range(1, 20000));
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_work_with_long_streams_when_buffer_size_is_1()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, probe) = MergeHub.Source<int>(1).Take(20000)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                Source.From(Enumerable.Range(1, 10000)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(10001, 10000)).RunWith(sink, Materializer);

                await probe.RequestAsync(int.MaxValue);
                var result = await probe.ExpectNextNAsync(20000, 3.Seconds()).ToListAsync();
                result.OrderBy(x => x).Should().BeEquivalentTo(Enumerable.Range(1, 20000));
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_work_with_long_streams_when_consumer_is_slower()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16)
                    .Take(2000)
                    .Throttle(10, TimeSpan.FromMilliseconds(1), 200, ThrottleMode.Shaping)
                    .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);

                Source.From(Enumerable.Range(1, 1000)).RunWith(sink, Materializer);
                Source.From(Enumerable.Range(1001, 1000)).RunWith(sink, Materializer);

                var result = await task.ShouldCompleteWithin(3.Seconds());
                result.OrderBy(x => x).Should().BeEquivalentTo(Enumerable.Range(1, 2000));

            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_work_with_long_streams_if_one_of_the_producers_is_slower()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16).Take(2000).ToMaterialized(Sink.Seq<int>(), Keep.Both)
                    .Run(Materializer);

                Source.From(Enumerable.Range(1, 1000))
                    .Throttle(10, TimeSpan.FromMilliseconds(1), 100, ThrottleMode.Shaping)
                    .RunWith(sink, Materializer);
                Source.From(Enumerable.Range(1001, 1000)).RunWith(sink, Materializer);

                var result = await task.ShouldCompleteWithin(3.Seconds());
                result.OrderBy(x => x).Should().BeEquivalentTo(Enumerable.Range(1, 2000));
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_work_with_different_producers_separated_over_time()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var downstream = this.CreateSubscriberProbe<IEnumerable<int>>();
                var sink = MergeHub.Source<int>(16)
                    .Grouped(100)
                    .ToMaterialized(Sink.FromSubscriber(downstream), Keep.Left)
                    .Run(Materializer);
                Source.From(Enumerable.Range(1, 100)).RunWith(sink, Materializer);
                (await downstream.RequestNextAsync()).Should().BeEquivalentTo(Enumerable.Range(1, 100));

                Source.From(Enumerable.Range(101, 100)).RunWith(sink, Materializer);
                (await downstream.RequestNextAsync()).Should().BeEquivalentTo(Enumerable.Range(101, 100));

                await downstream.CancelAsync();
            }, Materializer);
        }

        [Fact]
        public async Task MergeHub_must_keep_working_even_if_one_of_the_producers_fail()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sink, task) = MergeHub.Source<int>(16).Take(10).ToMaterialized(Sink.Seq<int>(), Keep.Both).Run(Materializer);

                await EventFilter.Error(contains: "Upstream producer failed with exception").ExpectOneAsync(async () =>
                {
                    Source.Failed<int>(new TestException("failing")).RunWith(sink, Materializer);
                    Source.From(Enumerable.Range(1, 10)).RunWith(sink, Materializer);
                    var result = await task.ShouldCompleteWithin(3.Seconds());
                    result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
                });

            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.From(Enumerable.Range(1, 10)).RunWith(BroadcastHub.Sink<int>(8), Materializer);
                var result = await source.RunWith(Sink.Seq<int>(), Materializer).ShouldCompleteWithin(3.Seconds());
                result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task BroadcastHub_must_send_the_same_elements_to_consumers_attaching_around_the_same_time()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var (firstElement, source) = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(500);
                
                firstElement.SetResult(1);
                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task BroadcastHub_must_send_the_same_prefix_to_consumers_attaching_around_the_same_time_if_one_cancels_earlier()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var other = Source.From(Enumerable.Range(2, 19))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var (firstElement, source) = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.Take(10).RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(500);
                
                firstElement.SetResult(1);
                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 20));
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_ensure_that_subsequent_consumers_see_subsequent_elements_without_gap()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.From(Enumerable.Range(1, 20)).RunWith(BroadcastHub.Sink<int>(8), Materializer);
                (await source.Take(10).RunWith(Sink.Seq<int>(), Materializer).ShouldCompleteWithin(3.Seconds()))
                    .Should().BeEquivalentTo(Enumerable.Range(1, 10));
                (await source.Take(10).RunWith(Sink.Seq<int>(), Materializer).ShouldCompleteWithin(3.Seconds()))
                    .Should().BeEquivalentTo(Enumerable.Range(11, 10));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task BroadcastHub_must_send_the_same_elements_to_consumers_of_different_speed_attaching_around_the_same_time()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var (firstElement, source) = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);

                var f1 = source.Throttle(1, TimeSpan.FromMilliseconds(10), 3, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(500);
                
                firstElement.SetResult(1);
                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task BroadcastHub_must_send_the_same_elements_to_consumers_of_attaching_around_the_same_time_if_the_producer_is_slow()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var (firstElement, source) = Source.Maybe<int>()
                    .Concat(other)
                    .Throttle(1, TimeSpan.FromMilliseconds(10), 3, ThrottleMode.Shaping)
                    .ToMaterialized(BroadcastHub.Sink<int>(8), Keep.Both)
                    .Run(Materializer);

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(500);
                
                firstElement.SetResult(1);
                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task BroadcastHub_must_ensure_that_from_two_different_speed_consumers_the_slower_controls_the_rate()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var other = Source.From(Enumerable.Range(2, 19))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var (firstElement, source) = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(1), Keep.Both)
                    .Run(Materializer);

                var f1 = source
                    .Throttle(1, TimeSpan.FromMilliseconds(10), 1, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);
                // Second cannot be overwhelmed since the first one throttles the overall rate, and second allows a higher rate
                var f2 = source
                    .Throttle(10, TimeSpan.FromMilliseconds(10), 8, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(500);
                
                firstElement.SetResult(1);
                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 20));
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 20));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task BroadcastHub_must_send_the_same_elements_to_consumers_attaching_around_the_same_time_with_a_buffer_size_of_one()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var other = Source.From(Enumerable.Range(2, 9))
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null);
                var (firstElement, source) = Source.Maybe<int>()
                    .Concat(other)
                    .ToMaterialized(BroadcastHub.Sink<int>(1), Keep.Both)
                    .Run(Materializer);

                var f1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var f2 = source.RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(500);
                
                firstElement.SetResult(1);
                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_be_able_to_implement_a_keep_dropping_if_unsubscribed_policy_with_a_simple_SinkIgnore()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var killSwitch = KillSwitches.Shared("test-switch");
                var source = Source.From(Enumerable.Range(1, int.MaxValue))
                    .Via(killSwitch.Flow<int>())
                    .RunWith(BroadcastHub.Sink<int>(8), Materializer);

                // Now the Hub "drops" elements until we attach a new consumer (Source.ignore consumes as fast as possible)
                var ignoredTask = source.RunWith(Sink.Ignore<int>(), Materializer);

                // Now we attached a subscriber which will block the Sink.ignore to "take away" and drop elements anymore,
                // turning the BroadcastHub to a normal non-dropping mode
                var downstream = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream), Materializer);

                var first = await downstream.AsyncBuilder()
                    .Request(1)
                    .ExpectNextAsync();

                foreach (var i in Enumerable.Range(first + 1, 9))
                {
                    await downstream.AsyncBuilder()
                        .Request(1)
                        .ExpectNext(i)
                        .ExecuteAsync();
                }

                await downstream.CancelAsync();
                killSwitch.Shutdown();
                await ignoredTask.ShouldCompleteWithin(3.Seconds());
            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_remember_completion_for_materialization_after_completion()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sourceProbe, source) = this.SourceProbe<NotUsed>()
                    .ToMaterialized(BroadcastHub.Sink<NotUsed>(), Keep.Both)
                    .Run(Materializer);
                var sinkProbe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

                sourceProbe.SendComplete();

                await sinkProbe.AsyncBuilder()
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();

                // Materialize a second time. There was a race here, where we managed to enqueue our Source registration just
                // immediately before the Hub shut down.
                var sink2Probe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

                await sink2Probe.AsyncBuilder()
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_properly_signal_error_to_consumers()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var source = Source.FromPublisher(upstream).RunWith(BroadcastHub.Sink<int>(8), Materializer);

                var downstream1 = this.CreateSubscriberProbe<int>();
                var downstream2 = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream1), Materializer);
                source.RunWith(Sink.FromSubscriber(downstream2), Materializer);

                await downstream1.RequestAsync(4);
                await downstream2.RequestAsync(8);

                await upstream.AsyncBuilder()
                    .SendNext(Enumerable.Range(1, 8))
                    .ExecuteAsync();

                await downstream1.AsyncBuilder()
                    .ExpectNext(1, 2, 3, 4)
                    .ExpectNoMsg(100.Milliseconds())
                    .ExecuteAsync();
                await downstream2.AsyncBuilder()
                    .ExpectNext(1, 2, 3, 4, 5, 6, 7, 8)
                    .ExpectNoMsg(100.Milliseconds())
                    .ExecuteAsync();

                await upstream.SendErrorAsync(new TestException("failed"));
                (await downstream1.ExpectErrorAsync()).Message.Should().Be("failed");
                (await downstream2.ExpectErrorAsync()).Message.Should().Be("failed");
            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_properly_signal_completion_to_consumers_arriving_after_producer_finished()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.Empty<int>().RunWith(BroadcastHub.Sink<int>(8), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                await Task.Delay(50);

                (await source.RunWith(Sink.Seq<int>(), Materializer).ShouldCompleteWithin(3.Seconds()))
                    .Should().BeEmpty();
            }, Materializer);
        }

        [Fact]
        public async Task BroadcastHub_must_properly_signal_error_to_consumers_arriving_after_producer_finished()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.Failed<int>(new TestException("Fail!"))
                    .RunWith(BroadcastHub.Sink<int>(8), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                await Task.Delay(50);

                await Awaiting(async () =>
                {
                    await source.RunWith(Sink.Seq<int>(), Materializer);
                }).Should().ThrowAsync<TestException>().ShouldCompleteWithin(3.Seconds());
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_work_in_the_happy_case_with_one_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var items = Enumerable.Range(1, 10).ToList();
                var source = Source.From(items)
                    .RunWith(PartitionHub.Sink<int>((size, e) => 0, 0, 8), Materializer);
                var result = await source.RunWith(Sink.Seq<int>(), Materializer).ShouldCompleteWithin(3.Seconds());
                result.Should().BeEquivalentTo(items);
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_work_in_the_happy_case_with_two_streams()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.From(Enumerable.Range(0, 10))
                    .RunWith(PartitionHub.Sink<int>((size, e) => e % size, 2, 8), Materializer);
                
                var result1 = source.RunWith(Sink.Seq<int>(), Materializer);
                // it should not start publishing until startAfterNrOfConsumers = 2
                await Task.Delay(50);
                var result2 = source.RunWith(Sink.Seq<int>(), Materializer);
                
                (await result1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(new[] { 0, 2, 4, 6, 8 });
                (await result2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(new[] { 1, 3, 5, 7, 9 });
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_be_able_to_be_used_as_round_robin_router()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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
                
                (await result1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(new[] { 1, 3, 5, 7, 9 });
                (await result2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(new[] { 0, 2, 4, 6, 8 });
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_be_able_to_be_used_as_sticky_session_round_robin_router()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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
                
                (await result1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo("usr-2");
                (await result2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo("usr-1", "usr-1", "usr-3");
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_be_able_to_use_as_fastest_consumer_router()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var items = Enumerable.Range(0, 999).ToList();
                var source = Source.From(items)
                    .RunWith(
                        PartitionHub.StatefulSink<int>(() => ((info, i) => info.ConsumerIds.Min(info.QueueSize)), 2, 4),
                        Materializer);
                var result1 = source.RunWith(Sink.Seq<int>(), Materializer);
                var result2 = source.Throttle(10, TimeSpan.FromMilliseconds(100), 10, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);

                var count1 = (await result1.ShouldCompleteWithin(3.Seconds())).Count;
                var count2 = (await result2.ShouldCompleteWithin(3.Seconds())).Count;
                count1.ShouldBeGreaterThan(count2);
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_route_evenly()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (testSource, hub) = this.SourceProbe<int>()
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => e % size, 2, 8), Keep.Both)
                    .Run(Materializer);

                var probe0 = hub.RunWith(this.SinkProbe<int>(), Materializer);
                var probe1 = hub.RunWith(this.SinkProbe<int>(), Materializer);

                await probe0.RequestAsync(3);
                await probe1.RequestAsync(10);
                await testSource.SendNextAsync(0);
                await probe0.ExpectNextAsync(0);
                await testSource.SendNextAsync(1);
                await probe1.ExpectNextAsync(1);

                await testSource.SendNextAsync(2);
                await testSource.SendNextAsync(3);
                await testSource.SendNextAsync(4);
                await probe0.ExpectNextAsync(2);
                await probe1.ExpectNextAsync(3);
                await probe0.ExpectNextAsync(4);

                // probe1 has not requested more
                await testSource.SendNextAsync(5);
                await testSource.SendNextAsync(6);
                await testSource.SendNextAsync(7);
                await probe1.ExpectNextAsync(5);
                await probe1.ExpectNextAsync(7);
                await probe0.AsyncBuilder()
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(50))
                    .Request(10)
                    .ExpectNext(6)
                    .ExecuteAsync();

                await testSource.SendCompleteAsync();
                await probe0.ExpectCompleteAsync();
                await probe1.ExpectCompleteAsync();

            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_route_unevenly()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (testSource, hub) = this.SourceProbe<int>()
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => (e % 3) % 2, 2, 8), Keep.Both)
                    .Run(Materializer);

                var probe0 = hub.RunWith(this.SinkProbe<int>(), Materializer);
                var probe1 = hub.RunWith(this.SinkProbe<int>(), Materializer);

                // (_ % 3) % 2
                // 0 => 0
                // 1 => 1
                // 2 => 0
                // 3 => 0
                // 4 => 1

                await probe0.RequestAsync(10);
                await probe1.RequestAsync(10);
                await testSource.SendNextAsync(0);
                await probe0.ExpectNextAsync(0);
                await testSource.SendNextAsync(1);
                await probe1.ExpectNextAsync(1);
                await testSource.SendNextAsync(2);
                await probe0.ExpectNextAsync(2);
                await testSource.SendNextAsync(3);
                await probe0.ExpectNextAsync(3);
                await testSource.SendNextAsync(4);
                await probe1.ExpectNextAsync(4);

                await testSource.SendCompleteAsync();
                await probe0.ExpectCompleteAsync();
                await probe1.ExpectCompleteAsync();

            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_backpressure()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (testSource, hub) = this.SourceProbe<int>()
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => 0, 2, 4), Keep.Both)
                    .Run(Materializer);

                var probe0 = hub.RunWith(this.SinkProbe<int>(), Materializer);
                var probe1 = hub.RunWith(this.SinkProbe<int>(), Materializer);

                await probe0.RequestAsync(10);
                await probe1.RequestAsync(10);
                await testSource.SendNextAsync(0);
                await probe0.ExpectNextAsync(0);
                await testSource.SendNextAsync(1);
                await probe0.ExpectNextAsync(1);
                await testSource.SendNextAsync(2);
                await probe0.ExpectNextAsync(2);
                await testSource.SendNextAsync(3);
                await probe0.ExpectNextAsync(3);
                await testSource.SendNextAsync(4);
                await probe0.ExpectNextAsync(4);

                await testSource.SendCompleteAsync();
                await probe0.ExpectCompleteAsync();
                await probe1.ExpectCompleteAsync();

            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task PartitionHub_must_ensure_that_from_two_different_speed_consumers_the_slower_controls_the_rate()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (firstElement, source) = Source.Maybe<int>().ConcatMaterialized(Source.From(Enumerable.Range(1, 19)), Keep.Left)
                    .ToMaterialized(PartitionHub.Sink<int>((size, e) => e % size, 2, 1), Keep.Both)
                    .Run(Materializer);

                var f1 = source.Throttle(1, TimeSpan.FromMilliseconds(10), 1, ThrottleMode.Shaping)
                    .RunWith(Sink.Seq<int>(), Materializer);

                // Second cannot be overwhelmed since the first one throttles the overall rate, and second allows a higher rate
                var f2 = source.Throttle(10, TimeSpan.FromMilliseconds(10), 8, ThrottleMode.Enforcing)
                    .RunWith(Sink.Seq<int>(), Materializer);

                // Ensure subscription of Sinks. This is racy but there is no event we can hook into here.
                await Task.Delay(100);
                // on the jvm Some 0 is used, unfortunately haven't we used Option<T> for the Maybe source
                // and therefore firstElement.SetResult(0) will complete the source without pushing an element
                // since 0 is the default value for int and if you set the result to default(T) it will ignore
                // the element and complete the source. We should probably fix this in the feature. 
                firstElement.SetResult(50);

                var expectationF1 = Enumerable.Range(1, 18).Where(v => v % 2 == 0).ToList();
                expectationF1.Insert(0, 50);

                (await f1.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(expectationF1);
                (await f2.ShouldCompleteWithin(3.Seconds())).Should().BeEquivalentTo(Enumerable.Range(1, 19).Where(v => v % 2 != 0));
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_properly_signal_error_to_consumer()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var source = Source.FromPublisher(upstream)
                    .RunWith(PartitionHub.Sink<int>((s, e) => e % s, 2, 8), Materializer);

                var downstream1 = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream1), Materializer);
                var downstream2 = this.CreateSubscriberProbe<int>();
                source.RunWith(Sink.FromSubscriber(downstream2), Materializer);

                await downstream1.RequestAsync(4);
                await downstream2.RequestAsync(8);

                Enumerable.Range(0, 16).ForEach(i => upstream.SendNext(i));

                await downstream1.AsyncBuilder()
                    .ExpectNext(0, 2, 4, 6)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExecuteAsync();
                await downstream2.AsyncBuilder()
                    .ExpectNext(1, 3, 5, 7, 9, 11, 13, 15)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExecuteAsync();

                var failure = new TestException("Failed");
                await upstream.SendErrorAsync(failure);

                (await downstream1.ExpectErrorAsync()).Should().Be(failure);
                (await downstream2.ExpectErrorAsync()).Should().Be(failure);
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_properly_signal_completion_to_consumers_arriving_after_producer_finished()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.Empty<int>().RunWith(PartitionHub.Sink<int>((s, e) => e % s, 0), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                await Task.Delay(50);

                (await source.RunWith(Sink.Seq<int>(), Materializer).ShouldCompleteWithin(3.Seconds()))
                    .Should().BeEmpty();
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_remember_completion_for_materialization_after_completion()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (sourceProbe, source) = this.SourceProbe<NotUsed>().ToMaterialized(PartitionHub.Sink<NotUsed>((s, e) => 0, 0), Keep.Both)
                    .Run(Materializer);
                var sinkProbe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

                await sourceProbe.SendCompleteAsync();

                await sinkProbe.AsyncBuilder()
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();

                // Materialize a second time. There was a race here, where we managed to enqueue our Source registration just
                // immediately before the Hub shut down.
                var sink2Probe = source.RunWith(this.SinkProbe<NotUsed>(), Materializer);

                await sink2Probe.AsyncBuilder()
                    .Request(1)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task PartitionHub_must_properly_signal_error_to_consumer_arriving_after_producer_finished()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failure = new TestException("Fail!");
                var source = Source.Failed<int>(failure).RunWith(PartitionHub.Sink<int>((s, e) => 0, 0), Materializer);
                // Wait enough so the Hub gets the completion. This is racy, but this is fine because both
                // cases should work in the end
                await Task.Delay(50);

                await Awaiting(async () =>
                {
                    await source.RunWith(Sink.Seq<int>(), Materializer);
                }).Should().ThrowAsync<TestException>().WithMessage("Fail!").ShouldCompleteWithin(3.Seconds());
            }, Materializer);
        }
    }

}
