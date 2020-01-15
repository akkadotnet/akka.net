//-----------------------------------------------------------------------
// <copyright file="QueueSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Dropped = Akka.Streams.QueueOfferResult.Dropped;
using Enqueued = Akka.Streams.QueueOfferResult.Enqueued;
using QueueClosed = Akka.Streams.QueueOfferResult.QueueClosed;

namespace Akka.Streams.Tests.Dsl
{
    public class QueueSourceSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;
        private readonly TimeSpan _pause = TimeSpan.FromMilliseconds(300);

        public QueueSourceSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        private static void AssertSuccess(Task<IQueueOfferResult> task)
        {
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().Be(Enqueued.Instance);
        }

        [Fact]
        public void QueueSource_should_emit_received_message_to_the_stream()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var queue =
                Source.Queue<int>(10, OverflowStrategy.Fail).To(Sink.FromSubscriber(s)).Run(_materializer);
            var sub = s.ExpectSubscription();

            sub.Request(2);
            AssertSuccess(queue.OfferAsync(1));
            s.ExpectNext(1);
            AssertSuccess(queue.OfferAsync(2));
            s.ExpectNext(2);
            AssertSuccess(queue.OfferAsync(3));
            sub.Cancel();
        }

        [Fact]
        public void QueueSource_should_be_reusable()
        {
            var source = Source.Queue<int>(0, OverflowStrategy.Backpressure);
            var q1 = source.To(Sink.Ignore<int>()).Run(_materializer);
            q1.Complete();
            var task = q1.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var q2 = source.To(Sink.Ignore<int>()).Run(_materializer);
            task = q2.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeFalse();
        }

        [Fact]
        public void QueueSource_should_reject_elements_when_backpressuring_with_maxBuffer_0()
        {
            var t =
                Source.Queue<int>(0, OverflowStrategy.Backpressure)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = t.Item1;
            var probe = t.Item2;
            var task = source.OfferAsync(42);
            var ex = source.OfferAsync(43);
            ex.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3)))
                .ShouldThrow<IllegalStateException>()
                .And.Message.Should()
                .Contain("have to wait");

            probe.RequestNext().Should().Be(42);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().Be(Enqueued.Instance);

        }

        [Fact]
        public void QueueSource_should_buffer_when_needed()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var queue =
                Source.Queue<int>(100, OverflowStrategy.DropHead)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);
            var sub = s.ExpectSubscription();

            for (var i = 1; i <= 20; i++) AssertSuccess(queue.OfferAsync(i));
            sub.Request(10);
            for (var i = 1; i <= 10; i++) AssertSuccess(queue.OfferAsync(i));
            sub.Request(10);
            for (var i = 11; i <= 20; i++) AssertSuccess(queue.OfferAsync(i));

            for (var i = 200; i <= 399; i++) AssertSuccess(queue.OfferAsync(i));
            sub.Request(100);
            for (var i = 300; i <= 399; i++) AssertSuccess(queue.OfferAsync(i));
            sub.Cancel();
        }

        [Fact]
        public void QueueSource_should_not_fail_when_0_buffer_space_and_demand_is_signalled()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(0, OverflowStrategy.DropHead)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = s.ExpectSubscription();

                sub.Request(1);
                AssertSuccess(queue.OfferAsync(1));
                sub.Cancel();

            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_wait_for_demand_when_buffer_is_0()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(0, OverflowStrategy.DropHead)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = s.ExpectSubscription();

                queue.OfferAsync(1).PipeTo(TestActor);
                ExpectNoMsg(_pause);
                sub.Request(1);
                ExpectMsg<Enqueued>();
                s.ExpectNext(1);
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_finish_offer_and_complete_futures_when_stream_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(0, OverflowStrategy.DropHead)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = s.ExpectSubscription();

                queue.WatchCompletionAsync()
                    .ContinueWith(t => "done", TaskContinuationOptions.OnlyOnRanToCompletion)
                    .PipeTo(TestActor);
                queue.OfferAsync(1).PipeTo(TestActor);
                ExpectNoMsg(_pause);

                sub.Cancel();

                ExpectMsgAllOf<object>(QueueClosed.Instance, "done");
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_fail_stream_on_buffer_overflow_in_fail_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.Fail)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                s.ExpectSubscription();

                queue.OfferAsync(1);
                queue.OfferAsync(1);
                s.ExpectError();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_remember_pull_from_downstream_to_send_offered_element_immediately()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var probe = CreateTestProbe();
                var queue = TestSourceStage<int, ISourceQueueWithComplete<int>>.Create(
                    new QueueSource<int>(1, OverflowStrategy.DropHead), probe)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);
                var sub = s.ExpectSubscription();

                sub.Request(1);
                probe.ExpectMsg<GraphStageMessages.Pull>();
                AssertSuccess(queue.OfferAsync(1));
                s.ExpectNext(1);
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_fail_offer_future_if_user_does_not_wait_in_backpressure_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var tuple =
                    Source.Queue<int>(5, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);
                var queue = tuple.Item1;
                var probe = tuple.Item2;

                for (var i = 1; i <= 5; i++)
                    AssertSuccess(queue.OfferAsync(i));

                queue.OfferAsync(6).PipeTo(TestActor);
                queue.OfferAsync(7).PipeTo(TestActor);
                ExpectMsg<Status.Failure>().Cause.InnerException.Should().BeOfType<IllegalStateException>();
                probe.RequestNext(1);
                ExpectMsg(Enqueued.Instance);
                queue.Complete();

                probe.Request(6)
                    .ExpectNext(2, 3, 4, 5, 6)
                    .ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_complete_watching_future_with_failure_if_stream_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.Fail)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                queue.WatchCompletionAsync().PipeTo(TestActor);
                queue.OfferAsync(1); // need to wait when first offer is done as initialization can be done in this moment
                queue.OfferAsync(2);
                ExpectMsg<Status.Failure>();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_return_false_when_element_was_not_added_to_buffer()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.DropNew)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = s.ExpectSubscription();

                queue.OfferAsync(1);
                queue.OfferAsync(2).PipeTo(TestActor);
                ExpectMsg<Dropped>();

                sub.Request(1);
                s.ExpectNext(1);
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_wait_when_buffer_is_full_and_backpressure_is_on()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.Backpressure)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = s.ExpectSubscription();
                AssertSuccess(queue.OfferAsync(1));

                queue.OfferAsync(2).PipeTo(TestActor);
                ExpectNoMsg(_pause);

                sub.Request(1);
                s.ExpectNext(1);

                sub.Request(1);
                s.ExpectNext(2);
                ExpectMsg<Enqueued>();

                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_fail_offer_future_when_stream_is_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.DropNew)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = s.ExpectSubscription();

                queue.WatchCompletionAsync().ContinueWith(t => "done").PipeTo(TestActor);
                sub.Cancel();
                ExpectMsg("done");

                queue.OfferAsync(1).ContinueWith(t => t.Exception.Should().BeOfType<IllegalStateException>());
            }, _materializer);
        }

        [Fact]
        public void QueueSource_should_not_share_future_across_materializations()
        {
            var source = Source.Queue<string>(1, OverflowStrategy.Fail);

            var mat1Subscriber = this.CreateSubscriberProbe<string>();
            var mat2Subscriber = this.CreateSubscriberProbe<string>();
            var sourceQueue1 = source.To(Sink.FromSubscriber(mat1Subscriber)).Run(_materializer);
            var sourceQueue2 = source.To(Sink.FromSubscriber(mat2Subscriber)).Run(_materializer);

            mat1Subscriber.EnsureSubscription();
            mat2Subscriber.EnsureSubscription();

            mat1Subscriber.Request(1);
            sourceQueue1.OfferAsync("hello");
            mat1Subscriber.ExpectNext("hello");
            mat1Subscriber.Cancel();
            sourceQueue1.WatchCompletionAsync().ContinueWith(task => task.IsCompleted).PipeTo(TestActor);
            ExpectMsg(true);

            sourceQueue2.WatchCompletionAsync().IsCompleted.Should().BeFalse();
        }

        [Fact]
        public void QueueSource_should_complete_the_stream_when_buffer_is_empty()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.Complete();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

            probe.EnsureSubscription().ExpectComplete();
        }

        [Fact]
        public void QueueSource_should_complete_the_stream_when_buffer_is_full()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.OfferAsync(1);
            source.Complete();
            probe.RequestNext(1).ExpectComplete();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
        }

        [Fact]
        public void QueueSource_should_complete_the_stream_when_buffer_is_full_and_element_is_pending()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Backpressure)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.OfferAsync(1);
            source.OfferAsync(2);
            source.Complete();
            probe.RequestNext(1)
                .RequestNext(2)
                .ExpectComplete();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

        }

        [Fact]
        public void QueueSource_should_complete_the_stream_when_no_buffer_is_used()
        {
            var tuple =
                Source.Queue<int>(0, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.Complete();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

            probe.EnsureSubscription().ExpectComplete();
        }

        [Fact]
        public void QueueSource_should_complete_the_stream_when_no_buffer_is_used_and_element_is_pending()
        {
            var tuple =
                Source.Queue<int>(0, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.OfferAsync(1);
            source.Complete();
            probe.RequestNext(1).ExpectComplete();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
        }

        private static readonly Exception Ex = new Exception("BUH");

        [Fact]
        public void QueueSource_should_fail_the_stream_when_buffer_is_empty()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.Fail(Ex);
            var task = source.WatchCompletionAsync();
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);
        }

        [Fact]
        public void QueueSource_should_fail_the_stream_when_buffer_is_full()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.OfferAsync(1);
            source.Fail(Ex);
            var task = source.WatchCompletionAsync();
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);
        }

        [Fact]
        public void QueueSource_should_fail_the_stream_when_buffer_is_full_and_element_is_pending()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Backpressure)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.OfferAsync(1);
            source.OfferAsync(2);
            source.Fail(Ex);
            var task = source.WatchCompletionAsync();
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);

        }

        [Fact]
        public void QueueSource_should_fail_the_stream_when_no_buffer_is_used()
        {
            var tuple =
                Source.Queue<int>(0, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.Fail(Ex);
            var task = source.WatchCompletionAsync();
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);
        }

        [Fact]
        public void QueueSource_should_fail_the_stream_when_no_buffer_is_used_and_element_is_pending()
        {
            var tuple =
                Source.Queue<int>(0, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            source.OfferAsync(1);
            source.Fail(Ex);
            var task = source.WatchCompletionAsync();
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);
        }
    }
}
