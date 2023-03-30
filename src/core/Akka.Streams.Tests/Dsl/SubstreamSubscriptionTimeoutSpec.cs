//-----------------------------------------------------------------------
// <copyright file="SubstreamSubscriptionTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class SubstreamSubscriptionTimeoutSpec : AkkaSpec
    {
        private const string Config = @"
            akka.stream.materializer 
            {
                subscription-timeout 
                {
                    mode = cancel
                    timeout = 300 ms
                }
            }
";
        private ActorMaterializer Materializer { get; }

        public SubstreamSubscriptionTimeoutSpec(ITestOutputHelper helper) : base(Config, helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task GroupBy_and_SplitWhen_must_timeout_and_cancel_substream_publisher_when_no_one_subscribes_to_them_after_some_time()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                var publisherProbe = this.CreatePublisherProbe<int>();
                Source.FromPublisher(publisherProbe)
                    .GroupBy(3, x => x % 3)
                    .Lift(x => x % 3)
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Request(100);

                await publisherProbe.SendNextAsync(1);
                await publisherProbe.SendNextAsync(2);
                await publisherProbe.SendNextAsync(3);

                /*
                 * Why this spec is skipped: in the event that subscriber.ExpectSubscription() or (subscriber.ExpectNext()
                 * + s1SubscriberProbe.ExpectSubscription()) exceeds 300ms, the next call to subscriber.ExpectNext will
                 * fail. This test is too tightly fitted to the timeout duration to be reliable, although somewhat ironically
                 * it does validate that the underlying cancellation does work! 
                 */

                var item = await subscriber.ExpectNextAsync();
                var s1 = item.Item2;
                // should not break normal usage
                var s1SubscriberProbe = this.CreateManualSubscriberProbe<int>();
                s1.RunWith(Sink.FromSubscriber(s1SubscriberProbe), Materializer);
                var s1Subscription = await s1SubscriberProbe.ExpectSubscriptionAsync();
                s1Subscription.Request(100);
                var next = await s1SubscriberProbe.ExpectNextAsync();
                next.Should().Be(1);

                item = await subscriber.ExpectNextAsync();
                var s2 = item.Item2;
                // should not break normal usage
                var s2SubscriberProbe = this.CreateManualSubscriberProbe<int>();
                s2.RunWith(Sink.FromSubscriber(s2SubscriberProbe), Materializer);
                var s2Subscription = await s2SubscriberProbe.ExpectSubscriptionAsync();
                s2Subscription.Request(100);
                next = await s2SubscriberProbe.ExpectNextAsync();
                next.Should().Be(2);

                item = await subscriber.ExpectNextAsync();
                var s3 = item.Item2;

                // sleep long enough for it to be cleaned up
                await Task.Delay(1500);

                // Must be a Sink.seq, otherwise there is a race due to the concat in the `lift` implementation
                Action action = () => s3.RunWith(Sink.Seq<int>(), Materializer).Wait(RemainingOrDefault);
                action.Should().Throw<SubscriptionTimeoutException>();

                await publisherProbe.SendCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GroupBy_and_SplitWhen_must_timeout_and_stop_groupBy_parent_actor_if_none_of_the_substreams_are_actually_consumed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                var publisherProbe = this.CreatePublisherProbe<int>();
                Source.FromPublisher(publisherProbe)
                    .GroupBy(2, x => x % 2)
                    .Lift(x => x % 2).RunWith(Sink.FromSubscriber(subscriber), Materializer);


                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
                downstreamSubscription.Request(100);

                await publisherProbe.SendNextAsync(1);
                await publisherProbe.SendNextAsync(2);
                await publisherProbe.SendNextAsync(3);
                await publisherProbe.SendCompleteAsync();

                await subscriber.ExpectNextAsync();
                await subscriber.ExpectNextAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task GroupBy_and_SplitWhen_must_not_timeout_and_cancel_substream_publisher_when_they_have_been_subscribed_to()
        {
            var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
            var publisherProbe = this.CreatePublisherProbe<int>();
            Source.FromPublisher(publisherProbe)
                .GroupBy(2, x => x % 2)
                .Lift(x => x % 2).RunWith(Sink.FromSubscriber(subscriber), Materializer);

            var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();
            downstreamSubscription.Request(10);

            await publisherProbe.SendNextAsync(1);
            await publisherProbe.SendNextAsync(2);

            var item = await subscriber.ExpectNextAsync();
            var s1 = item.Item2;
            var s1SubscriberProbe = this.CreateManualSubscriberProbe<int>();
            s1.RunWith(Sink.FromSubscriber(s1SubscriberProbe), Materializer);
            var s1Subscription = await s1SubscriberProbe.ExpectSubscriptionAsync();
            s1Subscription.Request(1);
            var s1Subscriber = await s1SubscriberProbe.ExpectNextAsync();
            s1Subscriber.Should().Be(1);

            item = await subscriber.ExpectNextAsync();
            var s2 = item.Item2;
            var s2SubscriberProbe = this.CreateManualSubscriberProbe<int>();
            s2.RunWith(Sink.FromSubscriber(s2SubscriberProbe), Materializer);
            var s2Subscription = await s2SubscriberProbe.ExpectSubscriptionAsync();
            await Task.Delay(1500);
            s2Subscription.Request(100);

            var s2Subscriber = await s2SubscriberProbe.ExpectNextAsync();
            s2Subscriber.Should().Be(2);

            s1Subscription.Request(100);
            await publisherProbe.SendNextAsync(3);
            await publisherProbe.SendNextAsync(4);
            var s1S = await s1SubscriberProbe.ExpectNextAsync();
            s1S.Should().Be(3);
            var s2S = await s2SubscriberProbe.ExpectNextAsync();
            s2S.Should().Be(4);
        }
    }
}
