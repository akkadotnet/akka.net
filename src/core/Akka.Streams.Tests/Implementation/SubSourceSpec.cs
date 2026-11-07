//-----------------------------------------------------------------------
// <copyright file="SubSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation;

public sealed class SubSourceSpec : AkkaSpec
{
    private readonly ActorMaterializer _materializer;

    public SubSourceSpec(ITestOutputHelper output) : base(output)
    {
        _materializer = Sys.Materializer();
    }

    [Fact(DisplayName = "Should_RejectPush_When_SubSourceHasNotBeenMaterialized")]
    public void Should_reject_push_when_subsource_has_not_been_materialized()
    {
        var source = new SubSource<int>("unmaterialized", _ => { });

        Action push = () => source.PushSubstream(0);

        push.Should().Throw<IllegalStateException>().WithMessage("cannot push to uninitialized substream");
    }

    [Theory(DisplayName = "Should_ApplyPendingTermination_When_SubSourceIsMaterialized")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_apply_pending_termination_when_subsource_is_materialized(bool fail)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var source = new SubSource<int>("pending-terminal", _ => { });
            var failure = new InvalidOperationException("pending subsource failure");
            if (fail)
                source.FailSubstream(failure);
            else
                source.CompleteSubstream();

            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await sink.EnsureSubscriptionAsync();

            if (fail)
                (await sink.ExpectErrorAsync()).Should().BeSameAs(failure);
            else
                await sink.ExpectCompleteAsync();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_ApplyPendingTimeout_When_SubSourceIsMaterialized")]
    public async Task Should_apply_pending_timeout_when_subsource_is_materialized()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var source = new SubSource<int>("timed-out", _ => { });
            source.Timeout(TimeSpan.FromSeconds(1)).Should().BeTrue();
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await sink.EnsureSubscriptionAsync();

            (await sink.ExpectErrorAsync()).Should().BeOfType<SubscriptionTimeoutException>();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_RejectSecondMaterialization_When_FirstSubSourceIsRunning")]
    public async Task Should_reject_second_materialization_when_first_subsource_is_running()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<int>("single-materialization", command => commands.Ref.Tell(command));
            var first = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await first.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();

            var second = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await second.EnsureSubscriptionAsync();
            (await second.ExpectErrorAsync()).Should().BeOfType<IllegalStateException>();

            source.PushSubstream(0);
            await first.ExpectNextAsync(0);
            source.CompleteSubstream();
            await first.ExpectCompleteAsync();
        }, _materializer);
    }

    [Theory(DisplayName = "Should_DeliverDefaultValueBeforeTermination_When_SubSourceIsRunning")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_deliver_default_value_before_termination_when_subsource_is_running(bool fail)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<int>("value-terminal", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await sink.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();

            source.PushSubstream(0);
            await sink.ExpectNextAsync(0);
            var failure = new InvalidOperationException("subsource failure after value");
            if (fail)
            {
                source.FailSubstream(failure);
                (await sink.ExpectErrorAsync()).Should().BeSameAs(failure);
            }
            else
            {
                source.CompleteSubstream();
                await sink.ExpectCompleteAsync();
            }
        }, _materializer);
    }

    [Theory(DisplayName = "Should_DeliverSequenceContents_When_SubSourceCarriesValueTypeElements")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_deliver_sequence_contents_when_subsource_carries_value_type_elements(bool empty)
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<ReadOnlySequence<byte>>("sequence", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<ReadOnlySequence<byte>>(), _materializer);
            await sink.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();
            var first = new Segment(new byte[] { 1, 2 });
            var last = first.Append(new byte[] { 3, 4 });
            var value = empty ? default : new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

            source.PushSubstream(value);
            var received = await sink.ExpectNextAsync();

            received.ToArray().Should().Equal(empty ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 });
            received.IsSingleSegment.Should().Be(empty);
            source.CompleteSubstream();
            await sink.ExpectCompleteAsync();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_DeliverQueuedValueBeforeCompletion_When_SubSourceCompletesImmediatelyAfterPush")]
    public async Task Should_deliver_queued_value_before_completion_when_subsource_completes_immediately_after_push()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<int>("queued-completion", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await sink.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();

            source.PushSubstream(0);
            source.CompleteSubstream();

            await sink.ExpectNextAsync(0);
            await sink.ExpectCompleteAsync();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_PreserveFailureIdentity_When_SubSourceFailsImmediatelyAfterPush")]
    public async Task Should_preserve_failure_identity_when_subsource_fails_immediately_after_push()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<int>("queued-failure", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await sink.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();
            var failure = new InvalidOperationException("queued subsource failure");

            source.PushSubstream(0);
            source.FailSubstream(failure);

            // Failure can overtake a value buffered at the subscriber boundary. Preserve that
            // eager-failure contract rather than require delivery of an unobserved value.
            var first = await sink.ExpectEventAsync();
            if (first is TestSubscriber.OnNext<int> next)
            {
                next.Element.Should().Be(0);
                (await sink.ExpectErrorAsync()).Should().BeSameAs(failure);
                Output?.WriteLine("Queued push/failure: value reached subscriber before failure.");
            }
            else
            {
                first.Should().BeOfType<TestSubscriber.OnError>().Which.Cause.Should().BeSameAs(failure);
                Output?.WriteLine("Queued push/failure: eager failure discarded the pending value.");
            }
        }, _materializer);
    }

    [Fact(DisplayName = "Should_PreserveCancellationCauseAndStop_When_TypedSubSourceIsCancelled")]
    public async Task Should_preserve_cancellation_cause_and_stop_when_typed_subsource_is_cancelled()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<int>("typed-cancellation", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int>(), _materializer);
            await sink.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();
            source.PushSubstream(0);
            await sink.ExpectNextAsync(0);
            var cause = new InvalidOperationException("typed subsource cancellation");

            ((ISubscriptionWithCancelException)sink.Subscription).Cancel(cause);

            var command = await commands.FishForMessageAsync(message => message is SubSink.Cancel);
            ((SubSink.Cancel)command).Cause.Should().BeSameAs(cause);
        }, _materializer);
    }

    [Fact(DisplayName = "Should_PreserveReferenceIdentityAndCancellation_When_SubSourceCarriesReferenceElements")]
    public async Task Should_preserve_reference_identity_and_cancellation_when_subsource_carries_reference_elements()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<object>("reference", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<object>(), _materializer);
            await sink.RequestAsync(1);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();
            var value = new object();

            source.PushSubstream(value);
            (await sink.ExpectNextAsync()).Should().BeSameAs(value);
            await sink.CancelAsync();

            await commands.FishForMessageAsync(message => message is SubSink.Cancel);
        }, _materializer);
    }

    [Fact(DisplayName = "Should_RejectNullableDefaultAtPush_When_SubSourceReceivesNullElement")]
    public async Task Should_reject_nullable_default_at_push_when_subsource_receives_null_element()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var commands = CreateTestProbe();
            var source = new SubSource<int?>("nullable", command => commands.Ref.Tell(command));
            var sink = Source.FromGraph(source).RunWith(this.SinkProbe<int?>(), _materializer);
            await sink.RequestAsync(2);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();
            source.PushSubstream(7);
            await sink.ExpectNextAsync(7);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();

            source.PushSubstream(default);

            (await sink.ExpectErrorAsync()).Should().BeOfType<ArgumentNullException>();
        }, _materializer);
    }

    [Fact(DisplayName = "Should_PreserveProducerTrace_When_TypedSubSourceCallbackDeliversValue")]
    public async Task Should_preserve_producer_trace_when_typed_subsource_callback_delivers_value()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            using var collector = new StreamsActivityCollector();
            using var producer = new ProducerActivityScope("SubSourceTypedProducer");
            var commands = CreateTestProbe();
            var source = new SubSource<int>("traced-value", command => commands.Ref.Tell(command));
            var result = Source.FromGraph(source)
                .Select(_ => Activity.Current?.Context ?? default)
                .RunWith(Sink.First<ActivityContext>(), _materializer);
            await commands.ExpectMsgAsync<SubSink.RequestOne>();
            ActivityTraceId traceId;
            using (var activity = producer.Start("subsource.push"))
            {
                activity.Should().NotBeNull();
                traceId = activity!.TraceId;
                source.PushSubstream(0);
            }

            var observed = await result;
            observed.TraceId.Should().Be(traceId);
            observed.SpanId.Should().NotBe(default(ActivitySpanId));
        }, _materializer);
    }

    [Fact(DisplayName = "Should_DeliverValuesAcrossRestart_When_RestartFlowFailsAndRematerializes")]
    public async Task Should_deliver_values_across_restart_when_restart_flow_fails_and_rematerializes()
    {
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var created = CreateTestProbe();
            var attempts = 0;
            var flow = RestartFlow.OnFailuresWithBackoff(() =>
            {
                created.Ref.Tell(++attempts);
                return Flow.Create<int>().Select(value => value == -1
                    ? throw new InvalidOperationException("restart typed substream")
                    : value);
            }, RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 0));
            var (upstream, downstream) = this.SourceProbe<int>().Via(flow)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(_materializer);
            await created.ExpectMsgAsync(1);
            await downstream.RequestAsync(2);
            await upstream.SendNextAsync(0);
            await downstream.ExpectNextAsync(0);

            await upstream.SendNextAsync(-1);
            await created.ExpectMsgAsync(2);
            await upstream.SendNextAsync(42);
            await downstream.ExpectNextAsync(42);
            await upstream.SendCompleteAsync();
            await downstream.ExpectCompleteAsync();
        }, _materializer);
    }

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
