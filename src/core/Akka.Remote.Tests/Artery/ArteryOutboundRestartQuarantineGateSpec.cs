//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundRestartQuarantineGateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// MUST-FIX regression guard: <c>ArteryRemoting.ScheduleOutboundRestart</c>'s post-backoff
    /// callbacks for the ORDINARY and LARGE streams must release the materialize-once gate when
    /// the scheduled restart is refused because the peer's CURRENT uid was quarantined DURING the
    /// backoff window -- quarantine is not permanent, unlike a transport shutdown.
    ///
    /// <para>
    /// Before the fix, only the PRE-SCHEDULE refusal check (the one <c>ScheduleOutboundRestart</c>
    /// runs before ever scheduling the backoff timer) released the gate on a quarantine refusal.
    /// The POST-BACKOFF callback -- the one that actually runs after <c>outbound-restart-backoff</c>
    /// elapses -- re-checked <c>ShouldRestartOutbound</c>/<c>ShouldRestartLargeOutbound</c> but, on
    /// a refusal, just returned with the gate still latched "started". Nothing else was ever going
    /// to reset it: <c>TripOutboundKillSwitch</c> on an already-dead stream produces no second
    /// completion, so no further <c>ScheduleOutboundRestart</c> call was coming. Every later send to
    /// that peer -- including the two sends the pre-schedule check's own remarks say must still get
    /// through a quarantine (an <c>ActorSelectionMessage</c>, and any send to a NEW incarnation of
    /// the peer once its own handshake ends the quarantine) -- would then enqueue forever into a
    /// channel nothing drains.
    /// </para>
    ///
    /// <para>
    /// Reaches <c>ArteryRemoting.ScheduleOutboundRestart</c> via reflection on a live (but otherwise
    /// idle) instance, the same technique <see cref="ArteryOutboundRestartBackoffSpec"/> uses --
    /// this spec's own <c>outbound-restart-backoff</c> is configured deliberately short (unlike that
    /// spec's 10s) so the scheduled callback can actually be AWAITED running, rather than merely
    /// asserted not to have run yet.
    /// </para>
    /// </summary>
    public class ArteryOutboundRestartQuarantineGateSpec : AkkaSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-restart-backoff = 300ms
            """);

        public ArteryOutboundRestartQuarantineGateSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        private static Address DeadPeerAddress() => new("akka", "peer-does-not-exist", "127.0.0.1", 1);

        private static MethodInfo ScheduleOutboundRestartMethod { get; } =
            typeof(ArteryRemoting).GetMethod("ScheduleOutboundRestart", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ArteryRemoting.ScheduleOutboundRestart not found -- has it been renamed?");

        [Fact(DisplayName = "MUST FIX: a quarantine landing during the backoff window must not permanently wedge the ORDINARY stream's materialize-once gate")]
        public async Task Quarantine_during_backoff_window_must_not_wedge_the_ordinary_gate()
        {
            var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
            var remoteAddress = DeadPeerAddress();
            var association = transport.Registry.AssociationFor(remoteAddress);

            // ShouldRestartOutbound() only consults quarantine status for the association's
            // CURRENT peer uid (AssociationState.UniqueRemoteAddress) -- a known peer uid is
            // required for a quarantine to have anything to attach to.
            var peer = new UniqueAddress(remoteAddress, 42L);
            association.CompleteHandshake(peer);

            // Simulate the stream having been up once already (a real restart), with a no-op
            // materialize callback so no real socket work happens.
            association.EnsureOutboundMaterialized(_ => { });
            association.IsOutboundMaterialized.Should().BeTrue();

            // Simulate that materialization's stream just terminating. The peer is NOT quarantined
            // yet at this point, so the pre-schedule refusal check passes straight through and
            // schedules the post-backoff callback without touching the gate.
            ScheduleOutboundRestartMethod.Invoke(transport, new object[] { remoteAddress, association, ArteryStreamId.Ordinary });
            association.IsOutboundMaterialized.Should().BeTrue("nothing has run yet to release or reset the gate");

            // The quarantine lands SOMEWHERE INSIDE the backoff window -- exactly the case the
            // pre-schedule check (which already ran, above) cannot see, because a peer's
            // reliability give-up (or an inbound ArteryQuarantined, or an explicit Quarantine call)
            // can happen at any point while the backoff timer is still running.
            association.Quarantine(peer.Uid);
            association.IsQuarantined(peer.Uid).Should().BeTrue();

            // Let the (short, 300ms-configured) backoff actually elapse and its scheduled callback
            // run for real. On the buggy code the callback observes ShouldRestartOutbound() ==
            // false (quarantined, not shut down) and returns with the gate still latched --
            // IsOutboundMaterialized would stay true forever and this wait would time out.
            await AwaitConditionAsync(
                () => Task.FromResult(!association.IsOutboundMaterialized),
                TimeSpan.FromSeconds(5),
                "the post-backoff callback must release the materialize-once gate when the restart " +
                "it refused was only a (non-permanent) quarantine, not a shutdown");

            // With the gate open, a later on-demand materialize call -- exactly what EnqueueOutbound
            // does for a real send, including the ActorSelectionMessage and new-incarnation sends
            // that must still pierce a quarantine -- must actually run, proving the stream can
            // materialize again rather than silently enqueueing behind a gate nothing will ever
            // open again.
            var materializedAgain = false;
            association.EnsureOutboundMaterialized(_ => materializedAgain = true);
            materializedAgain.Should().BeTrue(
                "a real send landing after the quarantine-refused restart must still be able to " +
                "materialize the ordinary stream, not enqueue forever behind a permanently latched gate");
        }

        [Fact(DisplayName = "MUST FIX: a quarantine landing during the backoff window must not permanently wedge the LARGE-MESSAGE stream's materialize-once gate")]
        public async Task Quarantine_during_backoff_window_must_not_wedge_the_large_gate()
        {
            var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
            var remoteAddress = DeadPeerAddress();
            var association = transport.Registry.AssociationFor(remoteAddress);

            var peer = new UniqueAddress(remoteAddress, 43L);
            association.CompleteHandshake(peer);

            association.EnsureLargeOutboundMaterialized(_ => { });
            association.IsLargeOutboundMaterialized.Should().BeTrue();

            ScheduleOutboundRestartMethod.Invoke(transport, new object[] { remoteAddress, association, ArteryStreamId.Large });
            association.IsLargeOutboundMaterialized.Should().BeTrue("nothing has run yet to release or reset the gate");

            association.Quarantine(peer.Uid);
            association.IsQuarantined(peer.Uid).Should().BeTrue();

            await AwaitConditionAsync(
                () => Task.FromResult(!association.IsLargeOutboundMaterialized),
                TimeSpan.FromSeconds(5),
                "the post-backoff callback must release the LARGE stream's materialize-once gate " +
                "when the restart it refused was only a (non-permanent) quarantine, not a shutdown");

            var materializedAgain = false;
            association.EnsureLargeOutboundMaterialized(_ => materializedAgain = true);
            materializedAgain.Should().BeTrue(
                "a real large-message send landing after the quarantine-refused restart must still " +
                "be able to materialize the stream, not enqueue forever behind a permanently latched gate");
        }
    }
}
