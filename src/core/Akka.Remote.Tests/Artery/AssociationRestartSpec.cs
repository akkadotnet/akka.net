//-----------------------------------------------------------------------
// <copyright file="AssociationRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Actor;
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
    }
}
