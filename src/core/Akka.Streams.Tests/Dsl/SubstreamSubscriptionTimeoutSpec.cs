using System;
using System.Collections.Generic;
using System.Reactive.Streams;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class nameSpec : AkkaSpec
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

        public nameSpec(ITestOutputHelper helper) : base(Config, helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void GroupBy_and_SplitWhen_must_timeout_and_cancel_substream_publisher_when_no_one_subscribes_to_them_after_some_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = TestSubscriber.CreateManualProbe<KeyValuePair<KeyValuePair<int, Source<int, Unit>>,Source<KeyValuePair<int, Source<int, Unit>>, Unit>>>(this);
                var publisherProbe = TestPublisher.CreateProbe<int>(this);
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy<int, Unit, int, int>(3, x => x%3)
                        .Lift(x => x%3).RunWith(Sink.FromSubscriber(subscriber), Materializer);

                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(10);

                publisherProbe.SendNext(1);
                publisherProbe.SendNext(2);
                publisherProbe.SendNext(3);

                var s1 = subscriber.ExpectNext().Value;
                var s1SubscriberProbe = TestSubscriber.CreateManualProbe<KeyValuePair<int, Source<int, Unit>>>(this);
                s1.RunWith(Sink.FromSubscriber(s1SubscriberProbe), Materializer);
                var s1Subscription = s1SubscriberProbe.ExpectSubscription();
                s1Subscription.Request(100);
                s1SubscriberProbe.ExpectNext().Key.Should().Be(1);

                var s2 = subscriber.ExpectNext().Value;
                var s2SubscriberProbe = TestSubscriber.CreateManualProbe<KeyValuePair<int, Source<int, Unit>>>(this);
                s2.RunWith(Sink.FromSubscriber(s2SubscriberProbe), Materializer);
                var s2Subscription = s2SubscriberProbe.ExpectSubscription();
                s2Subscription.Request(100);
                s2SubscriberProbe.ExpectNext().Key.Should().Be(2);

                var s3 = subscriber.ExpectNext().Value;
                Thread.Sleep(1500);

                Action action = () => s3.RunWith(Sink.Seq<KeyValuePair<int, Source<int, Unit>>>(), Materializer);
                action.ShouldThrow<SubscriptionTimeoutException>();

                publisherProbe.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_and_SplitWhen_must_timeout_and_stop_groupBy_parent_actor_if_none_of_the_substreams_are_actually_consuemd()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = TestSubscriber.CreateManualProbe<KeyValuePair<KeyValuePair<int, Source<int, Unit>>, Source<KeyValuePair<int, Source<int, Unit>>, Unit>>>(this);
                var publisherProbe = TestPublisher.CreateProbe<int>(this);
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy<int, Unit, int, int>(2, x => x % 2)
                        .Lift(x => x % 2).RunWith(Sink.FromSubscriber(subscriber), Materializer);


                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                publisherProbe.SendNext(1);
                publisherProbe.SendNext(2);
                publisherProbe.SendNext(3);
                publisherProbe.SendComplete();

                subscriber.ExpectNext();
                subscriber.ExpectNext();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_and_SplitWhen_must_not_timeout_and_cancel_substream_publisher_when_they_have_been_subscribed_to()
        {
            var subscriber = TestSubscriber.CreateManualProbe<KeyValuePair<KeyValuePair<int, Source<int, Unit>>, Source<KeyValuePair<int, Source<int, Unit>>, Unit>>>(this);
            var publisherProbe = TestPublisher.CreateProbe<int>(this);
            var publisher =
                Source.FromPublisher(publisherProbe)
                    .GroupBy<int, Unit, int, int>(2, x => x % 2)
                    .Lift(x => x % 2).RunWith(Sink.FromSubscriber(subscriber), Materializer);

            var downstreamSubscription = subscriber.ExpectSubscription();
            downstreamSubscription.Request(10);

            publisherProbe.SendNext(1);
            publisherProbe.SendNext(2);

            var s1 = subscriber.ExpectNext().Value;
            var s1SubscriberProbe = TestSubscriber.CreateManualProbe<KeyValuePair<int, Source<int, Unit>>>(this);
            s1.RunWith(Sink.FromSubscriber(s1SubscriberProbe), Materializer);
            var s1Subscription = s1SubscriberProbe.ExpectSubscription();
            s1Subscription.Request(1);
            s1SubscriberProbe.ExpectNext().Key.Should().Be(1);

            var s2 = subscriber.ExpectNext().Value;
            var s2SubscriberProbe = TestSubscriber.CreateManualProbe<KeyValuePair<int, Source<int, Unit>>>(this);
            s2.RunWith(Sink.FromSubscriber(s2SubscriberProbe), Materializer);
            var s2Subscription = s2SubscriberProbe.ExpectSubscription();
            Thread.Sleep(1500);
            s2Subscription.Request(100);
            s2SubscriberProbe.ExpectNext().Key.Should().Be(2);
            s2Subscription.Request(100);
            publisherProbe.SendNext(3);
            publisherProbe.SendNext(4);
            s2SubscriberProbe.ExpectNext().Key.Should().Be(3);
            s2SubscriberProbe.ExpectNext().Key.Should().Be(4);
        }
    }
}
