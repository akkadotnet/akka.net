//-----------------------------------------------------------------------
// <copyright file="AssociationRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Unit tests for the outbound-stream reconnect DECISION logic (design.md group 9,
    /// "Association outbound-stream lifecycle: reconnect") that lives on
    /// <see cref="Association"/>/<see cref="AssociationRegistry"/>: <c>ShouldRestartOutbound</c>/
    /// <c>ShouldRestartControl</c>, the materialize-once gate reset, and the shutdown latch. No
    /// <c>ActorSystem</c>, TCP, or streams are needed here -- these are pure state-machine checks;
    /// the actual restart SCHEDULING (backoff timer, re-materialization) is exercised end-to-end by
    /// <c>ArteryReconnectSpec</c> instead.
    /// </summary>
    public class AssociationRestartSpec
    {
        private static Address NewRemote() => new("akka", "remote-sys", "remote-host", 2552);

        [Fact(DisplayName = "ShouldRestartOutbound/ShouldRestartControl are both true before any handshake has completed (Associating, nothing quarantined, not shut down)")]
        public void ShouldRestart_should_be_true_before_handshake()
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(NewRemote());

            association.ShouldRestartOutbound().Should().BeTrue();
            association.ShouldRestartControl().Should().BeTrue();
        }

        [Fact(DisplayName = "Quarantining the CURRENT peer uid stops ShouldRestartOutbound but NOT ShouldRestartControl (design.md group 9: control pierces quarantine, ordinary stays gated)")]
        public void Quarantine_of_current_uid_gates_ordinary_restart_only()
        {
            var registry = new AssociationRegistry();
            var remote = NewRemote();
            var association = registry.AssociationFor(remote);
            var peer = new UniqueAddress(remote, 42L);
            registry.CompleteHandshake(remote, peer);

            association.ShouldRestartOutbound().Should().BeTrue("not quarantined yet");
            association.ShouldRestartControl().Should().BeTrue();

            association.Quarantine(42L).Should().BeTrue();

            association.ShouldRestartOutbound().Should().BeFalse(
                "Send() already gates ordinary sends for a quarantined uid -- reconnecting the ordinary stream would only waste a connection");
            association.ShouldRestartControl().Should().BeTrue(
                "the control stream restarts regardless of quarantine -- it is what lets the quarantine notice/give-up-system-message machinery keep working");
        }

        [Fact(DisplayName = "A stale (superseded) uid's quarantine does not gate restart for a NEW, non-quarantined incarnation")]
        public void Quarantine_of_stale_uid_does_not_gate_new_incarnation()
        {
            var registry = new AssociationRegistry();
            var remote = NewRemote();
            var association = registry.AssociationFor(remote);

            var oldPeer = new UniqueAddress(remote, 1L);
            registry.CompleteHandshake(remote, oldPeer);
            association.Quarantine(1L).Should().BeTrue();
            association.ShouldRestartOutbound().Should().BeFalse("the old (current-at-the-time) uid is quarantined");

            // Remote restarts under a NEW uid -- a genuinely new incarnation. The OLD uid stays
            // quarantined (design.md: quarantine is UID-scoped, not auto-inherited across a uid
            // change), but the NEW uid is not, so ordinary restart must be allowed again.
            var newPeer = new UniqueAddress(remote, 2L);
            registry.CompleteHandshake(remote, newPeer);

            association.ShouldRestartOutbound().Should().BeTrue("the new incarnation's uid was never quarantined");
            association.IsQuarantined(1L).Should().BeTrue("the old uid must still be quarantinable/quarantined");
            association.IsQuarantined(2L).Should().BeFalse();
        }

        [Fact(DisplayName = "CompleteOutbound/CompleteControlOutbound permanently latch ShouldRestartOutbound/ShouldRestartControl to false, independently per stream (design.md group 9's shutdown guard)")]
        public void Shutdown_permanently_gates_restart_independently_per_stream()
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(NewRemote());

            association.IsOutboundShutDown.Should().BeFalse();
            association.IsControlShutDown.Should().BeFalse();

            association.CompleteOutbound();

            association.IsOutboundShutDown.Should().BeTrue();
            association.ShouldRestartOutbound().Should().BeFalse("the ordinary stream was shut down for good");
            association.IsControlShutDown.Should().BeFalse("shutting down the ordinary stream must not affect the control stream's independent gate");
            association.ShouldRestartControl().Should().BeTrue();

            association.CompleteControlOutbound();

            association.IsControlShutDown.Should().BeTrue();
            association.ShouldRestartControl().Should().BeFalse();
        }

        [Fact(DisplayName = "MaterializeOnceGate.Reset allows EnsureStarted to materialize again")]
        public void Gate_reset_allows_remateralization()
        {
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(NewRemote());

            association.IsOutboundMaterialized.Should().BeFalse();
            var callCount = 0;
            association.EnsureOutboundMaterialized(_ => callCount++);
            association.IsOutboundMaterialized.Should().BeTrue();

            // A second EnsureOutboundMaterialized call, without a Reset in between, must NOT
            // materialize again (the whole point of the once-only latch).
            association.EnsureOutboundMaterialized(_ => callCount++);
            callCount.Should().Be(1);

            association.ResetOutboundGate();
            association.IsOutboundMaterialized.Should().BeFalse("reset must clear the latch so the NEXT call materializes again");

            association.EnsureOutboundMaterialized(_ => callCount++);
            callCount.Should().Be(2, "after a reset, the next EnsureOutboundMaterialized call must run the callback again");
            association.IsOutboundMaterialized.Should().BeTrue();
        }

        [Fact(DisplayName = "SystemMessageDeliveryState is the SAME instance across repeated lookups for the same Association -- it is what lets a restarted materialization attach to the prior unacked buffer")]
        public void SystemMessageDeliveryState_is_stable_per_association()
        {
            var registry = new AssociationRegistry();
            var remote = NewRemote();

            var first = registry.AssociationFor(remote).SystemMessageDeliveryState;
            var second = registry.AssociationFor(remote).SystemMessageDeliveryState;

            second.Should().BeSameAs(first);
        }

        [Fact(DisplayName = "8857: failed control restart discards stale housekeeping without losing queued system messages")]
        public void Should_Discard_Housekeeping_But_Preserve_Queued_System_Messages()
        {
            var association = new AssociationRegistry().AssociationFor(NewRemote());
            var peer = new UniqueAddress(NewRemote(), 1L);
            object[] housekeeping =
            {
                new HandshakeReq(peer, NewRemote()), new HandshakeRsp(peer),
                new ArteryHeartbeat(), new ArteryHeartbeatRsp(),
                new Akka.Remote.Artery.Ack(1L, peer), new Akka.Remote.Artery.Nack(1L, peer)
            };
            foreach (var message in housekeeping)
                association.TryEnqueueControl(new OutboundEnvelope(message, null, null)).Should().BeTrue();
            var watch = new OutboundEnvelope(new Watch(ActorRefs.Nobody, ActorRefs.Nobody), null, "/user/target");
            association.TryEnqueueControl(watch).Should().BeTrue();

            association.PrepareControlRestart().Should().BeFalse("no message has been wrapped into reliable state yet");
            association.ControlQueueCount.Should().Be(1);
            association.ControlReader.TryRead(out var retained).Should().BeTrue();
            retained.Should().BeSameAs(watch);
        }

        [Fact(DisplayName = "8857: control restart must not discard a pending quarantine notification")]
        public void Should_Preserve_Quarantine_Notification_Across_Restart()
        {
            var remote = NewRemote();
            var association = new AssociationRegistry().AssociationFor(remote);
            var notification = new OutboundEnvelope(new ArteryQuarantined(new UniqueAddress(remote, 42L), 42L), null, null);
            association.TryEnqueueControl(new OutboundEnvelope(new ArteryHeartbeat(), null, null)).Should().BeTrue();
            association.TryEnqueueControl(notification).Should().BeTrue();

            association.PrepareControlRestart();

            association.ControlQueueCount.Should().Be(1);
            association.ControlReader.TryRead(out var retained).Should().BeTrue();
            retained.Should().BeSameAs(notification);
        }

        [Fact(DisplayName = "Quarantine notification expiry is bounded and never renewed by requeue")]
        public void Should_Expire_Quarantine_Notification_Without_Renewing_On_Return()
        {
            var now = TimeSpan.Zero;
            var remote = NewRemote();
            var association = new Association(remote, quarantineNotificationTimeout: TimeSpan.FromSeconds(20), monotonicClock: () => now);
            var notification = new OutboundEnvelope(new ArteryQuarantined(new UniqueAddress(remote, 99L), 42L), null, null);
            association.TryEnqueueControl(notification).Should().BeTrue();
            association.ControlReader.TryRead(out var held).Should().BeTrue();
            now = TimeSpan.FromSeconds(19);
            association.TryEnqueueControl(held!).Should().BeTrue();
            association.PrepareControlRestart().Should().BeFalse();
            association.ControlQueueCount.Should().Be(1);

            now = TimeSpan.FromSeconds(20);
            association.PrepareControlRestart().Should().BeFalse();
            association.ControlQueueCount.Should().Be(0, "the last expired notice must no longer drive retries");
            association.ShouldSendControl(notification).Should().BeFalse();

            var fresh = new OutboundEnvelope(notification.Message, null, null);
            association.TryEnqueueControl(fresh).Should().BeTrue();
            association.ShouldSendControl(fresh).Should().BeTrue("a genuinely new notice has its own deadline");
            now = TimeSpan.FromSeconds(40);
            association.PrepareControlRestart();
            association.ControlQueueCount.Should().Be(0);
        }

        [Fact(DisplayName = "Quarantine notice target UID, rather than originating UID, controls retirement")]
        public void Should_Retire_Notification_When_Target_Uid_Is_Replaced()
        {
            var remote = NewRemote();
            var registry = new AssociationRegistry();
            var association = registry.AssociationFor(remote);
            registry.CompleteHandshake(remote, new UniqueAddress(remote, 42L));
            var notification = new OutboundEnvelope(new ArteryQuarantined(new UniqueAddress(NewRemote(), 99L), 42L), null, null);
            association.TryEnqueueControl(notification).Should().BeTrue();
            association.ShouldSendControl(notification).Should().BeTrue();

            registry.CompleteHandshake(remote, new UniqueAddress(remote, 43L));
            association.ShouldSendControl(notification).Should().BeFalse("the notice concerns the previous incarnation");
            association.PrepareControlRestart();
            association.ControlQueueCount.Should().Be(0);
        }

        [Fact(DisplayName = "8857: unacknowledged delivery state survives an idle-control decision unchanged")]
        public void Should_Preserve_Unacknowledged_Delivery_State()
        {
            var association = new AssociationRegistry().AssociationFor(NewRemote());
            var state = association.SystemMessageDeliveryState;
            var envelope = new OutboundEnvelope("unacknowledged", null, "/user/target");
            var sentAt = DateTime.UtcNow;
            state.Buffer.Enqueue((7L, envelope, sentAt));
            state.NextSeq = 8;
            state.CurrentIncarnation = 2;

            association.PrepareControlRestart().Should().BeTrue("reliable work remains even with an empty channel");
            state.Buffer.Peek().Should().Be((7L, envelope, sentAt));
            state.NextSeq.Should().Be(8);
            state.CurrentIncarnation.Should().Be(2);
            association.IsControlShutDown.Should().BeFalse();
        }

        [Fact(DisplayName = "8857: failed-control cleanup applies local delivery clears but ignores stale incarnations")]
        public void Should_Apply_Current_Delivery_Clear_Without_Reviving_Stale_State()
        {
            var association = new AssociationRegistry().AssociationFor(NewRemote());
            var state = association.SystemMessageDeliveryState;
            state.Buffer.Enqueue((1L, new OutboundEnvelope("pending", null, null), DateTime.UtcNow));
            state.NextSeq = 2;
            state.CurrentIncarnation = 2;
            association.TryEnqueueControl(new OutboundEnvelope(new ClearSystemMessageDelivery(1), null, null));
            association.PrepareControlRestart().Should().BeTrue();
            state.Buffer.Count.Should().Be(1);
            state.NextSeq.Should().Be(2);

            association.TryEnqueueControl(new OutboundEnvelope(new ClearSystemMessageDelivery(2), null, null));
            association.PrepareControlRestart().Should().BeFalse();
            state.Buffer.Should().BeEmpty();
            state.NextSeq.Should().Be(1);
            state.CurrentIncarnation.Should().Be(2);
            association.ControlQueueCount.Should().Be(0);
        }

        [Fact(DisplayName = "8857: pending traffic in any ordinary lane prevents quiescence")]
        public void Should_Find_Pending_Traffic_In_All_Ordinary_Lanes()
        {
            var association = new AssociationRegistry(outboundLanes: 3).AssociationFor(NewRemote());
            association.HasQueuedOutbound.Should().BeFalse();
            association.TryEnqueueOutbound(new OutboundEnvelope("last-lane", null, null), 2).Should().BeTrue();
            association.HasQueuedOutbound.Should().BeTrue();
            association.LaneReader(2).TryRead(out _).Should().BeTrue();
            association.HasQueuedOutbound.Should().BeFalse();
        }
    }
}
