//-----------------------------------------------------------------------
// <copyright file="FlowInterleaveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using FluentAssertions;
using Reactive.Streams;
using Xunit;

// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowInterleaveSpec : BaseTwoStreamsSetup<int>
    {
        protected override TestSubscriber.Probe<int> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            Source.FromPublisher(p1)
                .Interleave(Source.FromPublisher(p2), 2)
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);
            return subscriber;
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();

                Source.From(Enumerable.Range(0, 4))
                    .Interleave(Source.From(Enumerable.Range(4, 3)), 2)
                    .Interleave(Source.From(Enumerable.Range(7, 5)), 3)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                var subscription = probe.ExpectSubscription();

                var collected = new List<int>();
                for (var i = 1; i <= 12; i++)
                {
                    subscription.Request(1);
                    collected.Add(probe.ExpectNext());
                }

                collected.ShouldAllBeEquivalentTo(new[] {0, 1, 4, 7, 8, 9, 5, 2, 3, 10, 11, 6});
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_when_segmentSize_is_not_equal_elements_in_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();

                Source.From(Enumerable.Range(0, 3))
                    .Interleave(Source.From(Enumerable.Range(3, 3)), 2)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                probe.ExpectSubscription().Request(10);
                probe.ExpectNext(0, 1, 3, 4, 2, 5);
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_with_segmentSize_1()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();

                Source.From(Enumerable.Range(0, 3))
                    .Interleave(Source.From(Enumerable.Range(3, 3)), 1)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                probe.ExpectSubscription().Request(10);
                probe.ExpectNext(0, 3, 1, 4, 2, 5);
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_not_work_with_segmentSize_0()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = Source.From(Enumerable.Range(0, 3));
                source.Invoking(s => s.Interleave(Source.From(Enumerable.Range(3, 3)), 0))
                    .ShouldThrow<ArgumentException>();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_when_segmentSize_is_greater_than_stream_elements()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(0, 3))
                    .Interleave(Source.From(Enumerable.Range(3, 13)), 10)
                    .RunWith(Sink.FromSubscriber(probe), Materializer);

                probe.ExpectSubscription().Request(25);
                Enumerable.Range(0, 16).ForEach(i => probe.ExpectNext(i));
                probe.ExpectComplete();

                var probe2 = this.CreateManualSubscriberProbe<int>();
                Source.From(Enumerable.Range(1, 20))
                    .Interleave(Source.From(Enumerable.Range(21, 5)), 10)
                    .RunWith(Sink.FromSubscriber(probe2), Materializer);

                probe2.ExpectSubscription().Request(100);
                Enumerable.Range(1, 10).ForEach(i => probe2.ExpectNext(i));
                Enumerable.Range(21, 5).ForEach(i => probe2.ExpectNext(i));
                Enumerable.Range(11, 10).ForEach(i => probe2.ExpectNext(i));
                probe2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();
                subscription1.Request(4);
                Enumerable.Range(1, 4).ForEach(i => subscriber1.ExpectNext(i));
                subscriber1.ExpectComplete();


                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();
                subscription2.Request(4);
                Enumerable.Range(1, 4).ForEach(i => subscriber2.ExpectNext(i));
                subscriber2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.ExpectSubscription();
                subscription1.Request(4);
                Enumerable.Range(1, 4).ForEach(i => subscriber1.ExpectNext(i));
                subscriber1.ExpectComplete();


                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                var subscription2 = subscriber2.ExpectSubscription();
                subscription2.Request(4);
                Enumerable.Range(1, 4).ForEach(i => subscriber2.ExpectNext(i));
                subscriber2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void An_Interleave_for_Flow_must_work_with_one_immediately_failed_and_one_nonempty_publisher()
        {
            var subscriber1 = Setup(FailedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
            var subscription1 = subscriber1.ExpectSubscription();
            subscription1.Request(4);
            subscriber1.ExpectError().Should().Be(TestException());


            var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), FailedPublisher<int>());
            var subscription2 = subscriber2.ExpectSubscription();
            subscription2.Request(4);
            var result = subscriber2.ExpectNextOrError();

            if (result is int)
                result.Should().Be(1);
            else
            {
                result.Should().Be(TestException());
                return;
            }

            result = subscriber2.ExpectNextOrError();
            if (result is int)
                result.Should().Be(1);
            else
            {
                result.Should().Be(TestException());
                return;
            }

            subscriber2.ExpectError().Should().Be(TestException());
        }

        [Fact(Skip = "This is non-deterministic, multiple scenarios can happen")]
        public void An_Interleave_for_Flow_must_work_with_one_delayed_failed_and_one_nonempty_publisher()
        {
        }

        [Fact]
        public void An_Interleave_for_Flow_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up1 = this.CreateManualPublisherProbe<int>();
                var up2 = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<int>();

                var t = Source.AsSubscriber<int>()
                    .InterleaveMaterialized(Source.AsSubscriber<int>(), 2, ValueTuple.Create)
                    .ToMaterialized(Sink.FromSubscriber(down), Keep.Left)
                    .Run(Materializer);
                var graphSubscriber1 = t.Item1;
                var graphSubscriber2 = t.Item2;

                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up1.Subscribe(graphSubscriber1);
                up2.Subscribe(graphSubscriber2);
                up1.ExpectSubscription().ExpectCancellation();
                up2.ExpectSubscription().ExpectCancellation();
            }, Materializer);
        }
    }
}
