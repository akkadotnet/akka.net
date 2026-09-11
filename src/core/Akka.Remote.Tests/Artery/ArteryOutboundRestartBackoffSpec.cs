//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundRestartBackoffSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// P1 regression guard: <c>ArteryRemoting.ScheduleOutboundRestart</c> must not open a stream's
    /// materialize-once gate until <c>outbound-restart-backoff</c> has actually elapsed. Before the
    /// fix, the CONTROL/LARGE/ORDINARY branches each reset their gate immediately (synchronously,
    /// before the backoff's <c>ScheduleOnce</c> callback even ran) and only scheduled the
    /// re-MATERIALIZE call for later -- but <c>EnqueueControl</c>/<c>EnqueueOutbound</c>/
    /// <c>EnqueueLarge</c> all materialize on demand through that SAME gate
    /// (<c>IsControlOutboundMaterialized</c>/etc.), so any producer call landing during the backoff
    /// window re-materialized immediately, bypassing the configured backoff entirely. Measured
    /// effect on the control stream in the field: 5.15 reconnects/second against a configured
    /// 1/second (see the <c>DistributedPubSubRestartSpec</c> concurrency analysis this fixes).
    ///
    /// <para>
    /// This is a pure state-machine/timing test: no real peer, no real socket needs to succeed or
    /// fail. <see cref="ArteryRemoting.ScheduleOutboundRestart"/> and the per-stream Enqueue methods
    /// are private, so this spec reaches them via reflection on a live (but otherwise idle)
    /// <see cref="ArteryRemoting"/> instance -- exactly the seam <c>AssociationRestartSpec</c> tests
    /// at the pure <see cref="Association"/> level, one layer up.
    /// </para>
    /// </summary>
    public class ArteryOutboundRestartBackoffSpec : AkkaSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-restart-backoff = 10s
            """);

        public ArteryOutboundRestartBackoffSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        private static Address DeadPeerAddress() => new("akka", "peer-does-not-exist", "127.0.0.1", 1);

        private static MethodInfo ScheduleOutboundRestartMethod { get; } =
            typeof(ArteryRemoting).GetMethod("ScheduleOutboundRestart", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ArteryRemoting.ScheduleOutboundRestart not found -- has it been renamed?");

        private static MethodInfo EnqueueControlMethod { get; } =
            typeof(ArteryRemoting).GetMethod("EnqueueControl", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ArteryRemoting.EnqueueControl not found -- has it been renamed?");

        [Fact(DisplayName = "P1: ScheduleOutboundRestart must not reset the CONTROL stream's materialize-once gate until outbound-restart-backoff elapses")]
        public void ScheduleOutboundRestart_should_not_reset_control_gate_before_backoff_elapses() =>
            AssertGateStaysOpenAcrossScheduleOutboundRestart(ArteryStreamId.Control);

        [Fact(DisplayName = "P1: ScheduleOutboundRestart must not reset the ORDINARY stream's materialize-once gate until outbound-restart-backoff elapses")]
        public void ScheduleOutboundRestart_should_not_reset_ordinary_gate_before_backoff_elapses() =>
            AssertGateStaysOpenAcrossScheduleOutboundRestart(ArteryStreamId.Ordinary);

        [Fact(DisplayName = "P1: ScheduleOutboundRestart must not reset the LARGE stream's materialize-once gate until outbound-restart-backoff elapses")]
        public void ScheduleOutboundRestart_should_not_reset_large_gate_before_backoff_elapses() =>
            AssertGateStaysOpenAcrossScheduleOutboundRestart(ArteryStreamId.Large);

        private void AssertGateStaysOpenAcrossScheduleOutboundRestart(ArteryStreamId streamId)
        {
            var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
            var remoteAddress = DeadPeerAddress();
            var association = transport.Registry.AssociationFor(remoteAddress);

            // Simulate the stream having been up once already (a real restart -- not this
            // association's first-ever materialization) with a no-op materialize callback, so no
            // real socket work happens.
            switch (streamId)
            {
                case ArteryStreamId.Control:
                    association.EnsureControlOutboundMaterialized(_ => { });
                    association.IsControlOutboundMaterialized.Should().BeTrue();
                    break;
                case ArteryStreamId.Large:
                    association.EnsureLargeOutboundMaterialized(_ => { });
                    association.IsLargeOutboundMaterialized.Should().BeTrue();
                    break;
                default:
                    association.EnsureOutboundMaterialized(_ => { });
                    association.IsOutboundMaterialized.Should().BeTrue();
                    break;
            }

            // Simulate that materialization's stream just terminating -- exactly what the write-side
            // WatchTermination continuation calls when an outbound stream dies.
            ScheduleOutboundRestartMethod.Invoke(transport, new object[] { remoteAddress, association, streamId });

            // The configured backoff is 10s and this assertion runs immediately (no time has
            // passed) -- the gate must still be OPEN (materialized) at this point. Before the fix,
            // the CONTROL/LARGE/ORDINARY branches reset the gate synchronously, right here, a full
            // backoff early.
            var stillMaterialized = streamId switch
            {
                ArteryStreamId.Control => association.IsControlOutboundMaterialized,
                ArteryStreamId.Large => association.IsLargeOutboundMaterialized,
                _ => association.IsOutboundMaterialized
            };
            stillMaterialized.Should().BeTrue(
                $"the {streamId} stream's materialize-once gate must stay open until outbound-restart-backoff has actually elapsed");
        }

        [Fact(DisplayName = "P1: an EnqueueControl call during the backoff window must not trigger a second materialize callback before outbound-restart-backoff elapses")]
        public void EnqueueControl_during_backoff_window_should_not_bypass_backoff()
        {
            var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
            var remoteAddress = DeadPeerAddress();
            var association = transport.Registry.AssociationFor(remoteAddress);

            // Count materialize callback invocations rather than asserting on
            // IsControlOutboundMaterialized alone: on the buggy code (gate reset
            // synchronously inside ScheduleOutboundRestart, before the backoff elapses) the
            // EnqueueControl call below observes the gate already closed and re-materializes
            // through it immediately, a full backoff early -- and MaterializeOnceGate.EnsureStarted
            // still leaves IsControlOutboundMaterialized == true afterwards, same as the fixed
            // code, just for the opposite reason (a second materialization instead of none). A
            // bare "is it still materialized" assertion is therefore true in both worlds and
            // cannot tell them apart. The callback count does not discriminate either, for a
            // related reason -- see the HasControlEverRestarted assertion below, which does.
            var materializeCount = 0;
            association.EnsureControlOutboundMaterialized(_ => materializeCount++);
            association.IsControlOutboundMaterialized.Should().BeTrue();
            materializeCount.Should().Be(1);

            ScheduleOutboundRestartMethod.Invoke(transport, new object[] { remoteAddress, association, ArteryStreamId.Control });

            // The discriminating assertion. MaterializeOnceGate.Reset() latches HasEverRestarted
            // synchronously (AssociationRegistry.cs), so this is the tell for exactly the bug this
            // spec guards against: if the CONTROL branch of ScheduleOutboundRestart resets the gate
            // BEFORE scheduling the backoff callback (the original bug) rather than inside it AFTER
            // outbound-restart-backoff elapses, HasControlEverRestarted is already true here -- no
            // time has passed since the Invoke above returned. materializeCount cannot show this:
            // MaterializeOnceGate.EnsureStarted runs the WINNING caller's callback, and once the
            // gate has been reset the winner of the very next materialize race is EnqueueControl's
            // own on-demand lambda below, not this test's -- so materializeCount stays 1 in both
            // the buggy and the fixed world, for opposite reasons, and cannot tell them apart.
            association.HasControlEverRestarted.Should().BeFalse(
                "the control stream's restart bookkeeping must not be latched until outbound-restart-backoff has actually elapsed");

            // Exactly what a housekeeping control message (HandshakeReq/ArteryHeartbeat/
            // DaemonMsgCreate) does on its way out -- EnqueueControl materializes on demand
            // whenever it observes the gate closed.
            EnqueueControlMethod.Invoke(transport, new object[] { remoteAddress, new ArteryHeartbeat() });

            materializeCount.Should().Be(1,
                "an outbound enqueue landing inside the backoff window must not trigger a second " +
                "materialize callback -- the gate must still be open from the materialization " +
                "already running, so EnqueueControl's on-demand path must be a no-op here");

            association.IsControlOutboundMaterialized.Should().BeTrue(
                "the control stream must not be torn down and re-materialized by an outbound enqueue landing inside the backoff window");
        }
    }
}
