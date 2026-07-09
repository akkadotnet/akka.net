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
    /// <b>Deviation from the original plan (documented per the task's own escape hatch).</b> Racing
    /// a full <c>systemB.Terminate()</c> (whole-ActorSystem <see cref="CoordinatedShutdown"/>) with a
    /// parallel <c>EnqueueControl</c> hammer was tried first and did NOT reliably land inside the
    /// race window in this environment -- an empty, single-association test system tears its whole
    /// <c>/user</c> guardian down too fast for external, out-of-process-clock-driven callers to
    /// reliably win that race. Instead, this test drives the SAME underlying condition directly and
    /// deterministically: it reaches (via reflection into the private <c>ArteryRemoting._materializer</c>
    /// field) the transport's own <see cref="ActorMaterializer.Supervisor"/> -- a PUBLIC property --
    /// and stops THAT ONE ACTOR directly (<c>ActorSystem.Stop</c>), independent of the whole system's
    /// (much slower, many-phase) termination sequence. That is the EXACT actor whose
    /// <c>ChildrenContainer.IsTerminating</c> flag is what throws <see cref="InvalidOperationException"/>
    /// from <c>Run()</c> -- racing ONLY its termination (not an entire ActorSystem's) gives a far
    /// tighter, more reliably-hit window while still exercising the real production code path
    /// end-to-end (no mocking of <c>ArteryRemoting</c> or the exception itself).
    /// </para>
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
    /// <b>Validated against a REAL regression.</b> Temporarily neutering the
    /// <see cref="InvalidOperationException"/> catch in <c>MaterializeOutboundStream</c> (while
    /// developing this test) made this test fail reliably and deterministically with the exact
    /// expected uncaught <see cref="InvalidOperationException"/>; restoring the fix made it pass
    /// reliably again. No wall-clock assertion is made anywhere in the test body -- only "zero
    /// ERROR events were logged across the whole race window" and "every worker returned" (liveness
    /// awaits, not timing measurements).
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

                // Reach the transport's OWN ActorMaterializer (private field) purely to read its
                // PUBLIC Supervisor property -- no reflection needed past this field access.
                var materializerField = typeof(ArteryRemoting).GetField("_materializer", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("ArteryRemoting._materializer not found via reflection -- check the field name.");
                var materializer = (ActorMaterializer)materializerField.GetValue(transportB)!;
                var supervisorRef = materializer.Supervisor;

                // SEED one real, already-materialized stream BEFORE racing anything: an empty
                // StreamSupervisor (no children yet) terminates its OWN ActorCell atomically --
                // ActorCell.Terminate() only transitions through the IsTerminating-true
                // intermediate state while WAITING for at least one child to also stop (see
                // ActorCell.FaultHandling.Terminate()/SetChildrenTerminationReason) -- so with zero
                // children there is no observable window to race at all. Seeding a first
                // materialization here (any address; never actually needs to connect) gives the
                // supervisor a real child to wait for, so its termination (below) has a genuine,
                // non-instantaneous IsTerminating window for the hammer loop to land in.
                enqueueControl.Invoke(transportB, new object[] { new Address("akka", "seed-peer", "127.0.0.1", 1), new ArteryHeartbeat() });

                var supervisorProbe = CreateTestProbe(systemB);
                await supervisorProbe.WatchAsync(supervisorRef);

                await CreateEventFilter(systemB).Error().ExpectAsync(0, async () =>
                {
                    // Stop JUST the materializer's StreamSupervisor -- the SAME actor whose
                    // ChildrenContainer.IsTerminating is what actually throws InvalidOperationException
                    // from Run(), independent of the whole ActorSystem's own CoordinatedShutdown
                    // sequence (see the type-level "Deviation from the original plan" remarks).
                    systemB.Stop(supervisorRef);

                    // Several PARALLEL, dedicated busy-spin threads (not a single async loop that
                    // yields between iterations -- a Task.Yield()'d loop cedes the thread back to
                    // the pool between calls and can easily step OVER the narrow race window
                    // entirely) each hammering EnqueueControl -- maximizes the total number of
                    // materialize-a-brand-new-stream attempts landing within the (now much
                    // tighter) window between the Stop() above and the supervisor's PostStop
                    // actually completing. Every call uses a FRESH port drawn from a single shared,
                    // Interlocked-incremented counter (never reused across workers) so every
                    // attempt is guaranteed to be a genuinely new, never-before-seen Address --
                    // modulo'd into TCP's valid port range [1024, 65535] so the port number itself
                    // can never be the thing that throws. Iteration caps are safety valves only
                    // (not a timing measurement) -- the real exit condition every worker checks is
                    // the shared supervisorGone flag, flipped once the watch below actually
                    // observes Terminated.
                    const int workerCount = 8;
                    const int maxIterationsPerWorker = 100_000;
                    const int minPort = 1024;
                    const int maxPort = 65535;
                    var portCounter = 0;
                    var supervisorGone = 0;

                    var workers = new Task[workerCount];
                    for (var w = 0; w < workerCount; w++)
                    {
                        workers[w] = Task.Run(() =>
                        {
                            for (var i = 0; i < maxIterationsPerWorker && Volatile.Read(ref supervisorGone) == 0; i++)
                            {
                                var port = minPort + Interlocked.Increment(ref portCounter) % (maxPort - minPort);
                                var freshAddress = new Address("akka", "race-peer", "127.0.0.1", port);
                                enqueueControl.Invoke(transportB, new object[] { freshAddress, new ArteryHeartbeat() });
                            }
                        });
                    }

                    // Liveness: the supervisor really does terminate (proves Stop() above worked
                    // and this is a genuine race, not a vacuous one against nothing).
                    await supervisorProbe.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(10));
                    Volatile.Write(ref supervisorGone, 1);

                    await Task.WhenAll(workers).AwaitWithTimeout(30.Seconds());
                });
            }
            finally
            {
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
