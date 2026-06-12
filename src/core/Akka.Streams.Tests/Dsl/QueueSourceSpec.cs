//-----------------------------------------------------------------------
// <copyright file="QueueSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Dropped = Akka.Streams.QueueOfferResult.Dropped;
using Enqueued = Akka.Streams.QueueOfferResult.Enqueued;
using QueueClosed = Akka.Streams.QueueOfferResult.QueueClosed;

namespace Akka.Streams.Tests.Dsl
{
    public class QueueSourceSpec : AkkaSpec
    {
        private sealed class QueueCompletionRefs
        {
            public QueueCompletionRefs(WeakReference queue, WeakReference completionTask)
            {
                Queue = queue;
                CompletionTask = completionTask;
            }

            public WeakReference Queue { get; }

            public WeakReference CompletionTask { get; }
        }

        private static readonly FieldInfo QueueCompletionField = typeof(QueueSource<int>.Materialized)
            .GetField("_completion", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private readonly ActorMaterializer _materializer;
        private readonly TimeSpan _pause = TimeSpan.FromMilliseconds(300);

        public QueueSourceSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<IReadOnlyList<QueueCompletionRefs>> CreateIssue8210FaultedCompletionTaskRefsAsync(int count)
        {
            var refs = new List<QueueCompletionRefs>(count);

            for (var i = 0; i < count; i++)
            {
                var tempMat = ActorMaterializer.Create(Sys, ActorMaterializerSettings.Create(Sys));
                var queue = (QueueSource<int>.Materialized)Source.Queue<int>(1, OverflowStrategy.Fail)
                    .To(Sink.Ignore<int>())
                    .Run(tempMat);
                var completion = (TaskCompletionSource<object>)QueueCompletionField.GetValue(queue)!;
                var completionTask = completion.Task;

                refs.Add(new QueueCompletionRefs(new WeakReference(queue), new WeakReference(completionTask)));

                await Task.Delay(50);
                tempMat.Shutdown();

                for (var attempt = 0; attempt < 30 && !completionTask.IsCompleted; attempt++)
                    await Task.Delay(10);

                completionTask.IsFaulted.Should().BeTrue(
                    "QueueSource should fault its internal completion task when the stage is abruptly stopped");
            }

            return refs;
        }

        private static void AssertSuccess(Task<IQueueOfferResult> task)
        {
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().Be(Enqueued.Instance);
        }

        private async Task AssertCanceledWithToken(Task<IQueueOfferResult> task, CancellationToken cancellationToken)
        {
            await AwaitAssertAsync(
                () => task.IsCanceled.Should().BeTrue(),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(50));

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            exception.CancellationToken.Should().Be(cancellationToken);
        }

        [Fact]
        public async Task QueueSource_should_emit_received_message_to_the_stream()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var queue =
                Source.Queue<int>(10, OverflowStrategy.Fail).To(Sink.FromSubscriber(s)).Run(_materializer);
            var sub = await s.ExpectSubscriptionAsync();

            sub.Request(2);
            AssertSuccess(queue.OfferAsync(1));
            await s.ExpectNextAsync(1);
            AssertSuccess(queue.OfferAsync(2));
            await s.ExpectNextAsync(2);
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
                .Should().Throw<IllegalStateException>()
                .And.Message.Should()
                .Contain("have to wait");

            probe.RequestNext().Should().Be(42);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().Be(Enqueued.Instance);

        }

        [Fact]
        public async Task QueueSource_should_buffer_when_needed()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            var queue =
                Source.Queue<int>(100, OverflowStrategy.DropHead)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);
            var sub = await s.ExpectSubscriptionAsync();

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
        public async Task QueueSource_should_not_fail_when_0_buffer_space_and_demand_is_signalled()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(0, OverflowStrategy.DropHead)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();

                sub.Request(1);
                AssertSuccess(queue.OfferAsync(1));
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_wait_for_demand_when_buffer_is_0()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(0, OverflowStrategy.DropHead)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.OfferAsync(1).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await ExpectNoMsgAsync(_pause);
                sub.Request(1);
                await ExpectMsgAsync<Enqueued>();
                await s.ExpectNextAsync(1);
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_finish_offer_and_complete_futures_when_stream_completed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(0, OverflowStrategy.DropHead)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.WatchCompletionAsync()
                    .ContinueWith(_ => "done", TaskContinuationOptions.OnlyOnRanToCompletion)
                    .PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.OfferAsync(1).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await ExpectNoMsgAsync(_pause);

                sub.Cancel();

                ExpectMsgAllOf(new object[] { QueueClosed.Instance, "done" });
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_fail_stream_on_buffer_overflow_in_fail_mode()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.Fail)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                await s.ExpectSubscriptionAsync();

                await queue.OfferAsync(1);
                await queue.OfferAsync(1);
                s.ExpectError();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_remember_pull_from_downstream_to_send_offered_element_immediately()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var probe = CreateTestProbe();
                var queue = TestSourceStage<int, ISourceQueueWithComplete<int>>.Create(
                    new QueueSource<int>(1, OverflowStrategy.DropHead), probe)
                    .To(Sink.FromSubscriber(s))
                    .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();

                sub.Request(1);
                await probe.ExpectMsgAsync<GraphStageMessages.Pull>();
                AssertSuccess(queue.OfferAsync(1));
                await s.ExpectNextAsync(1);
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_fail_offer_future_if_user_does_not_wait_in_backpressure_mode()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var tuple =                                                                             
                Source.Queue<int>(5, OverflowStrategy.Backpressure)                                                                                 
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)                                                                                 
                .Run(_materializer);
                var queue = tuple.Item1;
                var probe = tuple.Item2;

                for (var i = 1; i <= 5; i++)
                    AssertSuccess(queue.OfferAsync(i));

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.OfferAsync(6).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.OfferAsync(7).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                var expect = await ExpectMsgAsync<Status.Failure>();
                expect.Cause.Should().BeOfType<IllegalStateException>();
                await probe.RequestNextAsync(1);
                await ExpectMsgAsync(Enqueued.Instance);
                queue.Complete();

                await probe.Request(6)
                    .ExpectNext(2, 3, 4, 5, 6)
                    .ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_complete_watching_future_with_failure_if_stream_failed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.Fail)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.WatchCompletionAsync().PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await queue.OfferAsync(1); // need to wait when first offer is done as initialization can be done in this moment
                await queue.OfferAsync(2);
                await ExpectMsgAsync<Status.Failure>();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_complete_watching_future_with_failure_if_materializer_shut_down()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var tempMap = ActorMaterializer.Create(Sys, ActorMaterializerSettings.Create(Sys)); // need to create a new materializer to be able to shutdown it
                var s = this.CreateManualSubscriberProbe<int>();
                var queue = Source.Queue<int>(1, OverflowStrategy.Fail)
                    .To(Sink.FromSubscriber(s))
                    .Run(tempMap);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.WatchCompletionAsync().PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                tempMap.Shutdown();
                await ExpectMsgAsync<Status.Failure>();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_not_trigger_UnobservedTaskException_when_completion_is_not_watched_and_materializer_is_shut_down()
        {
            var unobserved = new TaskCompletionSource<AggregateException>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
            {
                try
                {
                    if (args.Exception.Flatten().InnerExceptions.OfType<StreamDetachedException>().Any())
                        unobserved.TrySetResult(args.Exception);
                }
                finally
                {
                    args.SetObserved();
                }
            };

            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                var refs = await this.AssertAllStagesStoppedAsync(() => CreateIssue8210FaultedCompletionTaskRefsAsync(10), _materializer);

                for (var i = 0; i < 30 && !unobserved.Task.IsCompleted && refs.Any(r => r.Queue.IsAlive || r.CompletionTask.IsAlive); i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(100);
                }

                refs.Count(r => !r.Queue.IsAlive).Should().BeGreaterThan(0,
                    "the discarded Source.Queue materialized values should become collectible during the repro");

                refs.Count(r => !r.CompletionTask.IsAlive).Should().BeGreaterThan(0,
                    "the unwatched completion tasks should become collectible during the repro");

                unobserved.Task.IsCompleted.Should().BeFalse(
                    "discarding Source.Queue completion should not leave an internal faulted task unobserved during abrupt shutdown");
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        [Fact]
        public async Task QueueSource_should_return_false_when_element_was_not_added_to_buffer()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.DropNew)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();

                await queue.OfferAsync(1);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.OfferAsync(2).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await ExpectMsgAsync<Dropped>();

                sub.Request(1);
                await s.ExpectNextAsync(1);
                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_wait_when_buffer_is_full_and_backpressure_is_on()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.Backpressure)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();
                AssertSuccess(queue.OfferAsync(1));

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.OfferAsync(2).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                await ExpectNoMsgAsync(_pause);

                sub.Request(1);
                await s.ExpectNextAsync(1);

                sub.Request(1);
                await s.ExpectNextAsync(2);
                await ExpectMsgAsync<Enqueued>();

                sub.Cancel();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_offer_with_cancellation_token_none()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(0, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);
                var queue = Assert.IsAssignableFrom<ISourceQueueWithComplete<int>>(source);

                var offer = queue.OfferAsync(1, CancellationToken.None);

                await probe.RequestNextAsync(1);
                AssertSuccess(offer);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_cancel_pending_offer_when_backpressured_buffer_is_full()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(1, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                AssertSuccess(source.OfferAsync(1));

                using var cts = new CancellationTokenSource();
                var offer = source.OfferAsync(2, cts.Token);
                await ExpectNoMsgAsync(_pause);

                cts.Cancel();
                await AssertCanceledWithToken(offer, cts.Token);

                await probe.RequestNextAsync(1);
                probe.Request(1);
                await probe.ExpectNoMsgAsync(_pause);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_cancel_pending_offer_when_backpressured_without_buffer()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(0, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                using var cts = new CancellationTokenSource();
                var offer = source.OfferAsync(1, cts.Token);
                await ExpectNoMsgAsync(_pause);

                cts.Cancel();
                await AssertCanceledWithToken(offer, cts.Token);

                probe.Request(1);
                await probe.ExpectNoMsgAsync(_pause);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_accept_next_backpressured_offer_after_pending_offer_is_canceled()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(1, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                AssertSuccess(source.OfferAsync(1));

                using var canceled = new CancellationTokenSource();
                var canceledOffer = source.OfferAsync(2, canceled.Token);
                canceled.Cancel();
                await AssertCanceledWithToken(canceledOffer, canceled.Token);

                var nextOffer = source.OfferAsync(3);

                await probe.RequestNextAsync(1);
                AssertSuccess(nextOffer);

                await probe.RequestNextAsync(3);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_not_enqueue_offer_when_token_is_already_canceled()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(0, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var canceledOffer = source.OfferAsync(1, cts.Token);
                await AssertCanceledWithToken(canceledOffer, cts.Token);

                var offer = source.OfferAsync(2);
                await probe.RequestNextAsync(2);
                AssertSuccess(offer);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_ignore_cancellation_after_offer_was_enqueued()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(1, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                using var cts = new CancellationTokenSource();
                var offer = source.OfferAsync(1, cts.Token);
                AssertSuccess(offer);

                cts.Cancel();

                offer.Status.Should().Be(TaskStatus.RanToCompletion);
                offer.Result.Should().Be(Enqueued.Instance);
                await probe.RequestNextAsync(1);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_ignore_cancellation_after_complete_was_requested_for_pending_offer()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(0, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                using var cts = new CancellationTokenSource();
                var offer = source.OfferAsync(1, cts.Token);
                source.Complete();
                cts.Cancel();

                await probe.RequestNextAsync(1);
                AssertSuccess(offer);
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_drop_new_offers_after_complete_was_requested_while_draining()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var (source, probe) =
                    Source.Queue<int>(0, OverflowStrategy.Backpressure)
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                var pendingOffer = source.OfferAsync(1);
                source.Complete();

                var droppedOffer = await source.OfferAsync(2);
                droppedOffer.Should().Be(Dropped.Instance);

                await probe.RequestNextAsync(1);
                AssertSuccess(pendingOffer);
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_cancel_offer_when_token_fires_before_stage_processes_offer()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                using var mapStarted = new ManualResetEventSlim();
                using var releaseMap = new ManualResetEventSlim();

                var (source, probe) =
                    Source.Queue<int>(0, OverflowStrategy.Backpressure)
                        .Select(element =>
                        {
                            mapStarted.Set();
                            releaseMap.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                            return element;
                        })
                        .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                        .Run(_materializer);

                probe.Request(1);
                var firstOffer = source.OfferAsync(1);
                mapStarted.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

                using var cts = new CancellationTokenSource();
                var canceledOffer = source.OfferAsync(2, cts.Token);
                cts.Cancel();

                releaseMap.Set();

                await probe.ExpectNextAsync(1);
                AssertSuccess(firstOffer);
                await AssertCanceledWithToken(canceledOffer, cts.Token);

                probe.Request(1);
                await probe.ExpectNoMsgAsync(_pause);

                source.Complete();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_fail_offer_future_when_stream_is_completed()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s = this.CreateManualSubscriberProbe<int>();
                var queue =
                    Source.Queue<int>(1, OverflowStrategy.DropNew)
                        .To(Sink.FromSubscriber(s))
                        .Run(_materializer);
                var sub = await s.ExpectSubscriptionAsync();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                queue.WatchCompletionAsync().ContinueWith(_ => Done.Instance).PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                sub.Cancel();
                await ExpectMsgAsync(Done.Instance);

                var exception = Record.ExceptionAsync(async () => await queue.OfferAsync(1)).Result;
                exception.Should().BeOfType<StreamDetachedException>();
            }, _materializer);
        }

        [Fact]
        public async Task QueueSource_should_not_share_future_across_materializations()
        {
            var source = Source.Queue<string>(1, OverflowStrategy.Fail);

            var mat1Subscriber = this.CreateSubscriberProbe<string>();
            var mat2Subscriber = this.CreateSubscriberProbe<string>();
            var sourceQueue1 = source.To(Sink.FromSubscriber(mat1Subscriber)).Run(_materializer);
            var sourceQueue2 = source.To(Sink.FromSubscriber(mat2Subscriber)).Run(_materializer);

            await mat1Subscriber.EnsureSubscriptionAsync();
            await mat2Subscriber.EnsureSubscriptionAsync();

            mat1Subscriber.Request(1);
            await sourceQueue1.OfferAsync("hello");
            await mat1Subscriber.ExpectNextAsync("hello");
            mat1Subscriber.Cancel();
            await sourceQueue1.WatchCompletionAsync().ContinueWith(task => task.IsCompleted).PipeTo(TestActor);
            await ExpectMsgAsync(true);

            sourceQueue2.WatchCompletionAsync().IsCompleted.Should().BeFalse();
        }

        [Fact]
        public async Task QueueSource_should_complete_the_stream_when_buffer_is_empty()
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

            await probe.EnsureSubscription().ExpectCompleteAsync();
        }

        [Fact]
        public async Task QueueSource_should_complete_the_stream_when_buffer_is_full()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            await source.OfferAsync(1);
            source.Complete();
            await probe.RequestNext(1).ExpectCompleteAsync();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
        }

        [Fact]
        public async Task QueueSource_should_complete_the_stream_when_buffer_is_full_and_element_is_pending()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Backpressure)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            source.OfferAsync(1);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            source.OfferAsync(2);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            source.Complete();
            await probe.RequestNext(1)
                .RequestNext(2)
                .ExpectCompleteAsync();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

        }

        [Fact]
        public async Task QueueSource_should_complete_the_stream_when_no_buffer_is_used()
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

            await probe.EnsureSubscription().ExpectCompleteAsync();
        }

        [Fact]
        public async Task QueueSource_should_complete_the_stream_when_no_buffer_is_used_and_element_is_pending()
        {
            var tuple =
                Source.Queue<int>(0, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            source.OfferAsync(1);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            source.Complete();
            await probe.RequestNext(1).ExpectCompleteAsync();
            var task = source.WatchCompletionAsync();
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
        }

        private static readonly Exception Ex = new("BUH");

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
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).Should().Throw<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);
        }

        [Fact]
        public async Task QueueSource_should_fail_the_stream_when_buffer_is_full()
        {
            var tuple =
                Source.Queue<int>(1, OverflowStrategy.Fail)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(_materializer);
            var source = tuple.Item1;
            var probe = tuple.Item2;

            await source.OfferAsync(1);
            source.Fail(Ex);
            var task = source.WatchCompletionAsync();
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).Should().Throw<Exception>().And.Should().Be(Ex);
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
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).Should().Throw<Exception>().And.Should().Be(Ex);
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
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).Should().Throw<Exception>().And.Should().Be(Ex);
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
            task.Invoking(_ => _.Wait(TimeSpan.FromSeconds(3))).Should().Throw<Exception>().And.Should().Be(Ex);
            probe.EnsureSubscription().ExpectError().Should().Be(Ex);
        }
    }
}
