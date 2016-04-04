using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowPrefixAndTailSpec : AkkaSpec
    {
        private readonly ITestOutputHelper _helper;
        public ActorMaterializer Materializer { get; set; }

        public FlowPrefixAndTailSpec(ITestOutputHelper helper) : base(helper)
        {
            _helper = helper;
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2,2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static TestException TestException = new TestException("test");

        private static
            Sink<Tuple<IEnumerable<int>, Source<int, Unit>>, Task<Tuple<IEnumerable<int>, Source<int, Unit>>>>
            NewHeadSink => Sink.First<Tuple<IEnumerable<int>, Source<int, Unit>>>();


        [Fact]
        public void PrefixAndTail_must_work_on_empty_input()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.Empty<int>().PrefixAndTail(10).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var tailFlow = fut.Result.Item2;
                var tailSubscriber = TestSubscriber.CreateManualProbe<int>(this);
                tailFlow.To(Sink.FromSubscriber<int, Unit>(tailSubscriber)).Run(Materializer);
                tailSubscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_work_on_short_inputs()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.From(new [] {1,2,3}).PrefixAndTail(10).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.ShouldAllBeEquivalentTo(new[] {1, 2, 3});
                var tailFlow = fut.Result.Item2;
                var tailSubscriber = TestSubscriber.CreateManualProbe<int>(this);
                tailFlow.To(Sink.FromSubscriber<int, Unit>(tailSubscriber)).Run(Materializer);
                tailSubscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_work_on_longer_inputs()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.From(Enumerable.Range(1, 10)).PrefixAndTail(5).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                var takes = fut.Result.Item1;
                var tail = fut.Result.Item2;
                takes.ShouldAllBeEquivalentTo(Enumerable.Range(1, 5));

                var futureSink2 = Sink.First<IEnumerable<int>>();
                var fut2 = tail.Grouped(6).RunWith(futureSink2, Materializer);
                fut2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut2.Result.ShouldAllBeEquivalentTo(Enumerable.Range(6, 5));
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_handle_zero_take_count()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.From(Enumerable.Range(1, 10)).PrefixAndTail(0).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.Should().BeEmpty();
                var tail = fut.Result.Item2;

                var futureSink2 = Sink.First<IEnumerable<int>>();
                var fut2 = tail.Grouped(11).RunWith(futureSink2, Materializer);
                fut2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut2.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_handle_negative_take_count()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.From(Enumerable.Range(1, 10)).PrefixAndTail(-1).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.Should().BeEmpty();
                var tail = fut.Result.Item2;

                var futureSink2 = Sink.First<IEnumerable<int>>();
                var fut2 = tail.Grouped(11).RunWith(futureSink2, Materializer);
                fut2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut2.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_work_if_size_of_tak_is_equal_to_stream_size()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.From(Enumerable.Range(1,10)).PrefixAndTail(10).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
                var tail = fut.Result.Item2;
                var subscriber = TestSubscriber.CreateManualProbe<int>(this);
                tail.To(Sink.FromSubscriber<int, Unit>(subscriber)).Run(Materializer);
                subscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_throw_if_tail_is_attempted_to_be_materialized_twice()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.From(Enumerable.Range(1, 2)).PrefixAndTail(1).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.ShouldAllBeEquivalentTo(Enumerable.Range(1, 1));
                var tail = fut.Result.Item2;

                var subscriber1 = TestSubscriber.CreateProbe<int>(this);
                tail.To(Sink.FromSubscriber<int, Unit>(subscriber1)).Run(Materializer);

                var subscriber2 = TestSubscriber.CreateProbe<int>(this);
                tail.To(Sink.FromSubscriber<int, Unit>(subscriber2)).Run(Materializer);

                subscriber2.ExpectSubscriptionAndError()
                    .Message.Should()
                    .Be("Substream Source cannot be materialized more than once");
                subscriber1.RequestNext(2).ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_signal_error_if_substream_has_been_not_subscribed_in_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var settings = ActorMaterializerSettings.Create(Sys)
                    .WithSubscriptionTimeoutSettings(
                        new StreamSubscriptionTimeoutSettings(
                            StreamSubscriptionTimeoutTerminationMode.CancelTermination,
                            TimeSpan.FromMilliseconds(500)));
                var tightTimeoutMaterializer = ActorMaterializer.Create(Sys, settings);

                var futureSink = NewHeadSink;
                var fut = Source.From(Enumerable.Range(1, 2)).PrefixAndTail(1).RunWith(futureSink, tightTimeoutMaterializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.ShouldAllBeEquivalentTo(Enumerable.Range(1, 1));
                var tail = fut.Result.Item2;

                var subscriber = TestSubscriber.CreateProbe<int>(this);
                Thread.Sleep(1000);
                tail.To(Sink.FromSubscriber<int, Unit>(subscriber)).Run(tightTimeoutMaterializer);
                subscriber.ExpectSubscriptionAndError()
                    .Message.Should()
                    .Be("Substream Source has not been materialized in 00:00:00.5000000");
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_shut_down_main_stage_if_substream_is_empty_even_when_not_subscribed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var futureSink = NewHeadSink;
                var fut = Source.Single(1).PrefixAndTail(1).RunWith(futureSink, Materializer);
                fut.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                fut.Result.Item1.Should().ContainSingle(i => i == 1);
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_handle_OnError_when_no_substream_is_open()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = TestPublisher.CreateManualProbe<int>(this);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<IEnumerable<int>, Source<int, Unit>>>(this);

                Source.FromPublisher<int, Unit>(publisher)
                    .PrefixAndTail(3)
                    .To(Sink.FromSubscriber<Tuple<IEnumerable<int>, Source<int, Unit>>, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();

                downstream.Request(1);

                upstream.ExpectRequest();
                upstream.SendNext(1);
                upstream.SendError(TestException);

                subscriber.ExpectError().Should().Be(TestException);
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_handle_OnError_when_substream_is_open()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = TestPublisher.CreateManualProbe<int>(this);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<IEnumerable<int>, Source<int, Unit>>>(this);

                Source.FromPublisher<int, Unit>(publisher)
                    .PrefixAndTail(1)
                    .To(Sink.FromSubscriber<Tuple<IEnumerable<int>, Source<int, Unit>>, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();

                downstream.Request(1000);

                upstream.ExpectRequest();
                upstream.SendNext(1);

                var t = subscriber.ExpectNext();
                t.Item1.Should().ContainSingle(i => i == 1);
                var tail = t.Item2;
                subscriber.ExpectComplete();

                var substreamSubscriber = TestSubscriber.CreateManualProbe<int>(this);
                tail.To(Sink.FromSubscriber<int, Unit>(substreamSubscriber)).Run(Materializer);
                substreamSubscriber.ExpectSubscription();
                upstream.SendError(TestException);
                substreamSubscriber.ExpectError().Should().Be(TestException);
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_handle_master_stream_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = TestPublisher.CreateManualProbe<int>(this);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<IEnumerable<int>, Source<int, Unit>>>(this);

                Source.FromPublisher<int, Unit>(publisher)
                    .PrefixAndTail(1)
                    .To(Sink.FromSubscriber<Tuple<IEnumerable<int>, Source<int, Unit>>, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();

                downstream.Request(1);

                upstream.ExpectRequest();
                upstream.SendNext(1);

                downstream.Cancel();
                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_handle_substream_cacellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = TestPublisher.CreateManualProbe<int>(this);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<IEnumerable<int>, Source<int, Unit>>>(this);

                Source.FromPublisher<int, Unit>(publisher)
                    .PrefixAndTail(1)
                    .To(Sink.FromSubscriber<Tuple<IEnumerable<int>, Source<int, Unit>>, Unit>(subscriber))
                    .Run(Materializer);

                var upstream = publisher.ExpectSubscription();
                var downstream = subscriber.ExpectSubscription();

                downstream.Request(1000);

                upstream.ExpectRequest();
                upstream.SendNext(1);

                var t = subscriber.ExpectNext();
                t.Item1.Should().ContainSingle(i => i == 1);
                var tail = t.Item2;
                subscriber.ExpectComplete();

                var substreamSubscriber = TestSubscriber.CreateManualProbe<int>(this);
                tail.To(Sink.FromSubscriber<int, Unit>(substreamSubscriber)).Run(Materializer);
                substreamSubscriber.ExpectSubscription().Cancel();

                upstream.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up = TestPublisher.CreateManualProbe<int>(this);
                var down = TestSubscriber.CreateManualProbe<Tuple<IEnumerable<int>, Source<int, Unit>>>(this);

                var flowSubscriber = Source.AsSubscriber<int>()
                    .PrefixAndTail(1)
                    .To(Sink.FromSubscriber<Tuple<IEnumerable<int>, Source<int, Unit>>, Unit>(down))
                    .Run(Materializer);

                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = up.ExpectSubscription();
                upSub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void PrefixAndTail_must_work_even_if_tail_subscriber_arrives_after_substream_completion()
        {
            var pub = TestPublisher.CreateManualProbe<int>(this);
            var sub = TestSubscriber.CreateManualProbe<int>(this);

            var f =
                Source.FromPublisher<int, Unit>(pub)
                    .PrefixAndTail(1)
                    .RunWith(Sink.First<Tuple<IEnumerable<int>, Source<int, Unit>>>(), Materializer);
            var s = pub.ExpectSubscription();
            s.SendNext(0);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var tail = f.Result.Item2;
            var tailPub = tail.RunWith(Sink.AsPublisher<int>(false), Materializer);
            s.SendComplete();

            tailPub.Subscribe(sub);
            sub.ExpectSubscriptionAndComplete();
        }
    }
}
