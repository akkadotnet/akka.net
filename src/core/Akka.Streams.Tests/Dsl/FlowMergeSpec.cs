//-----------------------------------------------------------------------
// <copyright file="FlowMergeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowMergeSpec : BaseTwoStreamsSetup<int>
    {
        protected override TestSubscriber.Probe<int> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            Source.FromPublisher(p1)
                .Merge(Source.FromPublisher(p2))
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);
            return subscriber;
        }

        [Fact]
        public async Task A_Merge_for_Flow_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                // Different input size (4 and 6)
                var source1 = Source.From(Enumerable.Range(0, 4));
                var source2 = Source.From(new List<int>());
                var source3 = Source.From(Enumerable.Range(4, 6));
                var probe = this.CreateManualSubscriberProbe<int>();

                source1
                    .Merge(source2)
                    .Merge(source3)
                    .Select(i => i * 2)
                    .Select(i => i / 2)
                    .Select(i => i + 1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

                var subscription = await probe.ExpectSubscriptionAsync();

                var collected = new List<int>();
                for (var i = 1; i <= 10; i++)
                {
                    subscription.Request(1);
                    collected.Add(await probe.ExpectNextAsync());
                }

                collected.Should().BeEquivalentTo(Enumerable.Range(1, 10));
                await probe.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Merge_for_Flow_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.EnsureSubscription();
                subscription1.Request(4);
                foreach (var _ in Enumerable.Range(1, 4))
                    await subscriber1.ExpectNextAsync();
                //Enumerable.Range(1, 4).ForEach(_ => subscriber1.ExpectNext());
                await subscriber1.ExpectCompleteAsync();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = subscriber2.EnsureSubscription();
                subscription2.Request(4);
                foreach (var _ in Enumerable.Range(1, 4))
                    await subscriber2.ExpectNextAsync();
                //Enumerable.Range(1, 4).ForEach(_ => subscriber2.ExpectNext());
                await subscriber2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Merge_for_Flow_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.EnsureSubscription();
                subscription1.Request(4);
                foreach (var _ in Enumerable.Range(1, 4))
                    await subscriber1.ExpectNextAsync();
                //Enumerable.Range(1, 4).ForEach(_ => subscriber1.ExpectNext());
                await subscriber1.ExpectCompleteAsync();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                var subscription2 = subscriber2.EnsureSubscription();
                subscription2.Request(4);
                foreach (var _ in Enumerable.Range(1, 4))
                    await subscriber2.ExpectNextAsync();
                //Enumerable.Range(1, 4).ForEach(_ => subscriber2.ExpectNext());
                await subscriber2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact(Skip = "This is nondeterministic, multiple scenarios can happen")]
        public void A_Merge_for_Flow_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
        }

        [Fact(Skip = "This is nondeterministic, multiple scenarios can happen")]
        public void A_Merge_for_Flow_must_work_with_one_delayed_failed_an_one_nonempty_publisher()
        {
        }

        [Fact]
        public async Task A_Merge_for_Flow_must_pass_along_early_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var up1 = this.CreateManualPublisherProbe<int>();
                var up2 = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<int>();

                var t =
                    Source.AsSubscriber<int>()
                        .MergeMaterialized(Source.AsSubscriber<int>(), ValueTuple.Create)
                        .ToMaterialized(Sink.FromSubscriber(down), Keep.Left)
                        .Run(Materializer);
                var graphSubscriber1 = t.Item1;
                var graphSubscriber2 = t.Item2;

                var downstream = await down.ExpectSubscriptionAsync();
                downstream.Cancel();
                up1.Subscribe(graphSubscriber1);
                up2.Subscribe(graphSubscriber2);

                await up1.ExpectSubscription().ExpectCancellationAsync();
                await up2.ExpectSubscription().ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Merge_for_Flow_must_not_try_to_grab_from_closed_input_previously_enqueued()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var up1 = this.CreatePublisherProbe<int>();
                var up2 = this.CreatePublisherProbe<int>();
                var down = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(up1)
                    .Merge(Source.FromPublisher(up2), true)
                    .To(Sink.FromSubscriber(down))
                    .Run(Materializer);

                await up1.EnsureSubscriptionAsync();
                await up2.EnsureSubscriptionAsync();
                await down.EnsureSubscriptionAsync();

                await up1.ExpectRequestAsync();
                await up2.ExpectRequestAsync();
                await up1.SendNextAsync(7);
                await up2.SendNextAsync(8);
                // there is a race here, the 8 needs to be queued before the
                // source completes (it failed consistently on my machine before bugfix)
                await up2.SendCompleteAsync();
                await down.RequestAsync(1);
                await down.ExpectNextAsync();
            }, Materializer);
        }
    }
}
