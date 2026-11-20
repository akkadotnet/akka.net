// <copyright file="SubSinkTypedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>

#nullable enable
using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation;

public sealed class SubSinkTypedSpec : AkkaSpec
{
    private readonly ActorMaterializer _materializer;

    public SubSinkTypedSpec(ITestOutputHelper output) : base(output)
    {
        _materializer = Sys.Materializer();
    }

    [Fact(DisplayName = "Should_KeepValueInTypedCarrier_When_SubSinkDeliversElement")]
    public async Task Should_keep_value_in_typed_carrier_when_subsink_delivers_element()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var received = CreateTestProbe();
            var sink = new SubSink<int>("typed-int", message => received.Ref.Tell(message));
            sink.PullSubstream();
            Source.Single(42).RunWith(Sink.FromGraph(sink), _materializer);
            var message = await received.ExpectMsgAsync<IActorSubscriberMessage>();
            message.Should().NotBeOfType<OnNext>("value elements must not use object-shaped OnNext.Element");
            var field = message.GetType().GetField("Element");
            field.Should().NotBeNull();
            field!.FieldType.Should().Be(typeof(int));
            field.GetValue(message).Should().Be(42);
            await received.ExpectMsgAsync<OnComplete>();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_KeepReferenceCarrier_When_SubSinkDeliversReferenceElement")]
    public async Task Should_keep_reference_carrier_when_subsink_delivers_reference_element()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var received = CreateTestProbe();
            var expected = new object();
            var sink = new SubSink<object>("reference", message => received.Ref.Tell(message));
            sink.PullSubstream();
            Source.Single(expected).RunWith(Sink.FromGraph(sink), _materializer);
            (await received.ExpectMsgAsync<OnNext>()).Element.Should().BeSameAs(expected);
            await received.ExpectMsgAsync<OnComplete>();
        }, _materializer);
    }

    [Theory(DisplayName = "Should_DeliverOrderedValuesThroughSubSinkInlet_When_RestartFlowUsesSubFusingOrAsyncBoundary")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_deliver_ordered_values_through_subsink_inlet_when_restart_flow_uses_subfusing_or_async_boundary(bool asyncBoundary)
    {
        var values = Enumerable.Range(0, 64).ToArray();
        await AssertValues(values, asyncBoundary);
        await AssertValues(new[] { new Payload(new object(), 1), new Payload(new object(), 2) }, asyncBoundary);
        await AssertValues(new int?[] { 0, 42 }, asyncBoundary);
        var first = new Segment(new byte[] { 1, 2 });
        var last = first.Append(new byte[] { 3, 4 });
        await AssertValues(new[] { ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length) }, asyncBoundary);
        var reference = new object();
        await AssertValues(new[] { reference, reference }, asyncBoundary);
    }

    private async Task AssertValues<T>(T[] values, bool asyncBoundary)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var flow = RestartFlow.OnFailuresWithBackoff(() => asyncBoundary ? Flow.Create<T>().Async() : Flow.Create<T>(),
                RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0));
            var sink = Source.From(values).Via(flow).RunWith(this.SinkProbe<T>(), _materializer);
            await sink.EnsureSubscriptionAsync();
            await sink.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(50));
            foreach (var value in values)
            {
                await sink.RequestAsync(1);
                var actual = await sink.ExpectNextAsync();
                actual.Should().Be(value);
            }
            await sink.ExpectCompleteAsync();
        }, _materializer);
    }

    [Theory(DisplayName = "Should_PreserveBufferedValueBeforeTermination_When_SubSinkInletCompletesOrFails")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_preserve_buffered_value_before_termination_when_subsink_inlet_completes_or_fails(bool fail)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var failure = new InvalidOperationException("subsink completion");
            var flow = RestartFlow.OnFailuresWithBackoff(() => Flow.Create<int>(),
                RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0));
            var (upstream, downstream) = this.SourceProbe<int>().Via(flow)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(_materializer);
            await downstream.RequestAsync(1);
            await upstream.SendNextAsync(42);
            await downstream.ExpectNextAsync(42);
            if (fail)
            {
                await upstream.SendErrorAsync(failure);
                (await downstream.ExpectErrorAsync()).Should().BeSameAs(failure);
            }
            else
            {
                await upstream.SendCompleteAsync();
                await downstream.ExpectCompleteAsync();
            }
        }, _materializer);
    }

    [Theory(DisplayName = "Should_CancelValueSubstream_When_DownstreamCancelsBeforeOrAfterDelivery")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_cancel_value_substream_when_downstream_cancels_before_or_after_delivery(bool deliver)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var flow = RestartFlow.OnFailuresWithBackoff(() => Flow.Create<Payload>(),
                RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0));
            var (upstream, downstream) = this.SourceProbe<Payload>().Via(flow)
                .ToMaterialized(this.SinkProbe<Payload>(), Keep.Both).Run(_materializer);
            if (deliver)
            {
                await downstream.RequestAsync(1);
                var value = new Payload(new object(), 1);
                await upstream.SendNextAsync(value);
                (await downstream.ExpectNextAsync()).Should().Be(value);
            }
            await downstream.CancelAsync();
            await upstream.ExpectCancellationAsync();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_PreserveNullElementPolicy_When_SourceWouldFeedSubSink")]
    public void Should_preserve_null_element_policy_when_source_would_feed_subsink()
    {
        Action nullable = () => Source.Single<int?>(null);
        Action reference = () => Source.Single<object>(null!);
        nullable.Should().Throw<ArgumentNullException>();
        reference.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "Should_ApplyPendingCancellation_When_SubSinkIsMaterializedAfterPullAndCancel")]
    public async Task Should_apply_pending_cancellation_when_subsink_is_materialized_after_pull_and_cancel()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var received = CreateTestProbe();
            var sink = new SubSink<Payload>("pending-cancel", message => received.Ref.Tell(message));
            sink.PullSubstream();
            sink.CancelSubstream(new InvalidOperationException("cancel before materialization"));
            var upstream = this.SourceProbe<Payload>().To(Sink.FromGraph(sink)).Run(_materializer);
            await upstream.ExpectCancellationAsync();
            await received.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(50));
        }, _materializer);
    }

    [Theory(DisplayName = "Should_PreserveTerminationWithoutDemand_When_SubSinkSourceIsEmptyOrFailed")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_preserve_termination_without_demand_when_subsink_source_is_empty_or_failed(bool fail)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var received = CreateTestProbe();
            var failure = new InvalidOperationException("pending failure");
            var sink = new SubSink<Payload>("empty-or-failed", message => received.Ref.Tell(message));
            var source = fail ? Source.Failed<Payload>(failure) : Source.Empty<Payload>();
            source.RunWith(Sink.FromGraph(sink), _materializer);
            if (fail)
                (await received.ExpectMsgAsync<OnError>()).Cause.Should().BeSameAs(failure);
            else
                await received.ExpectMsgAsync<OnComplete>();
        }, _materializer);
    }

    [Theory(DisplayName = "Should_PreserveValueOrderAcrossYield_When_RestartSubSinkUsesSmallQuota")]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Should_preserve_value_order_across_yield_when_restart_subsink_uses_small_quota(int limit)
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1).WithSyncProcessingLimit(limit).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var expected = Enumerable.Range(0, 256).Select(value => new Payload(new object(), value)).ToArray();
            var flow = RestartFlow.OnFailuresWithBackoff(() => Flow.Create<Payload>(),
                RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0));
            var result = await Source.From(expected).Via(flow).RunWith(Sink.Seq<Payload>(), materializer);
            result.Should().Equal(expected);
        }, materializer);
    }

    [Theory(DisplayName = "Should_PreserveProducerTraceThroughSubSink_When_ListenerAndProducerContextsVary")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Should_preserve_producer_trace_through_subsink_when_listener_and_producer_contexts_vary(bool listener, bool active)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            using var collector = listener ? new StreamsActivityCollector() : null;
            using var producer = new ProducerActivityScope("SubSinkTypedProducer");
            var flow = RestartFlow.OnFailuresWithBackoff(() => Flow.Create<Payload>(),
                RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0));
            var (queue, result) = Source.Queue<Payload>(1, OverflowStrategy.Backpressure).Via(flow)
                .Select(value => (value, Activity.Current?.Context ?? default))
                .ToMaterialized(Sink.First<(Payload, ActivityContext)>(), Keep.Both).Run(_materializer);
            var expected = new Payload(new object(), 42);
            ActivityTraceId traceId = default;
            using (var activity = active ? producer.Start("offer-value") : null)
            {
                if (active)
                {
                    activity.Should().NotBeNull();
                    traceId = activity!.TraceId;
                }
                (await queue.OfferAsync(expected)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            var observed = await result;
            observed.Item1.Should().Be(expected);
            if (listener && active)
                observed.Item2.TraceId.Should().Be(traceId);
        }, _materializer);
    }

    private readonly record struct Payload(object Owner, int Sequence);

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(byte[] bytes) => Memory = bytes;
        public Segment Append(byte[] bytes)
        {
            var next = new Segment(bytes) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
