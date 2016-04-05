using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
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

                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                main.FlatMapConcat(s => s).To(Sink.FromSubscriber<int, Unit>(subscriber)).Run(Materializer);
                var subscription = subscriber.ExpectSubscription();
                subscription.Request(10);
                for (var i = 1; i <= 10; i++)
                    subscriber.ExpectNext(i);

                subscription.Request(1);
                subscriber.ExpectComplete();
            }, Materializer);
        }

        [Fact(Skip = "We need to implement SubFlow first")]
        public void ConcatAll_must_work_together_with_SplitWhen()
        {
            /*
             val subscriber = TestSubscriber.probe[Int]()
              Source(1 to 10)
                .splitWhen(_ % 2 == 0)
                .prefixAndTail(0)
                .map(_._2)
                .concatSubstreams
                .flatMapConcat(ConstantFun.scalaIdentityFunction)
                .runWith(Sink.fromSubscriber(subscriber))

              for (i ← 1 to 10)
                subscriber.requestNext() shouldBe i

              subscriber.request(1)
              subscriber.expectComplete()
            */
        }

        [Fact]
        public void ConcatAll_must_on_OnError_on_master_stream_cancel_the_current_open_substream_and_signal_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher<Source<int, Unit>, Unit>(publisher)
                    .FlatMapConcat(x => x)
                    .To(Sink.FromSubscriber<int, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = TestPublisher.CreateManualProbe<int>(this);
                var substreamFlow = Source.FromPublisher<int, Unit>(substreamPublisher);
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
                var publisher = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher<Source<int, Unit>, Unit>(publisher)
                    .FlatMapConcat(x => x)
                    .To(Sink.FromSubscriber<int, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = TestPublisher.CreateManualProbe<int>(this, false);
                var substreamFlow = Source.FromPublisher<int, Unit>(substreamPublisher);
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
                var publisher = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher<Source<int, Unit>, Unit>(publisher)
                    .FlatMapConcat<Source<int,Unit>,int,Unit>(x => { throw TestException; })
                    .To(Sink.FromSubscriber<int, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = TestPublisher.CreateManualProbe<int>(this);
                var substreamFlow = Source.FromPublisher<int, Unit>(substreamPublisher);
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
                var publisher = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher<Source<int, Unit>, Unit>(publisher)
                    .FlatMapConcat(x => x)
                    .To(Sink.FromSubscriber<int, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = TestPublisher.CreateManualProbe<int>(this);
                var substreamFlow = Source.FromPublisher<int, Unit>(substreamPublisher);
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
                var publisher = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher<Source<int, Unit>, Unit>(publisher)
                    .FlatMapConcat(x => x)
                    .To(Sink.FromSubscriber<int, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = TestPublisher.CreateManualProbe<int>(this);
                var substreamFlow = Source.FromPublisher<int, Unit>(substreamPublisher);
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
                var publisher = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher<Source<int, Unit>, Unit>(publisher)
                    .FlatMapConcat(x => x)
                    .To(Sink.FromSubscriber<int, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();
                downstream.Request(1000);

                var substreamPublisher = TestPublisher.CreateManualProbe<int>(this, false);
                var substreamFlow = Source.FromPublisher<int, Unit>(substreamPublisher);
                upstream.ExpectRequest();
                upstream.SendNext(substreamFlow);
                var subUpstream = substreamPublisher.ExpectSubscription();

                downstream.Cancel();

                subUpstream.SendOnSubscribe();

                subUpstream.ExpectCancellation();
                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact(Skip = "FlatMapConcat has type conflict due to different materialize types")]
        public void ConcatAll_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up = TestPublisher.CreateManualProbe<Source<int, Unit>>(this);
                var down = TestSubscriber.CreateManualProbe<int>(this);

                //var flowSubscriber = Source.AsSubscriber<Source<int, Unit>>()
                //    .FlatMapConcat(x => x)
                //    .To(Sink.FromSubscriber<int, Unit>(down))
                //    .Run(Materializer);

                //var downstream = down.ExpectSubscription();
                //downstream.Cancel();
                //up.Subscribe(flowSubscriber);
                //var upSub = up.ExpectSubscription();
                //upSub.ExpectCancellation();
            }, Materializer);
        }
    }
}
