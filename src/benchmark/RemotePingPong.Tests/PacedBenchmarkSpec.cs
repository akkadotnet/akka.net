// -----------------------------------------------------------------------
// <copyright file="PacedBenchmarkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using static RemotePingPong.ValidatedBenchmark;

namespace RemotePingPong.Tests
{
    public sealed class PacedBenchmarkSpec : Akka.TestKit.Xunit.TestKit
    {
        public PacedBenchmarkSpec(ITestOutputHelper output) : base(output: output) { }

        [Fact(DisplayName = "Paced validated messages wait for scheduled offers and preserve bounded credit and final receipts")]
        public async Task Should_PreserveOffersAndCredit_When_EchoesAreDelayed()
        {
            var peer = CreateTestProbe();
            var settings = new Agreement(Guid.NewGuid(), 3, 1, 1, false, 352, RttIntervalMs: 1);
            var payloads = PreparePayloads(3);
            var outcome = new Outcome(3);
            var actor = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, peer.Ref, outcome)));
            actor.Tell(new Prepare(), TestActor);
            await peer.ExpectMsgAsync<Agreement>();
            peer.Send(actor, new Ready(settings));
            await ExpectMsgAsync<bool>();
            var origin = Stopwatch.GetTimestamp() - PacedBenchmark.IntervalTicks(100);
            actor.Tell(new PacedBenchmark.Run(settings.RunId, origin), TestActor);
            await peer.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            actor.Tell(new PacedBenchmark.Release(settings.RunId, 0), TestActor);
            Assert.Equal(payloads[0], await peer.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
            actor.Tell(new PacedBenchmark.Release(settings.RunId, 1), TestActor);
            actor.Tell(new PacedBenchmark.Release(settings.RunId, 2), TestActor);
            await peer.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            peer.Send(actor, payloads[0]);
            Assert.Equal(payloads[1], await peer.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
            peer.Send(actor, payloads[1]);
            Assert.Equal(payloads[2], await peer.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
            peer.Send(actor, payloads[2]);
            Assert.Equal(settings.RunId, (await peer.ExpectMsgAsync<Fence>()).RunId);
            Assert.False(outcome.Task.IsCompleted);
            peer.Send(actor, new Receipt(settings, 3));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal(3, await outcome.Task.WaitAsync(deadline.Token));
            Assert.All(outcome.Samples, sample =>
            {
                Assert.True(sample.DispatchDelay > 0);
                Assert.True(sample.RoundTrip >= sample.DispatchDelay);
            });
        }

        [Theory(DisplayName = "Paced actors reject duplicate, skipped and foreign-phase offers")]
        [InlineData("duplicate")]
        [InlineData("skipped")]
        [InlineData("phase")]
        public async Task Should_RejectOffer_When_PhaseOrIndexIsInvalid(string fault)
        {
            var peer = CreateTestProbe();
            var settings = new Agreement(Guid.NewGuid(), 3, 1, 1, false, 352, RttIntervalMs: 1);
            var payloads = PreparePayloads(3);
            var outcome = new Outcome(3);
            var actor = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, peer.Ref, outcome)));
            actor.Tell(new Prepare(), TestActor);
            await peer.ExpectMsgAsync<Agreement>();
            peer.Send(actor, new Ready(settings));
            await ExpectMsgAsync<bool>();
            actor.Tell(new PacedBenchmark.Run(settings.RunId, Stopwatch.GetTimestamp() - PacedBenchmark.IntervalTicks(100)));
            if (fault == "duplicate")
            {
                actor.Tell(new PacedBenchmark.Release(settings.RunId, 0));
                await peer.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>();
            }
            actor.Tell(new PacedBenchmark.Release(fault == "phase" ? Guid.NewGuid() : settings.RunId,
                fault == "skipped" ? 1 : 0));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await outcome.Task.WaitAsync(deadline.Token));
        }

        [Fact(DisplayName = "A late generator emits every planned offer and retains its actual lateness")]
        public async Task Should_KeepOverdueOffers_When_TheGeneratorIsLate()
        {
            var phase = Guid.NewGuid();
            var actual = new long[3];
            var origin = Stopwatch.GetTimestamp() - PacedBenchmark.IntervalTicks(100);
            var never = new TaskCompletionSource<long>();
            var failed = new TaskCompletionSource<Exception>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var releases = await PacedBenchmark.ReleaseAsync(new[] { TestActor }, phase, origin, actual, 1,
                never.Task, failed.Task, deadline.Token);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(new PacedBenchmark.Release(phase, i), await ExpectMsgAsync<PacedBenchmark.Release>());
                Assert.True(releases.Actual[i] >= origin + PacedBenchmark.IntervalTicks(1) * (i + 1));
            }
        }

        [Theory(DisplayName = "The pacer exits on cancellation, early completion and the first client failure")]
        [InlineData("cancel")]
        [InlineData("completed")]
        [InlineData("failed")]
        public async Task Should_AbortReleasePlan_When_RunCannotComplete(string reason)
        {
            using var cancellation = new CancellationTokenSource();
            var completed = new TaskCompletionSource<long>();
            var failed = new TaskCompletionSource<Exception>();
            if (reason == "cancel") cancellation.Cancel();
            if (reason == "completed") completed.SetResult(0);
            if (reason == "failed") failed.SetResult(new InvalidOperationException("first client failed"));
            var error = await Record.ExceptionAsync(async () => await PacedBenchmark.ReleaseAsync(new[] { TestActor },
                Guid.NewGuid(), Stopwatch.GetTimestamp(), new long[3], 1000, completed.Task, failed.Task, cancellation.Token));
            Assert.NotNull(error);
            if (reason == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
            else Assert.IsType<InvalidOperationException>(error);
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact(DisplayName = "Paced report arithmetic uses every sample and separates release, dispatch and echo latency")]
        public void Should_ReportKnownQuantilesAndRate_When_TimestampsAreKnown()
        {
            var step = PacedBenchmark.IntervalTicks(1);
            var outcomes = new[] { new Outcome(3), new Outcome(3) };
            foreach (var outcome in outcomes)
                for (var i = 0; i < 3; i++) outcome.Samples[i] = new PacedBenchmark.Sample(step * (i + 1), step * (i + 2));
            var report = PacedBenchmark.Summarize(Guid.NewGuid(), outcomes, new PacedBenchmark.Releases(0,
                new[] { step * 2, step * 3, step * 4 }, 1));
            Assert.Equal(6, report.Samples);
            Assert.Equal(2000, report.NominalRttsPerSecond);
            Assert.Equal(2000, report.ActualReleaseRttsPerSecond);
            Assert.Equal(2, report.ReleaseSpanMilliseconds);
            Assert.Equal(1000, report.ReleaseLateness.P99Us);
            Assert.Equal(2000, report.DispatchDelay.P50Us);
            Assert.Equal(1000, report.SendToEcho.P99Us);
            Assert.Equal(4000, report.ScheduledToEcho.P99Us);
        }

        [Fact(DisplayName = "A client failure during final echo draining does not wait for another pending client")]
        public async Task Should_FailFastAfterReleases_When_AnotherClientRemainsPending()
        {
            var failure = new InvalidOperationException("failed after final release");
            var pending = new TaskCompletionSource<long>();
            var completed = Task.WhenAll(Task.FromException<long>(failure), pending.Task);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PacedBenchmark.CompleteAsync(completed, Task.FromResult<Exception>(failure), deadline.Token));
            Assert.Same(failure, observed);
            Assert.False(completed.IsCompleted);
        }

        [Fact(DisplayName = "Continuing scheduled offers cannot postpone the delivery watchdog when credit and echoes stall")]
        public async Task Should_TimeoutDuringOffers_When_NoEchoMakesProgress()
        {
            var peer = CreateTestProbe();
            var settings = new Agreement(Guid.NewGuid(), 20, 1, 1, false, 352, RttIntervalMs: 1000);
            var payloads = PreparePayloads(20);
            var outcome = new Outcome(20);
            var actor = Sys.ActorOf(Props.Create(() => new SenderActor(settings, payloads, peer.Ref, outcome)));
            actor.Tell(new Prepare(), TestActor);
            await peer.ExpectMsgAsync<Agreement>();
            peer.Send(actor, new Ready(settings));
            await ExpectMsgAsync<bool>();
            var origin = Stopwatch.GetTimestamp();
            actor.Tell(new PacedBenchmark.Run(settings.RunId, origin));
            var actual = new long[20];
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await Assert.ThrowsAsync<TimeoutException>(async () => await PacedBenchmark.ReleaseAsync(new[] { actor },
                settings.RunId, origin, actual, 1000, Task.WhenAll(outcome.Task), outcome.Failure, deadline.Token));
            Assert.InRange(actual.Count(ticks => ticks > 0), 2, 19);
            Assert.Equal(payloads[0], await peer.ExpectMsgAsync<RealPayload.V2.RealBenchmarkMessage>());
        }
    }
}
