//-----------------------------------------------------------------------
// <copyright file="FlowMergeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class FlowMergeSpec : BaseTwoStreamsSetup<int>
    {
        protected override TestSubscriber.Probe<int> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            var subscriber = TestSubscriber.CreateProbe<int>(this);
            Source.FromPublisher(p1)
                .Merge(Source.FromPublisher(p2))
                .RunWith(Sink.FromSubscriber(subscriber), Materializer);
            return subscriber;
        }

        [Fact]
        public void A_Merge_for_Flow_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                // Different input size (4 and 6)
                var source1 = Source.From(Enumerable.Range(0, 4));
                var source2 = Source.From(new List<int>());
                var source3 = Source.From(Enumerable.Range(4, 6));
                var probe = TestSubscriber.CreateManualProbe<int>(this);

                source1
                    .Merge(source2)
                    .Merge(source3)
                    .Select(i => i*2)
                    .Select(i => i/2)
                    .Select(i => i+1)
                .RunWith(Sink.FromSubscriber(probe), Materializer);

                var subscription = probe.ExpectSubscription();

                var collected = new List<int>();
                for (var i = 1; i <= 10; i++)
                {
                    subscription.Request(1);
                    collected.Add(probe.ExpectNext());
                }

                collected.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Merge_for_Flow_must_work_with_one_immediately_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(CompletedPublisher<int>(), NonEmptyPublisher(Enumerable.Range(1,4)));
                var subscription1 = subscriber1.EnsureSubscription();
                subscription1.Request(4);
                Enumerable.Range(1, 4).ForEach(_ => subscriber1.ExpectNext());
                subscriber1.ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), CompletedPublisher<int>());
                var subscription2 = subscriber2.EnsureSubscription();
                subscription2.Request(4);
                Enumerable.Range(1, 4).ForEach(_ => subscriber2.ExpectNext());
                subscriber2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Merge_for_Flow_must_work_with_one_delayed_completed_and_one_nonempty_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber1 = Setup(SoonToCompletePublisher<int>(), NonEmptyPublisher(Enumerable.Range(1, 4)));
                var subscription1 = subscriber1.EnsureSubscription();
                subscription1.Request(4);
                Enumerable.Range(1, 4).ForEach(_ => subscriber1.ExpectNext());
                subscriber1.ExpectComplete();

                var subscriber2 = Setup(NonEmptyPublisher(Enumerable.Range(1, 4)), SoonToCompletePublisher<int>());
                var subscription2 = subscriber2.EnsureSubscription();
                subscription2.Request(4);
                Enumerable.Range(1, 4).ForEach(_ => subscriber2.ExpectNext());
                subscriber2.ExpectComplete();
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
        public void A_Merge_for_Flow_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up1 = TestPublisher.CreateManualProbe<int>(this);
                var up2 = TestPublisher.CreateManualProbe<int>(this);
                var down = TestSubscriber.CreateManualProbe<int>(this);

                var t =
                    Source.AsSubscriber<int>()
                        .MergeMaterialized(Source.AsSubscriber<int>(), Tuple.Create)
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
