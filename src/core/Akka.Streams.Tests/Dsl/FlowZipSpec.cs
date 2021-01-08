//-----------------------------------------------------------------------
// <copyright file="FlowZipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowZipSpec : BaseTwoStreamsSetup<(int, int)>
    {
        public FlowZipSpec(ITestOutputHelper output = null) : base(output)
        {
        }

        protected override TestSubscriber.Probe<(int, int)> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = this.CreateSubscriberProbe<(int, int)>();
            Source.FromPublisher(p1)
                .Zip(Source.FromPublisher(p2))
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);
            return subscriber;
        }

        [Fact]
        public void A_Zip_for_Flow_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<(int, string)>();
                Source.From(Enumerable.Range(1, 4))
                    .Zip(Source.From(new[] {"A", "B", "C", "D", "E", "F"}))
                    .RunWith(Sink.FromSubscriber(probe), Materializer);
                var subscription = probe.ExpectSubscription();

                subscription.Request(2);
                probe.ExpectNext((1, "A"));
                probe.ExpectNext((2, "B"));

                subscription.Request(1);
                probe.ExpectNext((3, "C"));
                subscription.Request(1);
                probe.ExpectNext((4, "D"));

                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Zip_for_Flow_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void A_Zip_for_Flow_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndComplete();

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
            subscriber2.ExpectSubscriptionAndComplete();
        }

        [Fact]
        public void A_Zip_for_Flow_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
            subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
        }

        [Fact]
        public void A_Zip_for_Flow_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(SoonToFailPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            subscriber1.ExpectSubscriptionAndError().Should().Be(TestException());

            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToFailPublisher<int>());
            subscriber2.ExpectSubscriptionAndError().Should().Be(TestException());
        }
    }
}
