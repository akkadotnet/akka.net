//-----------------------------------------------------------------------
// <copyright file="ArteryReconnectLoggingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Pins the PER-OUTAGE reconnect-log cadence (<c>ArteryRemoting</c>'s outbound fault
    /// continuations + <c>ReportOutboundConnectionEstablished</c>): while a peer stays
    /// unreachable, restart attempt 1 logs at WARNING, every
    /// <c>ReconnectWarningAttemptInterval</c>-th attempt logs another WARNING (with the attempt
    /// count and outage duration, so a persistent failure stays visible), and every attempt in
    /// between logs at DEBUG -- an unreachable peer no longer produces one WARNING per
    /// <c>outbound-restart-backoff</c> per stream (which drowned test event filters and real
    /// warnings). A subsequent successful reconnect reports ONCE at INFO with the outage's
    /// failed-attempt count and resets the cadence.
    ///
    /// <para>
    /// The testkit's own <c>Sys</c> IS the reconnecting node here (the Artery config is passed to
    /// the <see cref="AkkaSpec"/> base) so <c>EventFilter</c> observes the system doing the
    /// reconnecting; the test proves each filter level is non-vacuous with a synthetic event
    /// first (an EventFilter on a system without a subscribed <c>TestEventListener</c> -- or
    /// Debug events below the configured loglevel -- would silently match nothing).
    /// </para>
    /// </summary>
    public class ArteryReconnectLoggingSpec : AkkaSpec
    {
        private const string PeerSystemName = "ArteryReconnectLogPeer";

        /// <summary>
        /// DEBUG loglevel so the per-attempt Debug cadence is observable on the event stream, and
        /// a short <c>outbound-restart-backoff</c> so 12+ reconnect attempts happen fast. No
        /// assertion anywhere touches the backoff TIMING itself -- progress is measured purely by
        /// counting per-attempt log events.
        /// </summary>
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString("""
            akka.loglevel = DEBUG
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-restart-backoff = 100ms
            """);

        private static Config PeerConfig(int port) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            """);

        public ArteryReconnectLoggingSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        /// <summary>
        /// Counts <see cref="Debug"/> log events whose FORMATTED message contains
        /// <c>marker</c> -- the count-based (never wall-clock) progress signal for "the reconnect
        /// loop has provably made N attempts". A plain EventStream subscriber, deliberately NOT an
        /// EventFilter: the filter's exact-count Expect semantics would race a cadence that keeps
        /// producing events after the expected count is reached.
        /// </summary>
        private sealed class DebugEventCounter : ReceiveActor
        {
            public DebugEventCounter(string marker, AtomicCounter counter)
            {
                Receive<Debug>(d =>
                {
                    if (d.Message?.ToString()?.Contains(marker) == true)
                        counter.IncrementAndGet();
                });
            }
        }

        /// <summary>
        /// Re-binds a fresh <see cref="ActorSystem"/> to the EXACT SAME port the just-terminated
        /// port-allocation system used, retrying if the OS has not yet released the socket --
        /// the same "bind-your-own is race-acceptable here" pattern as
        /// <c>ArteryReconnectSpec</c>/<c>ArteryOutboundLanesRestartSpec</c>: this test exclusively
        /// owns the port between the two incarnations (the only consumer in between is the system
        /// under test, whose connect attempts are EXPECTED to fail), so a bind failure here can
        /// only mean the previous listener's teardown has not finished yet.
        /// </summary>
        private static async Task<ActorSystem> CreateSystemOnPortWithRetryAsync(string name, int port, int maxAttempts = 40)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return ActorSystem.Create(name, PeerConfig(port));
                }
                catch (Exception) when (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                }
            }

            // Unreachable: the loop above either returns on success or lets the final attempt's
            // exception propagate once attempt == maxAttempts.
            throw new InvalidOperationException($"Unreachable: failed to bind port {port} after {maxAttempts} attempts.");
        }

        [Fact(DisplayName = "Should_LogReconnectsOnPerOutageCadence_When_PeerIsUnreachable: attempt 1 at WARNING, every 10th at WARNING with attempt count + outage duration, the rest at DEBUG, then ONE INFO on recovery")]
        public async Task Should_LogReconnectsOnPerOutageCadence_When_PeerIsUnreachable()
        {
            // Allocate a real port, then FREE it (self-bind-then-release; see
            // CreateSystemOnPortWithRetryAsync's remarks) -- every connect attempt below hits a
            // dead port until the peer is deliberately reborn on it.
            var firstIncarnation = ActorSystem.Create(PeerSystemName, PeerConfig(0));
            var port = BoundPort(firstIncarnation);
            await firstIncarnation.Terminate().AwaitWithTimeout(10.Seconds());

            var peerAddress = $"akka://{PeerSystemName}@127.0.0.1:{port}";

            // All assertions are scoped to the CONTROL stream's messages: the control and
            // ordinary streams each run this cadence independently (their attempt counters are
            // per-stream), so a single-stream scope keeps the expected counts exact.
            var controlPrefix = $"Artery Control outbound connection to [{peerAddress}]";

            // NON-VACUITY: prove each filter level actually intercepts on Sys before relying on it.
            await EventFilter.Warning(contains: "reconnect-log-spec-synthetic").ExpectOneAsync(
                () => { Sys.Log.Warning("reconnect-log-spec-synthetic warning"); return Task.CompletedTask; });
            await EventFilter.Debug(contains: "reconnect-log-spec-synthetic").ExpectOneAsync(
                () => { Sys.Log.Debug("reconnect-log-spec-synthetic debug"); return Task.CompletedTask; });
            await EventFilter.Info(contains: "reconnect-log-spec-synthetic").ExpectOneAsync(
                () => { Sys.Log.Info("reconnect-log-spec-synthetic info"); return Task.CompletedTask; });

            // Count-based progress signal: the control stream's per-attempt DEBUG lines.
            var debugAttempts = new AtomicCounter(0);
            var counterRef = Sys.ActorOf(Props.Create(() =>
                new DebugEventCounter($"{controlPrefix} failed (reconnect attempt", debugAttempts)));
            Sys.EventStream.Subscribe(counterRef, typeof(Debug));

            ActorSystem? rebornPeer = null;
            try
            {
                // OUTAGE: exactly TWO control-stream WARNINGs -- attempt 1 (the first fault) and
                // attempt 10 (the every-10th persistent-failure warning) -- across a window that
                // provably spans 12+ attempts: 10 per-attempt DEBUG lines = attempts 2-9 plus
                // 11-12, so by the time the window closes the cadence has passed attempt 12, and
                // a third WARNING would only ever come at attempt 20 (never inside this window).
                await EventFilter.Warning(contains: controlPrefix).ExpectAsync(2, TimeSpan.FromSeconds(90), async () =>
                {
                    // Provider-resolved ref: no wire round trip, so this send is what
                    // materializes the outbound streams and their FIRST connect attempt is
                    // guaranteed to hit the dead port (same stimulus as
                    // ArteryOutboundLanesRestartSpec's connect-race test).
                    var target = RARP.For(Sys).Provider.ResolveActorRef($"{peerAddress}/user/nobody");
                    target.Tell("poke", ActorRefs.NoSender);

                    // Explicit 50ms poll interval: the default interval scales with `max`, which
                    // would leave this window open for several extra seconds after the condition
                    // turns true -- long enough for the attempt-20 (and beyond) WARNINGs to leak
                    // into the exactly-2 count.
                    await AwaitConditionAsync(
                        () => Task.FromResult(debugAttempts.Current >= 10),
                        TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(50));
                });

                // RECOVERY: the peer comes back on the SAME port; the next restart attempt's TCP
                // connect ESTABLISHES, which must report exactly one INFO carrying the outage's
                // failed-attempt count (and reset the cadence for any future outage).
                await EventFilter.Info(contains: $"{controlPrefix} reconnected after").ExpectOneAsync(
                    TimeSpan.FromSeconds(90),
                    async () => { rebornPeer = await CreateSystemOnPortWithRetryAsync(PeerSystemName, port); });
            }
            finally
            {
                Sys.EventStream.Unsubscribe(counterRef);
                if (rebornPeer is not null)
                    await rebornPeer.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
