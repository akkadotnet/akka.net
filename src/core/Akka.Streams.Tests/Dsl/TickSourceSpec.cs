//-----------------------------------------------------------------------
// <copyright file="TickSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class TickSourceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public TickSourceSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Flow_based_on_a_tick_publisher_must_produce_ticks()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(2);
                c.ExpectNext("tick");
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                c.ExpectNext("tick");
                sub.Cancel();
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_a_tick_publisher_must_drop_ticks_when_not_requested()
        {
            var c = this.CreateManualSubscriberProbe<string>();
            Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                .To(Sink.FromSubscriber(c))
                .Run(Materializer);
            var sub = c.ExpectSubscription();
            sub.Request(2);
            c.ExpectNext("tick");
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            c.ExpectNext("tick");
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(1400));
            sub.Request(2);
            c.ExpectNext("tick");
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            c.ExpectNext("tick");
            sub.Cancel();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void A_Flow_based_on_a_tick_publisher_must_reject_multiple_subscribers_but_keep_the_firs()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                    .RunWith(Sink.AsPublisher<string>(false), Materializer);
                var c1 = this.CreateManualSubscriberProbe<string>();
                var c2 = this.CreateManualSubscriberProbe<string>();
                p.Subscribe(c1);
                p.Subscribe(c2);
                var sub1 = c1.ExpectSubscription();
                c2.ExpectSubscriptionAndError();
                sub1.Request(1);
                c1.ExpectNext("tick");
                c1.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                sub1.Request(2);
                c1.ExpectNext("tick");
                sub1.Cancel();
            }, Materializer);
        }

        [Fact(Skip = "Racy. See https://github.com/akkadotnet/akka.net/pull/4424#issuecomment-632284459")]
        public void A_Flow_based_on_a_tick_publisher_must_be_usable_with_zip_for_a_simple_form_of_rate_limiting()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    b.From(Source.From(Enumerable.Range(1, 100))).To(zip.In0);
                    b.From(Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                        .MapMaterializedValue(_ => NotUsed.Instance)).To(zip.In1);
                    b.From(zip.Out)
                        .Via(Flow.Create<(int, string)>().Select(t => t.Item1))
                        .To(Sink.FromSubscriber(c));
                    return ClosedShape.Instance;
                })).Run(Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(1000);
                c.ExpectNext(1);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                c.ExpectNext(2);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                sub.Cancel();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_a_tick_publisher_must_be_possible_to_cancel()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                var tickSource = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick");
                var cancelable = tickSource.To(Sink.FromSubscriber(c)).Run(Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(2);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(600));
                c.ExpectNext("tick");
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                c.ExpectNext("tick");
                cancelable.Cancel();
                AwaitCondition(() => cancelable.IsCancellationRequested);
                sub.Request(3);
                c.ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void A_Flow_based_on_a_tick_publisher_must_have_IsCancelled_mirror_the_cancellation_state()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                var tickSource = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), "tick");
                var cancelable = tickSource.To(Sink.FromSubscriber(c)).Run(Materializer);
                var sub = c.ExpectSubscription();
                sub.Request(2);
                c.ExpectNext("tick");
                cancelable.IsCancellationRequested.Should().BeFalse();
                cancelable.Cancel();
                cancelable.IsCancellationRequested.Should().BeTrue();
                c.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Flow_based_on_a_tick_publisher_must_support_being_cancelled_immediately_after_its_materialization()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                var tickSource = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), "tick");
                var cancelable = tickSource.To(Sink.FromSubscriber(c)).Run(Materializer);
                cancelable.Cancel();
                var sub = c.ExpectSubscription();
                sub.Request(2);
                c.ExpectComplete();
            }, Materializer);
        }
    }
}
