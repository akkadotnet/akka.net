//-----------------------------------------------------------------------
// <copyright file="ArteryShutdownSystemMessageAckRaceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Regression test for the shutdown race guarded by <c>ArteryRemoting.MaterializeOutboundStream</c>'s
    /// <c>catch (InvalidOperationException) when (IsActorSystemTerminating())</c>.
    ///
    /// <para>
    /// <b>Why this no longer races a full <see cref="ActorSystem.Terminate()"/>.</b> The transport's
    /// materializer now hosts its <c>StreamSupervisor</c> as a <c>/system</c> actor
    /// (<c>ArteryRemoting.CreateSystemMaterializer</c>, via <c>System.SystemActorOf</c>), precisely so
    /// it does NOT start terminating at <c>/user</c> teardown -- it survives until
    /// <c>ArteryRemoting.Shutdown()</c> itself runs, which sets <c>_isShutdown</c> as the FIRST
    /// statement of that method, well before the supervisor is ever touched
    /// (<c>materializer?.Shutdown()</c> runs many awaits later). So a graceful
    /// <see cref="ActorSystem.Terminate()"/> can no longer open the window this catch guards: the
    /// up-front <c>IsTransportTerminating()</c> guard (deliberately NOT gated on
    /// <c>CoordinatedShutdown.ShutdownReason</c> -- see its remarks on why materializing during a
    /// graceful cluster leave must still be allowed) refuses new materializations via
    /// <c>_isShutdown</c> well before the supervisor's own cell can ever start terminating underneath
    /// it. That is the intended, desirable effect of hosting the supervisor under <c>/system</c>, not
    /// a hole in this test.
    /// </para>
    ///
    /// <para>
    /// <b>What still exercises the catch.</b> The catch it guards is defensive against a narrower,
    /// residual race: the supervisor's OWN <c>ActorCell</c> entering <c>IsTerminating</c> (forbidding
    /// new children, i.e. new graph-interpreter actors) for a reason INDEPENDENT of any of this
    /// transport's own shutdown flags -- e.g. a supervision failure, or a future bug that stops the
    /// supervisor without going through <c>ArteryRemoting.Shutdown()</c> first. This spec drives that
    /// directly: it stops the <c>StreamSupervisor</c> itself (<c>ActorSystem.Stop</c>) and races a
    /// bounded burst of brand-new control-stream materializations against it, asserting the guard's
    /// observable contract -- zero ERROR events logged -- rather than relying on scheduling luck
    /// against a real, multi-phase <see cref="ActorSystem.Terminate()"/> that (post-fix) no longer
    /// reaches the race at all.
    /// </para>
    ///
    /// <para>
    /// <b>Why reflection, and why no live peer.</b> <c>ArteryRemoting.EnqueueControl</c> and the
    /// <c>_materializer</c> field are <see langword="private"/> production internals -- reflection is
    /// the only way to drive the SAME "materialize a brand-new control stream" code path this fix
    /// guards directly from a test, on demand, without needing a real peer to complete a handshake
    /// against. No live peer is needed because the race is in the LOCAL actor-creation step inside
    /// <c>Run()</c> -- entirely before any socket is ever touched -- so a synthetic, never-actually-
    /// reachable remote address is sufficient to exercise it (any subsequent, asynchronous connection
    /// failure against that address is irrelevant to this test and only ever logs at Warning, which
    /// this test does not assert on).
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately a COARSE smoke test.</b> This test races stopping the <c>StreamSupervisor</c>
    /// against a small, bounded, yielding burst of <c>EnqueueControl</c> calls and asserts only the
    /// guard's observable contract: zero ERROR events logged. Depending on scheduling, any given run's
    /// burst may land in the supervisor's <c>IsTerminating</c> window (exercising the
    /// <see cref="InvalidOperationException"/> swallow), in a fast-path refusal before <c>Run()</c> is
    /// ever attempted (the up-front guard already sees the supervisor terminating), or entirely before
    /// either -- that non-determinism is an ACCEPTED trade-off, chosen over a previous
    /// deterministic-window design whose scaffolding (busy-spin worker threads in one version, a
    /// PostStop-gated child actor attached to the StreamSupervisor's cell in another) outweighed the
    /// three-line guard under test. In particular the busy-spin version starved the shared ThreadPool
    /// on 2-core CI agents badly enough that the system's own termination processing fell behind the
    /// test's liveness bound -- an environment-timing flake, not a product regression. A regression
    /// here (the guard removed, so the exception surfaces uncaught as an ERROR) still fails this test
    /// on the runs where the window IS hit, which is what a smoke regression test is for.
    /// </para>
    ///
    /// <para>
    /// No wall-clock assertion is made anywhere in the test body -- only "zero ERROR events were
    /// logged across the burst" plus a bounded liveness await on the supervisor's own termination
    /// (a liveness bound, not a timing measurement).
    /// </para>
    /// </summary>
    public class ArteryShutdownSystemMessageAckRaceSpec : AkkaSpec
    {
        public ArteryShutdownSystemMessageAckRaceSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]
            """);

        [Fact(DisplayName = "Shutdown race: materializing a brand-new CONTROL stream while the transport's own /system-hosted StreamSupervisor is terminating (independent of any transport-shutdown flag) must not surface as an ERROR log (the InvalidOperationException is swallowed as an authoritative shutdown signal, per this fix's ArteryRemoting.MaterializeOutboundStream guard)")]
        public async Task Should_Not_Log_Error_When_New_Control_Stream_Materialization_Races_StreamSupervisor_Termination()
        {
            var systemB = ActorSystem.Create("ArteryShutdownRaceB", ArteryConfig());
            try
            {
                var transportB = (ArteryRemoting)RARP.For(systemB).Provider.Transport;

                // EnqueueControl(Address, object) is private production-internal plumbing -- both
                // it and EnqueueSystemMessage funnel into the SAME MaterializeControlOutbound code
                // path this fix guards, so driving EITHER exercises the race identically; this one
                // has the simpler signature to invoke via reflection.
                var enqueueControl = typeof(ArteryRemoting).GetMethod("EnqueueControl", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("ArteryRemoting.EnqueueControl not found via reflection -- check the method name/signature.");

                var materializerField = typeof(ArteryRemoting).GetField("_materializer", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("ArteryRemoting._materializer not found via reflection -- check the field name.");

                // SEED one real, already-materialized stream BEFORE stopping the supervisor: an empty
                // StreamSupervisor (no children yet) terminates its OWN ActorCell atomically --
                // ActorCell.Terminate() only transitions through the IsTerminating-true
                // intermediate state while WAITING for at least one child to also stop (see
                // ActorCell.FaultHandling.Terminate()/SetChildrenTerminationReason) -- so with zero
                // children there is no observable window to race at all. Seeding a first
                // materialization here (any address; never actually needs to connect) gives the
                // supervisor a real child to wait for, so its termination below has a genuine,
                // non-atomic IsTerminating window.
                enqueueControl.Invoke(transportB, new object[] { new Address("akka", "seed-peer", "127.0.0.1", 1), new ArteryHeartbeat() });

                var materializer = (ActorMaterializer)(materializerField.GetValue(transportB)
                    ?? throw new InvalidOperationException("ArteryRemoting._materializer was null -- the transport has not finished starting."));
                var supervisor = materializer.Supervisor;

                var probe = CreateTestProbe(systemB);
                probe.Watch(supervisor);

                await CreateEventFilter(systemB).Error().ExpectAsync(0, async () =>
                {
                    // Stop the /system-hosted StreamSupervisor DIRECTLY -- see this spec's class doc
                    // for why a full ActorSystem.Terminate() can no longer reach this race now that
                    // CreateSystemMaterializer hosts the supervisor under /system: _isShutdown is set
                    // as the FIRST statement of ArteryRemoting.Shutdown(), so the up-front
                    // IsTransportTerminating() guard refuses new materializations long before the
                    // supervisor's own cell could ever start terminating via that path. The residual
                    // race the catch below still guards -- the supervisor's cell terminating for a
                    // reason independent of any transport-shutdown flag -- is what this drives.
                    systemB.Stop(supervisor);

                    // Concurrently, a small bounded YIELDING burst of materialize-a-brand-new-
                    // control-stream attempts. Every call uses a fresh, never-before-seen port
                    // (single shared Interlocked counter, 2 * 4000 max draws starting at 1025 --
                    // always a valid port) so no attempt can fail for any reason other than the
                    // race under test. Task.Yield() every 16 iterations keeps the burst from
                    // monopolizing the ThreadPool (the previous busy-spin design's downfall on
                    // 2-core CI agents); the supervisor's own Terminated message (awaited below) is
                    // the early exit once there is nothing left to race.
                    const int workerCount = 2;
                    const int maxIterationsPerWorker = 4000;
                    var portCounter = 0;
                    var supervisorGone = 0;

                    var workers = new Task[workerCount];
                    for (var w = 0; w < workerCount; w++)
                    {
                        workers[w] = Task.Run(async () =>
                        {
                            for (var i = 0; i < maxIterationsPerWorker && Volatile.Read(ref supervisorGone) == 0; i++)
                            {
                                var freshAddress = new Address("akka", "race-peer", "127.0.0.1", 1024 + Interlocked.Increment(ref portCounter));
                                enqueueControl.Invoke(transportB, new object[] { freshAddress, new ArteryHeartbeat() });
                                if (i % 16 == 15)
                                    await Task.Yield();
                            }
                        });
                    }

                    // Liveness: the supervisor really does terminate underneath the burst (bounded
                    // so a genuine hang fails the test rather than wedging the suite).
                    await probe.ExpectTerminatedAsync(supervisor, TimeSpan.FromSeconds(30));
                    Interlocked.Exchange(ref supervisorGone, 1);

                    await Task.WhenAll(workers);
                });
            }
            finally
            {
                // No-op-safe: Terminate() is idempotent (it just returns WhenTerminated once
                // termination has been initiated), so this only does real work if the test failed
                // before the supervisor (and thus the transport) was already torn down.
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
