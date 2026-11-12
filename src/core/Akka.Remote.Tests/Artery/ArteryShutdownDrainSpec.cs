// -----------------------------------------------------------------------
// <copyright file="ArteryShutdownDrainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryShutdownDrainSpec
    {
        private static readonly UniqueAddress Local = new(new Address("akka", "local", "127.0.0.1", 2551), 11);
        private static readonly UniqueAddress Peer = new(new Address("akka", "peer", "127.0.0.1", 2552), 42);

        [Theory(DisplayName = "Shutdown receipts reject unknown or mismatched identities without consuming the expected receipt")]
        [InlineData("nonce")]
        [InlineData("destination")]
        [InlineData("origin")]
        [InlineData("address")]
        [InlineData("incarnation")]
        public async Task Should_RejectReceipt_When_IdentityDoesNotMatch(string mismatch)
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(Peer.Address);
            registry.CompleteHandshake(Peer.Address, Peer);
            var drain = new ArteryShutdownDrain();
            var target = drain.Register(association, ArteryStreamId.Ordinary);
            var current = Peer;
            drain.Begin(Local, candidate => candidate.Equals(current));
            var acknowledgement = new ArteryDrainAck(Peer, Local.Uid, target.Id);
            var originUid = Peer.Uid;
            switch (mismatch)
            {
                case "nonce": acknowledgement = acknowledgement with { Id = target.Id + 1 }; break;
                case "destination": acknowledgement = acknowledgement with { ToUid = Local.Uid + 1 }; break;
                case "origin": originUid++; break;
                case "address": acknowledgement = acknowledgement with { From = new UniqueAddress(Local.Address, Peer.Uid) }; break;
                case "incarnation": current = new UniqueAddress(Peer.Address, Peer.Uid + 1); break;
            }
            drain.Acknowledge(originUid, acknowledgement).Should().BeFalse();
            drain.WhenReceived.IsCompleted.Should().BeFalse();
            drain.Outstanding.Should().Be(1);
            current = Peer;
            drain.Acknowledge(Peer.Uid, new ArteryDrainAck(Peer, Local.Uid, target.Id)).Should().BeTrue();
            await drain.WaitForReceiptsAsync(CancellationToken.None);
            drain.Acknowledge(Peer.Uid, new ArteryDrainAck(Peer, Local.Uid, target.Id)).Should().BeFalse();
            drain.Outstanding.Should().Be(0);
            drain.Close();
        }

        [Theory(DisplayName = "Shutdown cannot confirm active streams whose peer identity is unknown or no longer current")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_RecordUnconfirmableStream_When_PeerCannotBeConfirmed(bool known)
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(Peer.Address);
            if (known)
                registry.CompleteHandshake(Peer.Address, Peer);
            var drain = new ArteryShutdownDrain();
            drain.Register(association, ArteryStreamId.Control);
            drain.Begin(Local, _ => !known).Should().BeEmpty();
            drain.Unconfirmable.Should().Be(1, "an empty receipt set must not make an unknown active stream confirmed");
            drain.Close();
        }

        [Fact(DisplayName = "Shutdown retains missing receipts after a local stream stops and rejects late registrations")]
        public async Task Should_RetainReceiptDebt_When_LocalWriterStops()
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(Peer.Address);
            registry.CompleteHandshake(Peer.Address, Peer);
            var drain = new ArteryShutdownDrain();
            var target = drain.Register(association, ArteryStreamId.Ordinary);
            drain.Begin(Local, _ => true);
            drain.Remove(target);
            drain.Outstanding.Should().Be(1);
            Assert.Throws<InvalidOperationException>(() => drain.Register(association, ArteryStreamId.Ordinary));
            drain.Close();
            target.Request.Should().BeNull("a deadline must invalidate markers that have not been encoded yet");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain.WhenReceived);
            drain.Acknowledge(Peer.Uid, new ArteryDrainAck(Peer, Local.Uid, target.Id)).Should().BeFalse();
        }

        [Fact(DisplayName = "A receipt from a replaced stream cannot confirm its new materialization")]
        public async Task Should_RejectOldNonce_When_StreamWasReplacedBeforeShutdown()
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(Peer.Address);
            registry.CompleteHandshake(Peer.Address, Peer);
            var drain = new ArteryShutdownDrain();
            var retired = drain.Register(association, ArteryStreamId.Ordinary);
            drain.Remove(retired);
            var replacement = drain.Register(association, ArteryStreamId.Ordinary);
            replacement.Id.Should().NotBe(retired.Id);
            drain.Begin(Local, _ => true).Should().ContainSingle().Which.Should().BeSameAs(replacement);
            drain.Acknowledge(Peer.Uid, new ArteryDrainAck(Peer, Local.Uid, retired.Id)).Should().BeFalse();
            drain.WhenReceived.IsCompleted.Should().BeFalse();
            drain.Outstanding.Should().Be(1);
            drain.Acknowledge(Peer.Uid, new ArteryDrainAck(Peer, Local.Uid, replacement.Id)).Should().BeTrue();
            await drain.WhenReceived;
            drain.Close();
        }

        [Theory(DisplayName = "Shutdown receipt confirmation observes cancellation even when receipts are already complete")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_RejectExpiredDeadline_When_ReceiptBecomesComplete(bool cancelBeforeReceipt)
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(Peer.Address);
            registry.CompleteHandshake(Peer.Address, Peer);
            var drain = new ArteryShutdownDrain();
            var target = drain.Register(association, ArteryStreamId.Ordinary);
            drain.Begin(Local, _ => true);
            using var deadline = new CancellationTokenSource();
            if (cancelBeforeReceipt)
                deadline.Cancel();
            drain.Acknowledge(Peer.Uid, new ArteryDrainAck(Peer, Local.Uid, target.Id)).Should().BeTrue();
            if (!cancelBeforeReceipt)
                deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain.WaitForReceiptsAsync(deadline.Token));
            drain.Close();
        }
    }
}
