﻿//-----------------------------------------------------------------------
// <copyright file="FlowSplitWhenSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Nito.AsyncEx;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

#nullable enable
namespace Akka.Streams.Tests.Dsl
{
    public class FlowSplitWhenSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSplitWhenSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings =
                ActorMaterializerSettings.Create(Sys)
                    .WithInputBuffer(2, 2)
                    .WithSubscriptionTimeoutSettings(
                        new StreamSubscriptionTimeoutSettings(
                            StreamSubscriptionTimeoutTerminationMode.CancelTermination, TimeSpan.FromSeconds(1)));
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private sealed class StreamPuppet
        {
            private readonly TestSubscriber.ManualProbe<int> _probe;
            private readonly ISubscription _subscription;

            public StreamPuppet(IPublisher<int> p, TestKitBase kit)
            {
                _probe = kit.CreateManualSubscriberProbe<int>();
                p.Subscribe(_probe);
                _subscription = _probe.ExpectSubscription();
            }

            public void Request(int demand) => _subscription.Request(demand);

            public async Task ExpectNextAsync(int element) => await _probe.ExpectNextAsync(element);
            
            public async Task ExpectNoMsgAsync(TimeSpan max) => await _probe.ExpectNoMsgAsync(max);
            
            public async Task ExpectCompleteAsync() => await _probe.ExpectCompleteAsync();
            
            public async Task ExpectErrorAsync(Exception ex) => (await _probe.ExpectErrorAsync()).Should().Be(ex);

            public void Cancel() => _subscription.Cancel();
        }

        private async Task WithSubstreamsSupportAsync(
            int splitWhen = 3,
            int elementCount = 6,
            SubstreamCancelStrategy substreamCancelStrategy = SubstreamCancelStrategy.Drain,
            Func<TestSubscriber.ManualProbe<Source<int, NotUsed>>, ISubscription, Func<Task<Source<int, NotUsed>>>, Task>? run = null)
        {
            var source = Source.From(Enumerable.Range(1, elementCount));
            var groupStream =
                source.SplitWhen(substreamCancelStrategy, i => i == splitWhen)
                    .Lift()
                    .RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);
            var masterSubscriber = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();
            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = await masterSubscriber.ExpectSubscriptionAsync();

            run?.Invoke(masterSubscriber, masterSubscription, async () =>
            {
                masterSubscription.Request(1);
                return await masterSubscriber.ExpectNextAsync();
            });
        }

        [Fact]
        public async Task SplitWhen_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                await WithSubstreamsSupportAsync(elementCount: 4,
                    run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet((await getSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                        await masterSubscriber.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                        s1.Request(2);
                        await s1.ExpectNextAsync(1);
                        await s1.ExpectNextAsync(2);
                        s1.Request(1);
                        await s1.ExpectCompleteAsync();

                        var s2 = new StreamPuppet((await getSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                        await masterSubscriber.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                        s2.Request(1);
                        await s2.ExpectNextAsync(3);
                        await s2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                        s2.Request(1);
                        await s2.ExpectNextAsync(4);
                        s2.Request(1);
                        await s2.ExpectCompleteAsync();

                        masterSubscription.Request(1);
                        await masterSubscriber.ExpectCompleteAsync();
                    });
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_not_emit_substreams_if_the_parent_stream_is_empty()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var result = await Source.Empty<int>()
                    .SplitWhen(_ => true)
                    .Lift()
                    .SelectAsync(1, s => s.RunWith(Sink.FirstOrDefault<int>(), Materializer))
                    .Grouped(10)
                    .RunWith(Sink.FirstOrDefault<IEnumerable<int>>(), Materializer);
                result.Should().BeEquivalentTo(default(IEnumerable<int>));
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_work_when_first_element_is_split_by()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                await WithSubstreamsSupportAsync(1, 3,
                    run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet((await getSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                        s1.Request(5);
                        await s1.ExpectNextAsync(1);
                        await s1.ExpectNextAsync(2);
                        await s1.ExpectNextAsync(3);
                        await s1.ExpectCompleteAsync();

                        masterSubscription.Request(1);
                        await masterSubscriber.ExpectCompleteAsync();
                    });
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_support_cancelling_substreams()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                await WithSubstreamsSupportAsync(5, 8,
                    run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet((await getSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                        s1.Cancel();
                        var s2 = new StreamPuppet((await getSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                        s2.Request(4);
                        await s2.ExpectNextAsync(5);
                        await s2.ExpectNextAsync(6);
                        await s2.ExpectNextAsync(7);
                        await s2.ExpectNextAsync(8);
                        s2.Request(1);
                        await s2.ExpectCompleteAsync();

                        masterSubscription.Request(1);
                        await masterSubscriber.ExpectCompleteAsync();
                    });
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_support_cancelling_both_master_and_substream()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var inputs = this.CreatePublisherProbe<int>();

                var substream = this.CreateSubscriberProbe<int>();
                var masterStream = this.CreateSubscriberProbe<NotUsed>();

                Source.FromPublisher(inputs)
                    .SplitWhen(x => x == 2)
                    .Lift()
                    .Select(x => x.RunWith(Sink.FromSubscriber(substream), Materializer))
                    .RunWith(Sink.FromSubscriber(masterStream), Materializer);

                masterStream.Request(1);
                await inputs.SendNextAsync(1);

                substream.Cancel();

                await masterStream.ExpectNextAsync(NotUsed.Instance);
                await masterStream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                masterStream.Cancel();
                await inputs.ExpectCancellationAsync();

                var inputs2 = this.CreatePublisherProbe<int>();
                Source.FromPublisher(inputs2)
                    .SplitWhen(x => x == 2)
                    .Lift()
                    .Select(x => x.RunWith(Sink.Cancelled<int>(), Materializer))
                    .RunWith(Sink.Cancelled<NotUsed>(), Materializer);
                await inputs2.ExpectCancellationAsync();

                var inputs3 = this.CreatePublisherProbe<int>();
                var masterStream3 = this.CreateSubscriberProbe<Source<int, NotUsed>>();

                Source.FromPublisher(inputs3)
                    .SplitWhen(x => x == 2)
                    .Lift()
                    .RunWith(Sink.FromSubscriber(masterStream3), Materializer);

                masterStream3.Request(1);
                await inputs3.SendNextAsync(1);

                var src = await masterStream3.ExpectNextAsync();
                src.RunWith(Sink.Cancelled<int>(), Materializer);

                masterStream3.Request(1);
                await inputs3.SendNextAsync(2);
                var src2 = await masterStream3.ExpectNextAsync();
                var substream4 = this.CreateSubscriberProbe<int>();
                src2.RunWith(Sink.FromSubscriber(substream4), Materializer);

                await substream4.RequestNextAsync(2);
                await substream4.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await masterStream3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await inputs3.ExpectRequestAsync();
                await inputs3.ExpectRequestAsync();
                await inputs3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                substream4.Cancel();
                await inputs3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await masterStream3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                masterStream3.Cancel();
                await inputs3.ExpectCancellationAsync();
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_support_cancelling_the_master_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                await WithSubstreamsSupportAsync(5, 8,
                    run: async (_, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet((await getSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                        masterSubscription.Cancel();

                        s1.Request(4);
                        await s1.ExpectNextAsync(1);
                        await s1.ExpectNextAsync(2);
                        await s1.ExpectNextAsync(3);
                        await s1.ExpectNextAsync(4);
                        s1.Request(1);
                        await s1.ExpectCompleteAsync();
                    });
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_fail_stream_when_SplitWhen_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).SplitWhen(i =>
                {
                    if (i == 3)
                        throw ex;
                    return i % 3 == 0;
                }).Lift().RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);

                var subscriber = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();

                downstreamSubscription.Request(100);
                upstreamSubscription.SendNext(1);

                var substream = await subscriber.ExpectNextAsync();
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(10);
                await substreamPuppet.ExpectNextAsync(1);

                upstreamSubscription.SendNext(2);
                await substreamPuppet.ExpectNextAsync(2);

                upstreamSubscription.SendNext(3);

                (await subscriber.ExpectErrorAsync()).Should().Be(ex);
                await substreamPuppet.ExpectErrorAsync(ex);
                await upstreamSubscription.ExpectCancellationAsync();
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_work_with_single_element_splits()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var result = await Source.From(Enumerable.Range(1, 100))
                    .SplitWhen(_ => true)
                    .Lift()
                    .SelectAsync(1, s => s.RunWith(Sink.First<int>(), Materializer)) // Please note that this line *also* implicitly asserts nonempty substreams                                                                             
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task SplitWhen_must_fail_substream_if_materialized_twice()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
                {
                    await Awaiting(async () =>
                        {
                            var task = Source.Single(1)
                                .SplitWhen(_ => true)
                                .Lift()
                                .SelectAsync(1, source =>
                                {
                                    source.RunWith(Sink.Ignore<int>(), Materializer);
                                    // Sink.ignore+mapAsync pipes error back                                                                                 
                                    return Task.Run(() =>
                                    {
                                        source.RunWith(Sink.Ignore<int>(), Materializer).Wait(TimeSpan.FromSeconds(3));
                                        return 1;
                                    });
                                })
                                .RunWith(Sink.Ignore<int>(), Materializer);
                            await task.WaitAsync(RemainingOrDefault);
                        }).Should().ThrowAsync<IllegalStateException>();
                }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_fail_stream_if_substream_not_materialized_in_time()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var tightTimeoutMaterializer = ActorMaterializer.Create(Sys,
                    ActorMaterializerSettings.Create(Sys)
                        .WithSubscriptionTimeoutSettings(
                            new StreamSubscriptionTimeoutSettings(
                                StreamSubscriptionTimeoutTerminationMode.CancelTermination,
                                TimeSpan.FromMilliseconds(500))));
                var testSource = Source.Single(1)
                    .MapMaterializedValue<TaskCompletionSource<int>>(_ => null)
                    .Concat(Source.Maybe<int>())
                    .SplitWhen(_ => true);

                await Awaiting(async () =>
                {
                    var task = testSource.Lift()
                        .Delay(TimeSpan.FromSeconds(1))
                        .ConcatMany(s => s.MapMaterializedValue<TaskCompletionSource<int>>(_ => null))
                        .RunWith(Sink.Ignore<int>(), tightTimeoutMaterializer);
                    await task.WaitAsync(RemainingOrDefault);
                }).Should().ThrowAsync<SubscriptionTimeoutException>();
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact(Skip = "Supervision is not supported fully by GraphStages yet")]
        public async Task SplitWhen_must_resume_stream_when_splitWhen_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(() => Task.CompletedTask, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_pass_along_early_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var up = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();

                var flowSubscriber = Source.AsSubscriber<int>()
                    .SplitWhen(i => i % 3 == 0)
                    .Lift()
                    .To(Sink.FromSubscriber(down))
                    .Run(Materializer);
                var downstream = await down.ExpectSubscriptionAsync();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = await up.ExpectSubscriptionAsync();
                await upSub.ExpectCancellationAsync();
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }

        [Fact]
        public async Task SplitWhen_must_support_eager_cancellation_of_master_stream_on_cancelling_substreams()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                await WithSubstreamsSupportAsync(5, 8, SubstreamCancelStrategy.Propagate,
                    async (masterSubscriber, _, expectSubFlow) =>
                    {
                        var s1 = new StreamPuppet((await expectSubFlow())
                            .RunWith(Sink.AsPublisher<int>(false), Materializer),
                            this);
                        s1.Cancel();
                        await masterSubscriber.ExpectCompleteAsync();
                    });
            }, Materializer)
                .ShouldCompleteWithin(RemainingOrDefault);
        }
    }
}
