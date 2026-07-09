//-----------------------------------------------------------------------
// <copyright file="ArteryUnwatchShutdownRaceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
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
    /// Regression test for the shutdown race fixed on <c>ArteryRemoting.HandleControlOverflow</c>/
    /// <c>EnqueueControl</c>/<c>EnqueueSystemMessage</c>: <see cref="Association.TryEnqueueControl"/>'s
    /// underlying <see cref="System.Threading.Channels.ChannelWriter{T}.TryWrite"/> returns
    /// <see langword="false"/> BOTH when the bounded control queue is genuinely at capacity AND when
    /// the writer has already been completed by <c>Association.CompleteControlOutbound</c> (which
    /// graceful <c>ArteryRemoting.Shutdown()</c> calls for EVERY association up front). RemoteWatcher
    /// -- a plain actor with its own mailbox, entirely independent of the transport's own shutdown
    /// sequencing -- routinely still has queued <c>UnwatchRemote</c> work to drain (each one
    /// potentially firing a real wire-level <see cref="Akka.Dispatch.SysMsg.Unwatch"/> system message,
    /// see <c>RemoteWatcher.RemoveWatch</c>) at the exact moment its own actor-termination races
    /// <c>ArteryRemoting.Shutdown()</c>'s per-association <c>CompleteControlOutbound()</c> sweep. Before
    /// this fix, that ordinary, entirely benign race was indistinguishable from a REAL queue overflow
    /// (both are <c>TryWrite</c> returning <see langword="false"/>) -- <c>HandleControlOverflow</c>
    /// logged at ERROR ("... queue ... is full (capacity ...)") and QUARANTINED the association, purely
    /// because it lost this ordinary shutdown race against a perfectly healthy peer.
    ///
    /// <para>
    /// <b>Why N DISTINCT watchees, not one.</b> <c>RemoteWatcher.AddWatching</c> dedupes multiple LOCAL
    /// watchers of the SAME remote actor into a single underlying wire-level <c>Watch</c> (see
    /// <c>ArteryBackpressureSpec.PlainWatcher</c>'s identical remark) -- so N instances all watching
    /// the SAME target would produce just one real <c>Watch</c>/<c>Unwatch</c> pair, not the sustained
    /// drain burst this race needs. Watching N DISTINCT remote actors, each via its own dedicated local
    /// watcher, forces N genuinely separate entries in <c>RemoteWatcher.Watching</c>, each of which
    /// needs its OWN <c>Context.Unwatch</c> (-&gt; real wire <c>Unwatch</c>) once its sole local watcher
    /// stops -- exactly what a mass <c>Terminate()</c> triggers as <c>/user</c> tears every one of them
    /// down together.
    /// </para>
    ///
    /// <para>
    /// <b>Established watches, not fire-and-forget (contrast with <c>ArterySystemMessageDeliverySpec
    /// .Should_Shutdown_Cleanly_With_InFlight_System_Messages_Without_Quarantining</c>).</b> That
    /// existing test fires a burst of <c>Watch</c> and terminates immediately, deliberately WITHOUT
    /// waiting for any of it to land in <c>RemoteWatcher</c>'s own bookkeeping -- it proves the SEPARATE
    /// "in-flight, unacknowledged Watch at Shutdown" path is quarantine-free. This test instead polls
    /// <c>RemoteWatcher.Stats</c> until every watch has GENUINELY registered before triggering
    /// <c>Terminate()</c>, so the burst this test exercises is real, already-established
    /// <c>Watching</c> state that MUST each produce a genuine <c>Unwatch</c> drain -- the specific
    /// condition the diagnosis names ("RemoteWatcher drains queued Unwatch work AFTER
    /// ArteryRemoting.Shutdown() has completed ... control channel").
    /// </para>
    ///
    /// <para>
    /// <b>Why looped across several fresh system pairs.</b> This is a genuine timing race (RemoteWatcher's
    /// mailbox-drain speed vs. CoordinatedShutdown's phase progression to
    /// <c>ArteryRemoting.Shutdown()</c>) -- a single attempt is not guaranteed to land inside the
    /// narrow window. Repeating the whole create-watch-terminate cycle against several independent,
    /// freshly created ActorSystem pairs (never reusing state across iterations) raises the odds that
    /// at least one iteration lands the race, while keeping every iteration's assertion independently
    /// meaningful (a failure in ANY iteration fails the test).
    /// </para>
    /// </summary>
    public class ArteryUnwatchShutdownRaceSpec : AkkaSpec
    {
        public ArteryUnwatchShutdownRaceSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// Watches <paramref name="target"/> at construction and does nothing else -- mirrors
        /// <c>ArteryBackpressureSpec.PlainWatcher</c>/<c>ArterySystemMessageDeliverySpec.PlainWatcher</c>.
        /// Each instance MUST watch a DISTINCT remote target (see the type-level remarks on why).
        /// </summary>
        private sealed class PlainWatcher : ReceiveActor
        {
            public PlainWatcher(IActorRef target)
            {
                Context.Watch(target);
                Receive<Terminated>(_ => { });
            }
        }

        [Fact(DisplayName = "Graceful ActorSystem shutdown must not spuriously quarantine a healthy peer or log a queue-full ERROR when RemoteWatcher is still draining queued Unwatch work as ArteryRemoting.Shutdown() completes every association's control channel")]
        public async Task Should_Not_Quarantine_When_RemoteWatcher_Drains_Queued_Unwatch_Work_During_Graceful_Shutdown()
        {
            const int watcheesPerIteration = 200;
            const int iterations = 6;

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var systemA = ActorSystem.Create($"ArteryUnwatchRaceA{iteration}", ArteryConfig());
                var systemB = ActorSystem.Create($"ArteryUnwatchRaceB{iteration}", ArteryConfig());
                try
                {
                    // N distinct, REAL watch targets on B -- a target that never actually existed
                    // would immediately DeathWatchNotification instead of sitting quietly watched,
                    // which is a different code path entirely.
                    for (var i = 0; i < watcheesPerIteration; i++)
                        systemB.ActorOf(Props.Create(() => new Echo()), $"target-{i}");

                    // Warm up the association FIRST (one real request/reply round trip) -- guarantees
                    // the control stream is fully materialized and handshaked BEFORE the watch burst
                    // below, so the burst below exercises RemoteWatcher's drain racing Shutdown(), not
                    // ordinary stream-materialization timing.
                    var warmupTarget = await systemA.ActorSelection(SelectionPath(systemB, "target-0")).ResolveOne(TimeSpan.FromSeconds(10));
                    var warmupProbe = CreateTestProbe(systemA);
                    warmupTarget.Tell("warmup", warmupProbe.Ref);
                    await warmupProbe.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));

                    // N distinct local watchers, each watching a DISTINCT remote target -- resolved
                    // purely locally (no network round trip, same idiom as ArteryBackpressureSpec's
                    // synthetic-ref tests) since only the address/path need be correct for Context.Watch.
                    var targets = new IActorRef[watcheesPerIteration];
                    for (var i = 0; i < watcheesPerIteration; i++)
                        targets[i] = RARP.For(systemA).Provider.ResolveActorRef(SelectionPath(systemB, $"target-{i}"));

                    for (var i = 0; i < watcheesPerIteration; i++)
                        systemA.ActorOf(Props.Create(() => new PlainWatcher(targets[i])));

                    // Liveness: wait until RemoteWatcher's OWN bookkeeping genuinely reflects every one
                    // of these N watches (not merely fired-and-forgotten) -- see the type-level remarks
                    // on why this test polls for real registration rather than firing and immediately
                    // terminating.
                    var remoteWatcher = RARP.For(systemA).Provider.RemoteWatcher;
                    var statsProbe = CreateTestProbe(systemA);
                    await AwaitAssertAsync(async () =>
                    {
                        remoteWatcher.Tell(new RemoteWatcher.Stats(0, 0), statsProbe.Ref);
                        var stats = await statsProbe.ExpectMsgAsync<RemoteWatcher.Stats>(TimeSpan.FromSeconds(3));
                        stats.WatchingRefs.Count.Should().BeGreaterOrEqualTo(watcheesPerIteration);
                    }, TimeSpan.FromSeconds(10));

                    // Secondary, best-effort corroboration alongside the primary ERROR-log assertion
                    // below: no quarantine LIFECYCLE EVENT either. Subscribed before Terminate() --
                    // ArterySystemMessageDeliverySpec's identical sibling test already establishes that
                    // checking a TestProbe's mailbox AFTER Terminate() has fully completed is reliable
                    // (the probe's queue holds whatever it received while systemA was still alive).
                    var quarantineProbe = CreateTestProbe(systemA);
                    systemA.EventStream.Subscribe(quarantineProbe.Ref, typeof(QuarantinedEvent));
                    systemA.EventStream.Subscribe(quarantineProbe.Ref, typeof(ThisActorSystemQuarantinedEvent));

                    // THE regression assertion: zero ERROR log events whose message contains the
                    // control-queue-full wording (HandleControlOverflow's ERROR line) anywhere during
                    // graceful termination -- with the fix, a closed-channel drop during shutdown is a
                    // quiet DEBUG line instead, matching Pekko's Association.sendControl isShutdown
                    // gating.
                    await CreateEventFilter(systemA).Error(contains: "is full (capacity").ExpectAsync(0, async () =>
                    {
                        await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                    });

                    await quarantineProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
                }
                finally
                {
                    await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                    await systemB.Terminate().AwaitWithTimeout(15.Seconds());
                }
            }
        }
    }
}
