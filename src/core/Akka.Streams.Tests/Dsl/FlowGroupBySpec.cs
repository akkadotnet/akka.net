//-----------------------------------------------------------------------
// <copyright file="FlowGroupBySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowGroupBySpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowGroupBySpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task GroupBy_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                await WithSubStreamsSupport(2, run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                {
                    var (sub1, probe1) = await StreamPuppet((await getSubFlow(1))
                        .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    await masterSubscriber.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                    await probe1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                    sub1.Request(1);
                    await probe1.ExpectNextAsync(1);
                    await probe1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                    var (sub2, probe2) = await StreamPuppet((await getSubFlow(0))
                        .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    
                    await probe2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                    sub2.Request(2);
                    await probe2.ExpectNextAsync(2);
                    // Important to request here on the OTHER stream because the buffer space is exactly one without the fan out box
                    sub1.Request(1);
                    await probe2.ExpectNextAsync(4);

                    await probe2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                    await probe1.ExpectNextAsync(3);

                    sub2.Request(1);
                    // Important to request here on the OTHER stream because the buffer space is exactly one without the fan out box
                    sub1.Request(1);
                    await probe2.ExpectNextAsync(6);
                    await probe2.ExpectCompleteAsync();

                    await probe1.ExpectNextAsync(5);
                    await probe1.ExpectCompleteAsync();
                    
                    masterSubscription.Request(1);
                    await masterSubscriber.ExpectCompleteAsync();
                });
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_work_in_normal_user_scenario()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = Source.From(new[] { "Aaa", "Abb", "Bcc", "Cdd", "Cee" })
                    .GroupBy(3, s => s.Substring(0, 1))
                    .Grouped(10)
                    .MergeSubstreams()
                    .Grouped(10);
                var task =
                    ((Source<IEnumerable<IEnumerable<string>>, NotUsed>)source).RunWith(
                        Sink.First<IEnumerable<IEnumerable<string>>>(), Materializer);

                await task.ShouldCompleteWithin(3.Seconds());
                task.Result.OrderBy(e => e.First())
                    .Should().BeEquivalentTo(new [] { new[] { "Aaa", "Abb" }, new[] { "Bcc" }, new[] { "Cdd", "Cee" } });
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_fail_when_key_function_returns_null()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = (Source<IEnumerable<string>, NotUsed>)Source.From(new[] { "Aaa", "Abb", "Bcc", "Cdd", "Cee" })
                    .GroupBy(3, s => s.StartsWith("A") ? null : s.Substring(0, 1))
                    .Grouped(10)
                    .MergeSubstreams();
                var down = source.RunWith(this.SinkProbe<IEnumerable<string>>(), Materializer);
                
                down.Request(1);
                var ex = await down.ExpectErrorAsync();
                ex.Message.Should().Contain("Key cannot be null");
                ex.Should().BeOfType<ArgumentNullException>();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_accept_cancelling_subStreams()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                await WithSubStreamsSupport(2, maxSubStream: 3, run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                {
                    var (s1, _) = await StreamPuppet((await getSubFlow(1)).RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    s1.Cancel();
                    
                    var (subscription, probe) = await StreamPuppet((await getSubFlow(0)).RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                    subscription.Request(2);
                    await probe.AsyncBuilder()
                        .ExpectNext(2)
                        .ExpectNext(4)
                        .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                        .ExecuteAsync();

                    subscription.Request(2);

                    await probe.AsyncBuilder()
                        .ExpectNext(6)
                        .ExpectComplete()
                        .ExecuteAsync();

                    masterSubscription.Request(1);
                    await masterSubscriber.ExpectCompleteAsync();
                });
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_accept_cancellation_of_master_stream_when_not_consume_anything()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var publisher = Source.FromPublisher(publisherProbe)
                    .GroupBy(2, x => x % 2)
                    .Lift(x => x % 2)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Cancel();
                await upstreamSubscription.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_work_with_empty_input_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisher = Source.From(new List<int>())
                    .GroupBy(2, x => x % 2)
                    .Lift(x => x % 2)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                await subscriber.AsyncBuilder()
                    .ExpectSubscriptionAndComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_abort_onError_from_upstream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var publisher = Source.FromPublisher(publisherProbe)
                    .GroupBy(2, x => x % 2)
                    .Lift(x => x % 2)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Request(100);

                var ex = new TestException("test");
                upstreamSubscription.SendError(ex);
                (await subscriber.ExpectErrorAsync()).Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_abort_onError_from_upstream_when_subStreams_are_running()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var publisher = Source.FromPublisher(publisherProbe)
                    .GroupBy(2, x => x % 2)
                    .Lift(x => x % 2)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Request(100);
                upstreamSubscription.SendNext(1);
                var subStream = (await subscriber.ExpectNextAsync()).Item2;
                var (sub, probe) = await StreamPuppet(subStream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                sub.Request(1);
                await probe.ExpectNextAsync(1);

                var ex = new TestException("test");
                upstreamSubscription.SendError(ex);

                (await probe.ExpectErrorAsync()).Should().Be(ex);
                (await subscriber.ExpectErrorAsync()).Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_fail_stream_when_GroupBy_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).GroupBy(2, i => i == 2 ? throw ex : i % 2)
                    .Lift(x => x % 2)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);

                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Request(100);

                upstreamSubscription.SendNext(1);

                var (_, subStream) = await subscriber.ExpectNextAsync();
                var (sub, probe) = await StreamPuppet(subStream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                sub.Request(1);
                await probe.ExpectNextAsync(1);

                upstreamSubscription.SendNext(2);
                (await subscriber.ExpectErrorAsync()).Should().Be(ex);
                (await probe.ExpectErrorAsync()).Should().Be(ex);
                await upstreamSubscription.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_resume_stream_when_GroupBy_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).GroupBy(2, i => i == 2 ? throw ex : i % 2)
                    .Lift(x => x % 2)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);

                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Request(100);

                upstreamSubscription.SendNext(1);

                var (_, subStream) = await subscriber.ExpectNextAsync();
                var (sub1, probe1) = await StreamPuppet(subStream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                sub1.Request(10);
                await probe1.ExpectNextAsync(1);

                upstreamSubscription.SendNext(2);
                upstreamSubscription.SendNext(4);

                var (_, subStream2) = (await subscriber.ExpectNextAsync());
                var (sub2, probe2) = await StreamPuppet(subStream2.RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                
                sub2.Request(10);
                await probe2.ExpectNextAsync(4);

                upstreamSubscription.SendNext(3);
                await probe1.ExpectNextAsync(3);

                upstreamSubscription.SendNext(6);
                await probe2.ExpectNextAsync(6);

                upstreamSubscription.SendComplete();
                await subscriber.ExpectCompleteAsync();
                await probe1.ExpectCompleteAsync();
                await probe2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_pass_along_early_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var up = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();

                var flowSubscriber =
                    Source.AsSubscriber<int>()
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .To(Sink.FromSubscriber(down)).Run(Materializer);

                var downstream = await down.ExpectSubscriptionAsync();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = await up.ExpectSubscriptionAsync();
                await upSub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_fail_when_exceeding_maxSubStreams()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var f = Flow.Create<int>().GroupBy(1, x => x % 2).PrefixAndTail(0).MergeSubstreams();
                var (up, down) = ((Flow<int, (IImmutableList<int>, Source<int, NotUsed>), NotUsed>)f)
                    .RunWith(this.SourceProbe<int>(), this.SinkProbe<(IImmutableList<int>, Source<int, NotUsed>)>(), Materializer);

                await down.RequestAsync(2);
                await up.SendNextAsync(1);
                var first = await down.ExpectNextAsync();
                var (sub, probe) = await StreamPuppet(first.Item2.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                sub.Request(1);
                await probe.ExpectNextAsync(1);

                await up.SendNextAsync(2);
                var ex = await down.ExpectErrorAsync();
                ex.Message.Should().Contain("too many substreams");
                (await probe.ExpectErrorAsync()).Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_resume_when_exceeding_maxSubStreams()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var f = Flow.Create<int>().GroupBy(0, x => x).MergeSubstreams();
                var (up, down) = ((Flow<int, int, NotUsed>)f)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(this.SourceProbe<int>(), this.SinkProbe<int>(), Materializer);

                await down.RequestAsync(1);
                await up.SendNextAsync(1);
                await down.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_emit_subscribe_before_completed()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var source = (Source<Source<int, NotUsed>, NotUsed>)Source.Single(0).GroupBy(1, _ => "all")
                    .PrefixAndTail(0)
                    .Select(t => t.Item2)
                    .ConcatSubstream();
                var futureGroupSource = source.RunWith(Sink.First<Source<int, NotUsed>>(), Materializer);
                await futureGroupSource.ShouldCompleteWithin(3.Seconds());
                var publisher = futureGroupSource.Result.RunWith(Sink.AsPublisher<int>(false), Materializer);
                
                var probe = this.CreateSubscriberProbe<int>();
                publisher.Subscribe(probe);
                var subscription = await probe.ExpectSubscriptionAsync();
                subscription.Request(1);
                
                await probe.AsyncBuilder()
                    .ExpectNext(0)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_work_under_fuzzing_stress_test()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<ByteString>();
                var subscriber = this.CreateManualSubscriberProbe<IEnumerable<byte>>();

                var firstGroup = (Source<IEnumerable<byte>, NotUsed>)Source.FromPublisher(publisherProbe)
                    .GroupBy(256, element => element[0])
                    .Select(b => b.Reverse())
                    .MergeSubstreams();
                var secondGroup = (Source<IEnumerable<byte>, NotUsed>)firstGroup.GroupBy(256, bytes => bytes.First())
                    .Select(b => b.Reverse())
                    .MergeSubstreams();
                var publisher = secondGroup.RunWith(Sink.AsPublisher<IEnumerable<byte>>(false), Materializer);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();

                downstreamSubscription.Request(300);
                for (var i = 1; i <= 300; i++)
                {
                    var byteString = RandomByteString(10);
                    await upstreamSubscription.ExpectRequestAsync();
                    upstreamSubscription.SendNext(byteString);
                    (await subscriber.ExpectNextAsync()).Should().BeEquivalentTo(byteString);
                }

                upstreamSubscription.SendComplete();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_work_if_pull_is_exercised_from_both_subStream_and_main()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstreamMaster = this.CreateSubscriberProbe<Source<int, NotUsed>>();

                Source.FromPublisher(upstream)
                    .Via(new GroupBy<int, bool>(2, element => element == 0))
                    .RunWith(Sink.FromSubscriber(downstreamMaster), Materializer);

                var subStream = this.CreateSubscriberProbe<int>();

                await downstreamMaster.RequestAsync(1);
                await upstream.SendNextAsync(1);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream), Materializer);

                // Read off first buffered element from sub source
                await subStream.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(1)

                    // Both will attempt to pull upstream
                    .Request(1)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExecuteAsync();
                
                await downstreamMaster.AsyncBuilder()
                    .Request(1)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExecuteAsync();

                // Cleanup, not part of the actual test
                await subStream.CancelAsync();
                await downstreamMaster.CancelAsync();
                await upstream.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_work_if_pull_is_exercised_from_multiple_subStreams_while_downstream_is_back_pressuring()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstreamMaster = this.CreateSubscriberProbe<Source<int, NotUsed>>();

                Source.FromPublisher(upstream)
                    .Via(new GroupBy<int, int>(10, element => element))
                    .RunWith(Sink.FromSubscriber(downstreamMaster), Materializer);

                var subStream1 = this.CreateSubscriberProbe<int>();
                await downstreamMaster.RequestAsync(1);
                await upstream.SendNextAsync(1);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream1), Materializer);

                var subStream2 = this.CreateSubscriberProbe<int>();
                await downstreamMaster.RequestAsync(1);
                await upstream.SendNextAsync(2);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream2), Materializer);

                await subStream1.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(1)
                    .ExecuteAsync();

                await subStream2.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(2)
                    .ExecuteAsync();

                // Both sub streams pull
                await subStream1.RequestAsync(1);
                await subStream2.RequestAsync(1);

                // Upstream sends new groups
                await upstream.AsyncBuilder()
                    .SendNext(3)
                    .SendNext(4)
                    .ExecuteAsync();

                var subStream3 = this.CreateSubscriberProbe<int>();
                var subStream4 = this.CreateSubscriberProbe<int>();
                await downstreamMaster.RequestAsync(1);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream3), Materializer);
                await downstreamMaster.RequestAsync(1);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream4), Materializer);

                await subStream3.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(3)
                    .ExecuteAsync();

                await subStream4.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(4)
                    .ExecuteAsync();

                // Cleanup, not part of the actual test
                await subStream1.CancelAsync();
                await subStream2.CancelAsync();
                await subStream3.CancelAsync();
                await subStream4.CancelAsync();
                await downstreamMaster.CancelAsync();
                await upstream.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_allow_to_recreate_an_already_closed_subStream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var f = Flow.Create<int>()
                    .GroupBy(2, x => x, allowClosedSubstreamRecreation: true)
                    .Take(1) // close the subStream after 1 element
                    .MergeSubstreams();

                var (up, down) = ((Flow<int, int, NotUsed>)f)
                    .RunWith(this.SourceProbe<int>(), this.SinkProbe<int>(), Materializer);

                await down.RequestAsync(4);

                // Creates and closes subStream "1"
                await up.SendNextAsync(1);
                await down.ExpectNextAsync(1);

                // Creates and closes subStream "2"
                await up.SendNextAsync(2);
                await down.ExpectNextAsync(2);

                // Recreates and closes subStream "1" twice
                await up.SendNextAsync(1);
                await down.ExpectNextAsync(1);
                await up.SendNextAsync(1);
                await down.ExpectNextAsync(1);

                // Cleanup, not part of the actual test
                await up.SendCompleteAsync();
                await down.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_cancel_if_downstream_has_cancelled_and_all_subStreams_cancel()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstreamMaster = this.CreateSubscriberProbe<Source<int, NotUsed>>();

                Source.FromPublisher(upstream)
                    .Via(new GroupBy<int, int>(10, element => element))
                    .RunWith(Sink.FromSubscriber(downstreamMaster), Materializer);

                var subStream1 = this.CreateSubscriberProbe<int>();
                await downstreamMaster.RequestAsync(1);
                await upstream.SendNextAsync(1);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream1), Materializer);

                var subStream2 = this.CreateSubscriberProbe<int>();
                await downstreamMaster.RequestAsync(1);
                await upstream.SendNextAsync(2);
                (await downstreamMaster.ExpectNextAsync()).RunWith(Sink.FromSubscriber(subStream2), Materializer);

                // Cancel downstream
                await downstreamMaster.CancelAsync();

                // Both subStreams still work
                await subStream1.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(1)
                    .ExecuteAsync();

                await subStream2.AsyncBuilder()
                    .Request(1)
                    .ExpectNext(2)
                    .ExecuteAsync();

                // New keys are ignored
                await upstream.AsyncBuilder()
                    .SendNext(3)
                    .SendNext(4)
                    .ExecuteAsync();

                // Cancel all subStreams
                await subStream1.CancelAsync();
                await subStream2.CancelAsync();

                // Upstream gets cancelled
                await upstream.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_must_work_with_random_demand()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            var materializer = Sys.Materializer(settings);
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var props = new RandomDemandProperties
                {
                    Kit = this
                };
                Enumerable.Range(0, 100)
                    .ToList()
                    .ForEach(_ => props.Probes.Add(new TaskCompletionSource<TestSubscriber.Probe<ByteString>>()));

                var map = new Dictionary<int, SubFlowState>();

                var publisherProbe = this.CreateManualPublisherProbe<ByteString>();
                var probeShape = new SinkShape<ByteString>(new Inlet<ByteString>("ProbeSink.in"));
                var probeSink = new ProbeSink(probeShape, props, Attributes.None);
                Source.FromPublisher(publisherProbe)
                    .GroupBy(100, element => Math.Abs(element[0] % 100))
                    .To(new Sink<ByteString, TestSubscriber.Probe<ByteString>>(probeSink))
                    .Run(materializer);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();

                foreach (var _ in Enumerable.Range(1, 400))
                {
                    var byteString = RandomByteString(10);
                    var index = Math.Abs(byteString[0] % 100);

                    await upstreamSubscription.ExpectRequestAsync();
                    upstreamSubscription.SendNext(byteString);

                    if (map.TryGetValue(index, out var state))
                    {
                        if (state.FirstElement != null) //first element in subFlow 
                        {
                            if (!state.HasDemand)
                                props.BlockingNextElement = byteString;
                            await RandomDemandAsync(map, props);
                        }
                        else if (state.HasDemand)
                        {
                            if (props.BlockingNextElement == null)
                            {
                                (await state.Probe.ExpectNextAsync()).Should().BeEquivalentTo(byteString);
                                map[index] = new SubFlowState(state.Probe, false, null);
                                await RandomDemandAsync(map, props);
                            }
                            else
                                throw new AssertActualExpectedException(true, false, "state.HasDemand INVALID STATE");
                        }
                        else
                        {
                            props.BlockingNextElement = byteString;
                            await RandomDemandAsync(map, props);
                        }
                    }
                    else
                    {
                        var probe = await props.Probes[props.ProbesReaderTop].Task.ShouldCompleteWithin(3.Seconds());
                        props.ProbesReaderTop++;
                        map[index] = new SubFlowState(probe, false, byteString);
                        //stream automatically requests next element 
                    }
                }
                upstreamSubscription.SendComplete();
            }, materializer);
        }

        private static async Task<(ISubscription subscription, TestSubscriber.ManualProbe<int> probe)> StreamPuppet(IPublisher<int> p, TestKitBase kit)
        {
            var probe = kit.CreateManualSubscriberProbe<int>();
            p.Subscribe(probe);
            var subscription = await probe.ExpectSubscriptionAsync();
            return (subscription, probe);
        }

        private async Task WithSubStreamsSupport(
            int groupCount = 2,
            int elementCount = 6,
            int maxSubStream = -1,
            Func<TestSubscriber.ManualProbe<(int, Source<int, NotUsed>)>, ISubscription, Func<int, Task<Source<int, NotUsed>>>, Task> run = null)
        {
            var source = Source.From(Enumerable.Range(1, elementCount)).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var max = maxSubStream > 0 ? maxSubStream : groupCount;
            var groupStream =
                Source.FromPublisher(source)
                    .GroupBy(max, x => x % groupCount)
                    .Lift(x => x % groupCount)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
            var masterSubscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();

            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = await masterSubscriber.ExpectSubscriptionAsync();

            if(run != null)
                await run(masterSubscriber, masterSubscription, async expectedKey =>
                {
                    masterSubscription.Request(1);
                    var (key, src) = await masterSubscriber.ExpectNextAsync();
                    key.Should().Be(expectedKey);
                    return src;
                });
        }

        private static ByteString RandomByteString(int size)
        {
            var a = new byte[size];
            ThreadLocalRandom.Current.NextBytes(a);
            return ByteString.FromBytes(a);
        }

        private sealed class SubFlowState
        {
            public SubFlowState(TestSubscriber.Probe<ByteString> probe, bool hasDemand, ByteString firstElement)
            {
                Probe = probe;
                HasDemand = hasDemand;
                FirstElement = firstElement;
            }

            public TestSubscriber.Probe<ByteString> Probe { get; }

            public bool HasDemand { get; }

            public ByteString FirstElement { get; }
        }

        private sealed class ProbeSink : SinkModule<ByteString, TestSubscriber.Probe<ByteString>>
        {
            private readonly RandomDemandProperties _properties;

            public ProbeSink(SinkShape<ByteString> shape, RandomDemandProperties properties, Attributes attributes) : base(shape)
            {
                _properties = properties;
                Attributes = attributes;
            }

            public override Attributes Attributes { get; }

            public override IModule WithAttributes(Attributes attributes)
            {
                return new ProbeSink(AmendShape(attributes), _properties, attributes);
            }

            protected override SinkModule<ByteString, TestSubscriber.Probe<ByteString>> NewInstance(SinkShape<ByteString> shape)
            {
                return new ProbeSink(shape, _properties, Attributes);
            }

            public override object Create(MaterializationContext context, out TestSubscriber.Probe<ByteString> materializer)
            {
                var promise = _properties.Probes[_properties.ProbesWriterTop];
                var probe = TestSubscriber.CreateSubscriberProbe<ByteString>(_properties.Kit);
                promise.SetResult(probe);
                _properties.ProbesWriterTop++;
                materializer = probe;
                return probe;
            }
        }

        private sealed class RandomDemandProperties
        {
            public TestKitBase Kit { get; set; }

            public int ProbesWriterTop { get; set; }

            public int ProbesReaderTop { get; set; }

            public List<TaskCompletionSource<TestSubscriber.Probe<ByteString>>> Probes { get; } =
                new List<TaskCompletionSource<TestSubscriber.Probe<ByteString>>>(100);

            public ByteString BlockingNextElement { get; set; }
        }

        private async Task RandomDemandAsync(Dictionary<int, SubFlowState> map, RandomDemandProperties props)
        {
            while (true)
            {
                var nextIndex = ThreadLocalRandom.Current.Next(0, map.Count);
                var key = map.Keys.ToArray()[nextIndex];
                if (!map[key].HasDemand)
                {
                    var state = map[key];
                    map[key] = new SubFlowState(state.Probe, true, state.FirstElement);

                    await state.Probe.RequestAsync(1);

                    // need to verify elements that are first element in subFlow or is in nextElement buffer before 
                    // pushing next element from upstream 
                    if (state.FirstElement != null)
                    {
                        (await state.Probe.ExpectNextAsync()).Should().BeEquivalentTo(state.FirstElement);
                        map[key] = new SubFlowState(state.Probe, false, null);
                    }
                    else if (props.BlockingNextElement != null && Math.Abs(props.BlockingNextElement[0] % 100) == key)
                    {
                        (await state.Probe.ExpectNextAsync()).Should().BeEquivalentTo(props.BlockingNextElement);
                        props.BlockingNextElement = null;
                        map[key] = new SubFlowState(state.Probe, false, null);
                    }
                    else if (props.BlockingNextElement == null)
                        break;
                }
            }

        }
    }
}
