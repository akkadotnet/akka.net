//-----------------------------------------------------------------------
// <copyright file="Streams8314Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Correctness net for the <c>.Async()</c> island boundary ahead of the element-batching change
    /// proposed in https://github.com/akkadotnet/akka.net/issues/8314.
    ///
    /// These tests characterize the observable Reactive-Streams invariants an element-batched
    /// boundary MUST preserve — ordering, drain-before-complete, no-onNext-after-onError,
    /// cancellation propagation, and demand-boundedness. They pass against the current
    /// (one-message-per-element) boundary and act as a regression gate for the batched protocol.
    ///
    /// They assert *invariants*, not incidental current behavior: e.g. the error test tolerates a
    /// batched impl delivering some buffered elements before the failure (RS allows either), but
    /// never an element after the terminal signal.
    /// </summary>
    public class Streams8314Spec : AkkaSpec
    {
        private static readonly TimeSpan NoMsg = TimeSpan.FromMilliseconds(200);

        public ActorMaterializer Materializer { get; }

        public Streams8314Spec(ITestOutputHelper output) : base(output)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact(DisplayName = "Should preserve element order across a single .Async() boundary")]
        public async Task Should_preserve_order_across_boundary()
        {
            const int n = 5000;
            var result = await Source.From(Enumerable.Range(1, n))
                .Async()
                .RunWith(Sink.Seq<int>(), Materializer);

            result.Should().Equal(Enumerable.Range(1, n));
        }

        [Fact(DisplayName = "Should preserve element order across two chained .Async() boundaries")]
        public async Task Should_preserve_order_across_two_boundaries()
        {
            const int n = 5000;
            var result = await Source.From(Enumerable.Range(1, n))
                .Async()
                .Select(x => x)
                .Async()
                .RunWith(Sink.Seq<int>(), Materializer);

            result.Should().Equal(Enumerable.Range(1, n));
        }

        [Fact(DisplayName = "Should drain all pending elements before signalling OnComplete")]
        public async Task Should_drain_pending_before_OnComplete()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream).Async().RunWith(Sink.FromSubscriber(downstream), Materializer);

                // Ample demand present, so the boundary is free to deliver as elements arrive.
                // (SendNext is demand-gated internally — it waits for the boundary's prefetch demand,
                // so we must NOT consume the RequestMore ourselves.)
                downstream.Request(100);

                upstream.SendNext(1);
                upstream.SendNext(2);
                upstream.SendNext(3);
                upstream.SendComplete();

                // All three buffered elements must be delivered, in order, *before* completion —
                // completion must not preempt pending elements.
                await downstream.ExpectNextAsync(1);
                await downstream.ExpectNextAsync(2);
                await downstream.ExpectNextAsync(3);
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact(DisplayName = "Should never signal OnNext after OnError, even with elements pending")]
        public async Task Should_not_emit_after_OnError_with_pending()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream).Async().RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(100);

                // Push three elements, then fail — all issued consecutively so they are in flight/
                // buffered across the boundary when the error overtakes them.
                upstream.SendNext(1);
                upstream.SendNext(2);
                upstream.SendNext(3);
                var boom = new TestException("boom");
                upstream.SendError(boom);

                // Invariant: any elements that still arrive are an in-order prefix of {1,2,3}; the
                // terminal signal is the error; and NOTHING arrives after it. A batched boundary may
                // legitimately deliver some, all, or none of the pending elements first — all
                // RS-compliant — but must never emit an element after the failure.
                // ExpectNextOrErrorAsync returns the raw element (int) for OnNext, or the Exception
                // (the error cause) for OnError.
                var received = new List<int>();
                while (true)
                {
                    var evt = await downstream.ExpectNextOrErrorAsync();
                    if (evt is Exception err)
                    {
                        err.Should().BeSameAs(boom);
                        break;
                    }

                    received.Add((int)evt);
                }

                received.Should().Equal(Enumerable.Range(1, received.Count),
                    "delivered elements must be an in-order prefix of {1,2,3}");
                await downstream.ExpectNoMsgAsync(NoMsg);
            }, Materializer);
        }

        [Fact(DisplayName = "Should deliver elements produced before an upstream stage failure")]
        public async Task Should_deliver_elements_produced_before_stage_failure()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                // A stage emits 1 and 2, then throws on 3 - all upstream of the boundary in one run.
                // The pre-batching design Tell'd each element immediately, so 1 and 2 crossed the
                // boundary before the OnError. Batching must preserve that: the elements produced
                // before the failure are delivered, then the error. (Regression guard for the
                // ClearBatch-on-fail data loss found in review.)
                var boom = new TestException("boom");
                var downstream = this.CreateSubscriberProbe<int>();

                Source.From(new[] { 1, 2, 3 })
                    .Select(x => x == 3 ? throw boom : x)
                    .Async()
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(10);
                await downstream.ExpectNextAsync(1);
                await downstream.ExpectNextAsync(2);
                (await downstream.ExpectErrorAsync()).Should().BeSameAs(boom);
            }, Materializer);
        }

        [Fact(DisplayName = "Should propagate cancellation upstream when cancelled mid-flight")]
        public async Task Should_handle_cancel_mid_batch()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream).Async().RunWith(Sink.FromSubscriber(downstream), Materializer);

                // Ample demand so the sends succeed; we deliberately consume only the first, leaving
                // 2 and 3 in flight/unconsumed when the cancel arrives.
                downstream.Request(100);

                upstream.SendNext(1);
                upstream.SendNext(2);
                upstream.SendNext(3);
                await downstream.ExpectNextAsync(1);

                downstream.Cancel();

                // Cancellation must reach the upstream cleanly despite the pending buffered elements.
                await upstream.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact(DisplayName = "Should never deliver more elements than downstream requested (RS 1.1)")]
        public async Task Should_never_exceed_requested_demand()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var downstream = this.CreateSubscriberProbe<int>();

                // Upstream has 1000 elements always available (so the boundary's prefetch fills its
                // buffer), but downstream only ever requests 3. A manual upstream probe can't be used
                // here because its SendNext is itself demand-gated; an eager Source makes elements
                // unconditionally available so the demand cap is the only thing under test.
                Source.From(Enumerable.Range(1, 1000)).Async()
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(3);
                await downstream.ExpectNextAsync(1);
                await downstream.ExpectNextAsync(2);
                await downstream.ExpectNextAsync(3);

                // The boundary must honor demand: no 4th element despite ~16 sitting in its prefetch
                // buffer and 1000 available upstream. A batched boundary must cap the batch at
                // outstanding demand, not the buffer fill.
                await downstream.ExpectNoMsgAsync(NoMsg);

                downstream.Cancel();
            }, Materializer);
        }
    }
}
