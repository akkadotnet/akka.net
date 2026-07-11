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
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Regression test for the shutdown race fixed on <c>ArteryRemoting.MaterializeOutboundStream</c>:
    /// <c>ActorMaterializer.Create(system)</c>'s <c>StreamSupervisor</c> is a TOP-LEVEL actor created
    /// via <c>system.ActorOf(...)</c>, i.e. it lives under <c>/user</c> -- so it starts terminating
    /// (forbidding new children, i.e. new graph-interpreter actors) as soon as <c>/user</c> guardian
    /// tears down, WELL BEFORE <c>ArteryRemoting.Shutdown()</c> runs (that is gated behind
    /// <c>/system</c>'s <c>RemotingTerminator</c> phase, later in <see cref="CoordinatedShutdown"/>).
    /// During that window BOTH <c>_isShutdown</c> and <c>_materializer.IsShutdown</c> are still
    /// <see langword="false"/>, so materializing a BRAND-NEW outbound stream in that window used to
    /// surface <see cref="InvalidOperationException"/> ("Cannot create child while terminating or
    /// terminated") uncaught -- this fix's <c>InvalidOperationException</c> catch (deliberately
    /// WITHOUT a flag guard, unlike the sibling <c>Akka.Pattern.IllegalStateException</c> catch)
    /// swallows it and logs at Debug instead.
    ///
    /// <para>
    /// <b>Why reflection, and why no live peer.</b> <c>ArteryRemoting.EnqueueControl</c> is
    /// <see langword="private"/> production internals -- reflection is the only way to drive the
    /// SAME "materialize a brand-new control stream" code path this fix guards directly from a
    /// test, on demand, without needing a real peer to complete a handshake against. No live peer
    /// is needed because the race is in the LOCAL actor-creation step inside <c>Run()</c> --
    /// entirely before any socket is ever touched -- so a synthetic, never-actually-reachable
    /// remote address is sufficient to exercise it (any subsequent, asynchronous connection
    /// failure against that address is irrelevant to this test and only ever logs at Warning,
    /// which this test does not assert on).
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately a COARSE smoke test.</b> This test races a REAL, full
    /// <c>ActorSystem.Terminate()</c> against a small, bounded, yielding burst of
    /// <c>EnqueueControl</c> calls and asserts only the guard's observable contract: zero ERROR
    /// events logged across the whole shutdown. Depending on scheduling, any given run's burst may
    /// land in the <c>IsTerminating</c> window (exercising the <see cref="InvalidOperationException"/>
    /// swallow), in the <c>_isShutdown</c> fast-path (quiet drop to dead letters), or entirely
    /// before either -- that non-determinism is an ACCEPTED trade-off, chosen over a previous
    /// deterministic-window design whose scaffolding (busy-spin worker threads in one version, a
    /// PostStop-gated child actor attached to the StreamSupervisor's cell in another) outweighed
    /// the three-line guard under test. In particular the busy-spin version starved the shared
    /// ThreadPool on 2-core CI agents badly enough that the system's own termination processing
    /// fell behind the test's liveness bound -- an environment-timing flake, not a product
    /// regression. A regression here (the guard removed, so the exception surfaces uncaught as an
    /// ERROR) still fails this test on the runs where the window IS hit, which is what a smoke
    /// regression test is for.
    /// </para>
    ///
    /// <para>
    /// No wall-clock assertion is made anywhere in the test body -- only "zero ERROR events were
    /// logged across the whole shutdown" plus bounded liveness awaits on worker completion and
    /// system termination (liveness bounds, not timing measurements).
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

        [Fact(DisplayName = "Shutdown race: materializing a brand-new CONTROL stream while the transport's own StreamSupervisor is terminating must not surface as an ERROR log (the InvalidOperationException is swallowed as an authoritative shutdown signal, per this fix's ArteryRemoting.MaterializeOutboundStream guard)")]
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

                // SEED one real, already-materialized stream BEFORE terminating anything: an empty
                // StreamSupervisor (no children yet) terminates its OWN ActorCell atomically --
                // ActorCell.Terminate() only transitions through the IsTerminating-true
                // intermediate state while WAITING for at least one child to also stop (see
                // ActorCell.FaultHandling.Terminate()/SetChildrenTerminationReason) -- so with zero
                // children there is no observable window to race at all. Seeding a first
                // materialization here (any address; never actually needs to connect) gives the
                // supervisor a real child to wait for, so its termination during the system
                // shutdown below has a genuine, non-atomic IsTerminating window.
                enqueueControl.Invoke(transportB, new object[] { new Address("akka", "seed-peer", "127.0.0.1", 1), new ArteryHeartbeat() });

                await CreateEventFilter(systemB).Error().ExpectAsync(0, async () =>
                {
                    // A real, full ActorSystem shutdown -- /user guardian teardown (which opens the
                    // StreamSupervisor's IsTerminating window this guard exists for) followed by
                    // the RemotingTerminator phase (which flips _isShutdown, the fast-path).
                    var termination = systemB.Terminate();

                    // Concurrently, a small bounded YIELDING burst of materialize-a-brand-new-
                    // control-stream attempts. Every call uses a fresh, never-before-seen port
                    // (single shared Interlocked counter, 2 * 2000 max draws starting at 1025 --
                    // always a valid port) so no attempt can fail for any reason other than the
                    // shutdown race under test. Task.Yield() every 16 iterations keeps the burst
                    // from monopolizing the ThreadPool (the previous busy-spin design's downfall on
                    // 2-core CI agents); termination.IsCompleted is the early exit once there is
                    // nothing left to race.
                    const int workerCount = 2;
                    const int maxIterationsPerWorker = 2000;
                    var portCounter = 0;

                    var workers = new Task[workerCount];
                    for (var w = 0; w < workerCount; w++)
                    {
                        workers[w] = Task.Run(async () =>
                        {
                            for (var i = 0; i < maxIterationsPerWorker && !termination.IsCompleted; i++)
                            {
                                var freshAddress = new Address("akka", "race-peer", "127.0.0.1", 1024 + Interlocked.Increment(ref portCounter));
                                enqueueControl.Invoke(transportB, new object[] { freshAddress, new ArteryHeartbeat() });
                                if (i % 16 == 15)
                                    await Task.Yield();
                            }
                        });
                    }

                    await Task.WhenAll(workers);

                    // Liveness: the whole system really does terminate underneath the burst
                    // (bounded so a genuine hang fails the test rather than wedging the suite).
                    await termination.WaitAsync(TimeSpan.FromSeconds(30));
                });
            }
            finally
            {
                // No-op-safe: the test body already terminated systemB; Terminate() is idempotent
                // (it just returns WhenTerminated once termination has been initiated), so this
                // only does real work if the test failed before reaching its own Terminate call.
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
