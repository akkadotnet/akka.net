//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundMaterializeDuringCoordinatedShutdownSpec.cs" company="Akka.NET Project">
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
using Akka.Streams;
using Akka.Streams.Implementation;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Regression test for the SCOPE of <c>ArteryRemoting</c>'s up-front outbound-materialize guards
    /// (<c>MaterializeOutboundStream</c>/<c>MaterializeOrdinaryOutboundWithLanes</c>).
    ///
    /// <para>
    /// <c>CoordinatedShutdown.ShutdownReason</c> is set as the FIRST statement of
    /// <c>CoordinatedShutdown.Run</c> -- before phase one -- and the whole graceful cluster-leave
    /// sequence (<c>cluster-sharding-shutdown-region</c>, <c>cluster-leave</c>, <c>cluster-exiting</c>)
    /// runs behind it, with <c>/user</c> still alive and this transport not remotely close to tearing
    /// down (<c>_isShutdown</c> is only set at the very start of <c>ArteryRemoting.Shutdown()</c>,
    /// which is gated behind <c>/system</c>'s <c>RemotingTerminator</c> phase, much later in
    /// <c>CoordinatedShutdown</c>). A materialize guard that refuses merely because
    /// <c>ShutdownReason != null</c> would refuse every fresh association, every reconnect after
    /// backoff, and every handshake reply for that WHOLE window -- making a graceful cluster leave
    /// undeliverable. This spec proves the guard does not do that: it holds <c>CoordinatedShutdown</c>
    /// open at an early phase and materializes a brand-new outbound control stream while it is held,
    /// asserting the stream is REALLY materialized (a new graph-interpreter child actually appears
    /// under the <c>StreamSupervisor</c>), not merely a latched, driverless gate.
    /// </para>
    ///
    /// <para>
    /// <b>Why "really materialized" needs its own check.</b>
    /// <c>MaterializeOnceGate.EnsureStarted</c> flips its <c>_started</c> flag to <see langword="true"/>
    /// BEFORE invoking the materialize callback, and only resets it if the callback throws. A guard
    /// that refuses by simply returning (rather than throwing, or resetting the gate itself) leaves
    /// <c>IsControlOutboundMaterialized</c> reading <see langword="true"/> with NO stream behind it
    /// and NO restart ever scheduled -- a permanent wedge indistinguishable, from that property alone,
    /// from a real materialization. Counting the <c>StreamSupervisor</c>'s children via
    /// <see cref="StreamSupervisor.GetChildren"/> before and after is what actually discriminates the
    /// two: only a real <c>Run()</c> creates a new graph-interpreter actor.
    /// </para>
    ///
    /// <para>
    /// This fails on a HEAD where the up-front guard also refuses on
    /// <c>CoordinatedShutdown.ShutdownReason != null</c> (no new child ever appears, the gate latches
    /// anyway) and passes once the guard is narrowed to the transport's own termination state.
    /// </para>
    /// </summary>
    public class ArteryOutboundMaterializeDuringCoordinatedShutdownSpec : AkkaSpec
    {
        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]
            """);

        public ArteryOutboundMaterializeDuringCoordinatedShutdownSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "A brand-new outbound control stream must still materialize for real while CoordinatedShutdown is running but the transport itself has not started shutting down")]
        public async Task Should_Materialize_A_Fresh_Outbound_Stream_While_CoordinatedShutdown_Is_Running_But_The_Transport_Is_Not_Shutting_Down()
        {
            var systemB = ActorSystem.Create("ArteryGuardScopeB", ArteryConfig());
            try
            {
                var transportB = (ArteryRemoting)RARP.For(systemB).Provider.Transport;

                // EnqueueControl and the materializer field are private production internals --
                // reflection drives the exact same code path a real send would (EnqueueControl ->
                // EnsureControlOutboundMaterialized -> MaterializeControlOutbound ->
                // MaterializeOutboundStream), the same seam ArteryShutdownSystemMessageAckRaceSpec
                // and ArteryOutboundRestartBackoffSpec use.
                var enqueueControl = typeof(ArteryRemoting).GetMethod("EnqueueControl", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("ArteryRemoting.EnqueueControl not found via reflection -- check the method name/signature.");
                var materializerField = typeof(ArteryRemoting).GetField("_materializer", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("ArteryRemoting._materializer not found via reflection -- check the field name.");

                var materializer = (ActorMaterializer)(materializerField.GetValue(transportB)
                    ?? throw new InvalidOperationException("ArteryRemoting._materializer was null -- the transport has not finished starting."));
                var supervisor = materializer.Supervisor;

                // Hold CoordinatedShutdown open at an EARLY phase. before-cluster-shutdown runs long
                // before actor-system-terminate -- the phase that eventually drives /user teardown
                // and, later still (gated behind /system's RemotingTerminator phase),
                // ArteryRemoting.Shutdown() itself. While this task is pending: ShutdownReason is
                // already set (Run() sets it as its first statement, before phase one runs) but the
                // transport is fully alive -- _isShutdown is false, the materializer is not shut
                // down, and the StreamSupervisor has not been touched.
                var releasePhase = new TaskCompletionSource<Done>();
                CoordinatedShutdown.Get(systemB).AddTask(CoordinatedShutdown.PhaseBeforeClusterShutdown, "hold-for-test",
                    () => releasePhase.Task);

                var terminate = systemB.Terminate();
                try
                {
                    Func<bool> shutdownReasonSet = () => CoordinatedShutdown.Get(systemB).ShutdownReason != null;
                    await AwaitConditionAsync(shutdownReasonSet, 5.Seconds(), "CoordinatedShutdown.Run() should set ShutdownReason as its first statement");

                    var freshAddress = new Address("akka", "leave-peer", "127.0.0.1", 1);
                    var association = transportB.Registry.AssociationFor(freshAddress);
                    association.IsControlOutboundMaterialized.Should().BeFalse(
                        "the association is brand new and nothing has tried to send to it yet");

                    var childrenBefore = await supervisor.Ask<StreamSupervisor.Children>(StreamSupervisor.GetChildren.Instance, TimeSpan.FromSeconds(5));

                    // Drive the SAME guarded code path a real send during a cluster leave would: a
                    // brand-new association's first-ever control-stream materialization, while
                    // CoordinatedShutdown is running but the transport itself is not shutting down.
                    enqueueControl.Invoke(transportB, new object[] { freshAddress, new ArteryHeartbeat() });

                    // The discriminating assertion: a REAL materialization creates a new
                    // graph-interpreter child under the StreamSupervisor. A refused materialization
                    // (the bug) only latches the gate -- IsControlOutboundMaterialized flips true
                    // either way, since EnsureStarted sets it BEFORE invoking the callback -- with NO
                    // new child and NO stream behind it: the exact wedge this fix must avoid.
                    await AwaitAssertAsync(async () =>
                    {
                        var childrenAfter = await supervisor.Ask<StreamSupervisor.Children>(StreamSupervisor.GetChildren.Instance, TimeSpan.FromSeconds(5));
                        childrenAfter.Refs.Count.Should().BeGreaterThan(childrenBefore.Refs.Count,
                            "materializing a fresh outbound stream during CoordinatedShutdown (but before the transport itself shuts down) must actually create the stream, not just latch the gate");
                    }, 5.Seconds());

                    association.IsControlOutboundMaterialized.Should().BeTrue();
                }
                finally
                {
                    // Release the held phase either way so the system can actually finish
                    // terminating instead of wedging the test run.
                    releasePhase.TrySetResult(Done.Instance);
                }

                await terminate.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
