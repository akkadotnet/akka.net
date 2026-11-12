// -----------------------------------------------------------------------
// <copyright file="ArteryQuicInboundDrainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryQuicInboundDrainSpec
    {
        [Theory(DisplayName = "Peer drain includes in-flight admissions and separates data processing from control EOF")]
        [InlineData(1)]
        [InlineData(8)]
        public async Task Should_WaitForAcceptedDataAndControlSeparately_When_AdmissionsOverlapDrain(int streams)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var control = connection.Register();
            var controlDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = control.Admit(ArteryStreamId.Control, () => controlDone.Task);
            var first = connection.Register();
            var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var data = connection.WaitAsync(false, deadline.Token);
            Assert.False(data.IsCompleted, "a preamble still awaiting classification must remain visible");
            _ = first.Admit(ArteryStreamId.Ordinary, () => firstDone.Task);
            var tails = new ArteryQuicInboundDrain.Stream[streams];
            var tailDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            for (var i = 0; i < tails.Length; i++)
            {
                tails[i] = connection.Register();
                _ = tails[i].Admit(ArteryStreamId.Ordinary, () => tailDone.Task);
            }
            first.Complete(true, true);
            firstDone.SetResult();
            Assert.False(data.IsCompleted, "streams admitted during the wait must also finish");
            foreach (var tail in tails)
                tail.Complete(true, true);
            tailDone.SetResult();
            await data;
            var controls = connection.WaitAsync(true, deadline.Token);
            Assert.False(controls.IsCompleted, "data completion must not stand in for final control processing");
            control.Complete(true, true);
            controlDone.SetResult();
            await controls;
        }

        [Theory(DisplayName = "Peer drain rejects successful tasks without natural EOF and admitted replies")]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public async Task Should_RejectIncompleteBoundary_When_ProcessingTaskSucceeds(bool naturalEof, bool replyAdmitted, bool rejectedEarlier)
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var stream = connection.Register();
            _ = stream.Admit(ArteryStreamId.Control, () => Task.CompletedTask);
            if (rejectedEarlier)
                stream.RejectReply();
            stream.Complete(naturalEof, replyAdmitted);
            await Assert.ThrowsAsync<IOException>(() => connection.WaitAsync(true, CancellationToken.None));
        }

        [Fact(DisplayName = "Peer drain publishes its processing obligation before a synchronous callback can begin draining")]
        public async Task Should_PublishProcessingBeforeCallback_When_MaterializationReenters()
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var stream = connection.Register();
            Task? draining = null;
            await stream.Admit(ArteryStreamId.Ordinary, () =>
            {
                draining = connection.WaitAsync(false, CancellationToken.None);
                Assert.False(draining.IsCompleted);
                stream.Complete(true, true);
                return Task.CompletedTask;
            });
            await draining!;
        }

        [Theory(DisplayName = "Peer drain observes connection cancellation and the original shutdown deadline")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_CancelPendingProcessing_When_ConnectionOrDeadlineStops(bool connectionStops)
        {
            using var connectionStop = new CancellationTokenSource();
            using var deadline = new CancellationTokenSource();
            var connection = new ArteryQuicInboundDrain(connectionStop.Token);
            var stream = connection.Register();
            _ = stream.Admit(ArteryStreamId.Ordinary, () => new TaskCompletionSource().Task);
            var draining = connection.WaitAsync(false, deadline.Token);
            (connectionStops ? connectionStop : deadline).Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draining);
        }

        [Fact(DisplayName = "Peer drain preserves a processing failure instead of confirming task settlement")]
        public async Task Should_FailDrain_When_DataProcessingFaults()
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var stream = connection.Register();
            var error = new IOException("Controlled inbound failure.");
            _ = stream.Admit(ArteryStreamId.Ordinary, () => Task.FromException(error));
            Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => connection.WaitAsync(false, CancellationToken.None)));
        }

        [Fact(DisplayName = "Rejected preambles cancel their admission slot instead of leaving a drain pending")]
        public async Task Should_CancelAdmission_When_PreambleIsRejected()
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var stream = connection.Register();
            var draining = connection.WaitAsync(false, CancellationToken.None);
            stream.CancelAdmission();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draining);
        }

        [Fact(DisplayName = "Peer drain includes a newly registered admission before confirming its snapshot")]
        public async Task Should_ObserveLateAdmissionFailure_When_AnEarlierDataStreamFinishes()
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var first = connection.Register();
            var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = first.Admit(ArteryStreamId.Ordinary, () => firstDone.Task);
            var draining = connection.WaitAsync(false, CancellationToken.None);
            var later = connection.Register();
            later.CancelAdmission();
            first.Complete(true, true);
            firstDone.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draining);
        }

        [Fact(DisplayName = "A synchronous materialization failure cannot leave an admission pending")]
        public async Task Should_CancelProcessingSlot_When_MaterializationThrows()
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var stream = connection.Register();
            var error = new IOException("Controlled materialization failure.");
            Assert.Same(error, Assert.Throws<IOException>(() =>
            {
                _ = stream.Admit(ArteryStreamId.Ordinary, () => throw error);
            }));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.WaitAsync(false, CancellationToken.None));
        }

        [Theory(DisplayName = "A native connection cannot announce a different peer address or incarnation during drain")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_RejectConflictingPeer_When_ShutdownWasAlreadyAnnounced(bool changedAddress)
        {
            var connection = new ArteryQuicInboundDrain(CancellationToken.None);
            var stream = connection.Register();
            var peer = new UniqueAddress(new Address("akka", "peer", "127.0.0.1", 2551), 42);
            stream.AnnouncePeerShutdown(peer);
            stream.AnnouncePeerShutdown(peer);
            var other = changedAddress ? new UniqueAddress(new Address("akka", "other", "127.0.0.1", 2552), 42)
                : new UniqueAddress(peer.Address, 43);
            Assert.Throws<ArteryFramingException>(() => stream.AnnouncePeerShutdown(other));
            Assert.Equal(peer, connection.Peer);
        }
    }
}
