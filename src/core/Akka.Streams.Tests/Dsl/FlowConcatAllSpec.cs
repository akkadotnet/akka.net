//-----------------------------------------------------------------------
// <copyright file="FlowConcatAllSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Akka.Streams.Dsl.Internal;
using Akka.TestKit;
using Reactive.Streams;

// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowConcatAllSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowConcatAllSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static readonly TestException TestException = new TestException("test");

        [Fact]
        public void ConcatAll_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s1 = Source.From(new[] {1, 2});
                var s2 = Source.From(new int[] {});
                var s3 = Source.From(new[] {3});
                var s4 = Source.From(new[] {4, 5, 6});
                var s5 = Source.From(new[] {7, 8, 9, 10});

                var main = Source.From(new[] {s1, s2, s3, s4, s5});

                var subscriber = this.CreateManualSubscriberProbe<int>();
                main.ConcatMany(s => s).To(Sink.FromSubscriber(subscriber)).Run(Materializer);
                var subscription = subscriber.ExpectSubscription();
                subscription.Request(10);
                for (var i = 1; i <= 10; i++)
                    subscriber.ExpectNext(i);

                subscription.Request(1);
                subscriber.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_work_together_with_SplitWhen()
        {
            var subscriber = this.CreateSubscriberProbe<int>();
            var source = Source.From(Enumerable.Range(1, 10))
                .SplitWhen(x => x%2 == 0)
                .PrefixAndTail(0)
                .Select(x => x.Item2)
                .ConcatSubstream()
                .ConcatMany(x => x);
            ((Source<int, NotUsed>) source).RunWith(Sink.FromSubscriber(subscriber), Materializer);

            for (var i = 1; i <= 10; i++)
                subscriber.RequestNext(i);

            subscriber.Request(1);
            subscriber.ExpectComplete();}

        [Fact]
        public void ConcatAll_must_on_OnError_on_master_stream_cancel_the_current_open_substream_and_signal_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(publisher)
                    .ConcatMany(x => x)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = this.CreateManualPublisherProbe<int>();
                var substreamFlow = Source.FromPublisher(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);
                var subUpstream = substreamPublisher.ExpectSubscription();

                upstream.SendError(TestException);
                subscriber.ExpectError().Should().Be(TestException);
                subUpstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_on_OnError_on_master_stream_cancel_the_currently_opening_substream_and_signal_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(publisher)
                    .ConcatMany(x => x)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = this.CreateManualPublisherProbe<int>(false);
                var substreamFlow = Source.FromPublisher(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);
                var subUpstream = substreamPublisher.ExpectSubscription();

                upstream.SendError(TestException);

                subUpstream.SendOnSubscribe();

                subscriber.ExpectError().Should().Be(TestException);
                subUpstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_on_OnError_on_opening_substream_cancel_the_master_stream_and_signal_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(publisher)
                    .ConcatMany<Source<int,NotUsed>,int,NotUsed>(x => { throw TestException; })
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = this.CreateManualPublisherProbe<int>();
                var substreamFlow = Source.FromPublisher(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);
                subscriber.ExpectError().Should().Be(TestException);
                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_on_OnError_on_open_substream_cancel_the_master_stream_and_signal_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(publisher)
                    .ConcatMany(x => x)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = this.CreateManualPublisherProbe<int>();
                var substreamFlow = Source.FromPublisher(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);

                var subUpstream = substreamPublisher.ExpectSubscription();
                subUpstream.SendError(TestException);
                subscriber.ExpectError().Should().Be(TestException);
                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_on_cancellation_cancel_the_current_open_substream_and_the_master_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(publisher)
                    .ConcatMany(x => x)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = this.CreateManualPublisherProbe<int>();
                var substreamFlow = Source.FromPublisher(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);
                var subUpstream = substreamPublisher.ExpectSubscription();

                downstream.Cancel();

                subUpstream.ExpectCancellation();
                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_on_cancellation_cancel_the_currently_opening_substream_and_the_master_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var subscriber = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(publisher)
                    .ConcatMany(x => x)
                    .To(Sink.FromSubscriber(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = this.CreateManualPublisherProbe<int>(false);
                var substreamFlow = Source.FromPublisher(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);
                var subUpstream = substreamPublisher.ExpectSubscription();

                downstream.Cancel();

                subUpstream.SendOnSubscribe();

                subUpstream.ExpectCancellation();
                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void ConcatAll_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up = this.CreateManualPublisherProbe<Source<int, NotUsed>>();
                var down = this.CreateManualSubscriberProbe<int>();

                var flowSubscriber = Source.AsSubscriber<Source<int, NotUsed>>()
                    .ConcatMany(x => x.MapMaterializedValue<ISubscriber<Source<int, NotUsed>>>(_ => null))
                    .To(Sink.FromSubscriber(down))
                    .Run(Materializer);

                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = up.ExpectSubscription();
                upSub.ExpectCancellation();
            }, Materializer);
        }
    }
}
