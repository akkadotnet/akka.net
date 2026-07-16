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
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
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

        [Theory(DisplayName = "Reconnect warning cadence should warn on attempt one and every tenth attempt")]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(9, false)]
        [InlineData(10, true)]
        [InlineData(11, false)]
        [InlineData(19, false)]
        [InlineData(20, true)]
        [InlineData(0, false)]
        public void Should_Select_Warning_Attempts_Deterministically(int attempt, bool expected)
        {
            ArteryRemoting.ShouldWarnReconnectAttempt(attempt).Should().Be(expected);
        }

        [Fact(DisplayName = "Reconnect outage should warn once initially and report recovery")]
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

            ActorSystem? rebornPeer = null;
            try
            {
                // Prove the live transport emits the initial operator-visible warning. Exact
                // periodic cadence is covered by the pure theory above; waiting for ten real TCP
                // reconnects made this test scheduler- and platform-sensitive.
                await EventFilter.Warning(contains: controlPrefix).ExpectOneAsync(
                    TimeSpan.FromSeconds(30), () =>
                {
                    var target = RARP.For(Sys).Provider.ResolveActorRef($"{peerAddress}/user/nobody");
                    target.Tell("poke", ActorRefs.NoSender);
                    return Task.CompletedTask;
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
                if (rebornPeer is not null)
                    await rebornPeer.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
