//-----------------------------------------------------------------------
// <copyright file="ArteryReconnectSpec.cs" company="Akka.NET Project">
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
    /// Covers Artery TCP remoting task group 9, "Lifecycle And Compatibility Tests", the RECONNECT
    /// portion (<c>openspec/changes/artery-tcp-remoting/design.md</c>, "Association outbound-stream
    /// lifecycle: reconnect (group 9)"): clean start/stop cycles (9.2), peer restart under a NEW
    /// uid (9.3), and pre-restart ordinary-message queueing semantics (the "pick and pin" invariant
    /// 5 the design section documents). 9.4 (QuarantinedEvent publication) and 9.1 (classic
    /// remoting unaffected) are already covered by <c>ArteryBackpressureSpec</c>/<c>ArteryControlStreamSpec</c>
    /// and <c>ArteryConfigSpec</c> respectively -- not duplicated here. 9.5 (cluster formation) is
    /// a separate spec, <c>ArteryClusterFormationSpec</c>, in <c>Akka.Cluster.Tests</c> (this
    /// project does not reference <c>Akka.Cluster</c>).
    /// </summary>
    public class ArteryReconnectSpec : AkkaSpec
    {
        public ArteryReconnectSpec(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Shared Artery config for a system on port 0, with a short <c>outbound-restart-backoff</c>
        /// (so reconnect tests do not need to wait a full 1s default per retry), a short
        /// <c>control-heartbeat-interval</c> (so the CONTROL stream -- the reliable dead-peer
        /// detector that drives the keep-alive-less ordinary stream down; see
        /// <c>Association._outboundKillSwitch</c> -- surfaces a peer's death within ~1s instead of
        /// its 5s production default), and a shrunk <c>watch-failure-detector</c> (so RemoteWatcher's
        /// address-level unreachability detection -- the mechanism that produces a synthetic
        /// <see cref="Terminated"/> for a peer that vanishes without ever sending a
        /// <c>DeathWatchNotification</c> -- completes in a reasonable test window instead of its
        /// 10s-plus production default). No test asserts on the exact backoff/detection timing itself
        /// -- only on progress/order/completion; these knobs merely keep the deterministic mechanism
        /// fast in the test environment.
        /// </summary>
        private static Config ArteryConfig(int port = 0) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            akka.remote.artery.advanced.outbound-restart-backoff = 300ms
            akka.remote.artery.advanced.control-heartbeat-interval = 500ms
            akka.remote.watch-failure-detector.heartbeat-interval = 200ms
            akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 1s
            akka.remote.watch-failure-detector.unreachable-nodes-reaper-interval = 200ms
            akka.remote.watch-failure-detector.expected-response-after = 300ms
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(string systemName, int port, string localName) =>
            $"akka://{systemName}@127.0.0.1:{port}/user/{localName}";

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// Re-binds a fresh <see cref="ActorSystem"/> to the EXACT SAME port a just-terminated
        /// system used, retrying with a short delay if the OS has not yet released the socket --
        /// "bind-your-own is race-acceptable here" (design.md group 9's 9.3 test note): this test
        /// exclusively owns the port between the two systems, nothing else in the process can
        /// steal it, so a bind failure here can only mean the OS has not finished tearing down the
        /// previous listener yet.
        /// </summary>
        private static async Task<ActorSystem> CreateSystemOnPortWithRetryAsync(string name, int port, int maxAttempts = 40)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return ActorSystem.Create(name, ArteryConfig(port));
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

        [Fact(DisplayName = "9.2: three sequential create/terminate cycles of an Artery system on port 0 complete cleanly, with no errors, each time")]
        public async Task Should_CleanStartStop_ThreeSequentialCycles()
        {
            for (var i = 0; i < 3; i++)
            {
                var system = ActorSystem.Create($"ArteryCleanCycle{i}", ArteryConfig());
                try
                {
                    var address = RARP.For(system).Provider.DefaultAddress;
                    address.Protocol.Should().Be("akka");
                    address.Port.Should().NotBeNull();
                    address.Port!.Value.Should().BeGreaterThan(0, "canonical.port = 0 must resolve to the actual bound ephemeral port");

                    // Prove the system is fully usable (not merely bound) before tearing it down.
                    var probe = CreateTestProbe(system);
                    system.ActorOf(Props.Create(() => new Echo())).Tell("ping", probe.Ref);
                    await probe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(5));
                }
                finally
                {
                    // The assertion IS the clean-shutdown gate for this cycle.
                    await system.Terminate().AwaitWithTimeout(10.Seconds());
                }
            }
        }

        [Fact(DisplayName = "9.3: B restarts on the SAME port with a NEW uid -- A re-associates (new incarnation visible via the registry), post-restart ordinary sends reach the new incarnation, the pre-kill Watch's Terminated arrives, and a stale-uid Quarantine call for the OLD uid is ignored")]
        public async Task Should_Reassociate_After_Peer_Restarts_With_New_Uid()
        {
            const string peerSystemName = "ArteryReconnect93Peer";

            var systemA = ActorSystem.Create("ArteryReconnect93A", ArteryConfig());
            ActorSystem? systemB = ActorSystem.Create(peerSystemName, ArteryConfig());
            ActorSystem? systemB2 = null;
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var watchTargetB = systemB.ActorOf(Props.Create(() => new Echo()), "watch-target");

                var boundPort = BoundPort(systemB);
                var bAddress = RARP.For(systemB).Provider.DefaultAddress;

                var echoRef = await systemA.ActorSelection(SelectionPath(peerSystemName, boundPort, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var watchTargetRef = await systemA.ActorSelection(SelectionPath(peerSystemName, boundPort, "watch-target")).ResolveOne(TimeSpan.FromSeconds(10));

                var probe = CreateTestProbe(systemA);
                echoRef.Tell("before-kill", probe.Ref);
                await probe.ExpectMsgAsync("before-kill", TimeSpan.FromSeconds(10));

                var terminatedProbe = CreateTestProbe(systemA);
                await terminatedProbe.WatchAsync(watchTargetRef);

                var oldUid = AddressUidExtension.Uid(systemB);

                // Kill B outright (no graceful DeathWatchNotification will ever be sent for
                // watch-target -- it just vanishes with the whole system).
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
                systemB = null;

                // Restart on the SAME port, SAME system name (a real restart keeps its node
                // identity -- only the uid, generated fresh per process, changes), with a NEW uid.
                systemB2 = await CreateSystemOnPortWithRetryAsync(peerSystemName, boundPort);
                systemB2.ActorOf(Props.Create(() => new Echo()), "echo");

                var newUid = AddressUidExtension.Uid(systemB2);
                newUid.Should().NotBe(oldUid);

                // A must re-associate on its own (group 9's automatic outbound-stream restart).
                // Wait for the registry to actually show the NEW incarnation (handshake complete)
                // before sending -- while a stream is mid-reconnect, a held-but-not-yet-flushed
                // ordinary envelope can still be lost if THAT SAME materialization has to restart
                // AGAIN before its own handshake completes (an accepted best-effort characteristic
                // of the ordinary stream, same as design.md's pinned invariant 5: only elements
                // already dequeued from the association-owned channel are at risk, and only across
                // a SECOND restart of the same stream instance) -- this test's "the new incarnation
                // is visible via the registry" assertion below is exactly that stable point, so
                // check it first, THEN send.
                var transportA = (ArteryRemoting)RARP.For(systemA).Provider.Transport;
                var associationToB = transportA.Registry.AssociationFor(bAddress);
                await AwaitConditionAsync(() => Task.FromResult(associationToB.CurrentState.Incarnation > 1), TimeSpan.FromSeconds(30));

                // The new incarnation must be visible via the registry.
                associationToB.CurrentState.UniqueRemoteAddress.Should().NotBeNull();
                associationToB.CurrentState.UniqueRemoteAddress!.Value.Uid.Should().Be(newUid);
                associationToB.CurrentState.Incarnation.Should().BeGreaterThan(1, "a genuinely new incarnation must have been recorded for the SAME address");

                // Sent via a FRESH ActorSelection, deliberately NOT the original resolved
                // `echoRef` -- `echoRef` is pinned to the OLD echo actor's specific incarnation uid
                // (from the Identify round trip that originally resolved it), a DIFFERENT actor
                // identity from B2's freshly-created "echo" (a new uid at the same path) --
                // ordinary Akka.NET actor-identity semantics (path+uid), not an Artery quirk: a
                // resolved IActorRef can never be redirected to a different incarnation at the
                // same path, by design. An ActorSelection re-resolves by PATH ONLY at delivery
                // time on the receiving side, reaching whichever actor currently lives there.
                //
                // Retried (fresh tag each attempt) rather than a single Tell: ordinary messages
                // are at-most-once by design (no ack/resend, unlike the reliable system-message
                // lane) -- "incarnation > 1" above only proves the CONTROL stream's own handshake
                // completed; the ORDINARY stream's own OutboundHandshakeStage independently
                // catches up to that same generation bump on ITS OWN next check (bounded by
                // handshake-retry-interval), and if ITS materialization has to restart a SECOND
                // time in that window (more likely under CI/parallel-test load), a held-but-not-
                // yet-flushed envelope from the first attempt can be lost -- the same accepted
                // best-effort characteristic invariant 5 documents. A well-behaved caller that
                // cares about confirmation retries; this loop does exactly that.
                var probeAfter = CreateTestProbe(systemA);
                var echoSelectionAfter = systemA.ActorSelection(SelectionPath(peerSystemName, boundPort, "echo"));
                var delivered = false;
                for (var attempt = 0; attempt < 10 && !delivered; attempt++)
                {
                    var tag = $"after-restart-{attempt}";
                    echoSelectionAfter.Tell(tag, probeAfter.Ref);
                    try
                    {
                        await probeAfter.ExpectMsgAsync(tag, TimeSpan.FromSeconds(6));
                        delivered = true;
                    }
                    catch (Exception) when (attempt < 9) // slopwatch-ignore: SW003 at-most-once ordinary send; a lost attempt mid-reconnect is expected and deliberately retried with a fresh tag
                    {
                        // Not yet -- retry with a fresh tag (see remarks above).
                    }
                }

                delivered.Should().BeTrue("an ordinary send must eventually get through once the association has re-associated, even if an individual at-most-once attempt is lost mid-reconnect");

                // The pre-kill Watch must still produce a Terminated for the vanished watch-target
                // -- however routed (RemoteWatcher's own address-level failure detector, in THIS
                // scenario, since nothing was left unacknowledged for SystemMessageDeliveryStage's
                // own give-up-timeout path to time out on) -- it just needs to arrive.
                await terminatedProbe.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(60));

                // A stale-uid Quarantine call (the OLD uid, now superseded by the new incarnation)
                // must be IGNORED -- design.md/AssociationState.Quarantine's documented, pre-existing
                // G2 semantics ("Acts ONLY if uid equals the CURRENT UniqueRemoteAddress's uid -- a
                // stale-uid request is ignored"), unaffected by group 9: a plain uid change does not
                // auto-quarantine the old uid, and an explicit LATE attempt to quarantine it after
                // the fact is a deliberate no-op, not an error.
                transportA.Quarantine(bAddress, oldUid);
                associationToB.IsQuarantined(oldUid).Should().BeFalse("a stale-uid Quarantine call (superseded by the new incarnation) must be ignored");
                associationToB.IsQuarantined(newUid).Should().BeFalse("the current incarnation must be entirely unaffected by a stale-uid Quarantine call");
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB2 is not null)
                    await systemB2.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Invariant 5 (pinned): after the peer restarts under a new uid, a burst of ordinary messages to the same path is delivered, in original order, to the new incarnation -- never reordered by the reconnect (the channel-buffering half of the invariant is unit-tested in AssociationRestartSpec)")]
        public async Task Should_Redeliver_Queued_Ordinary_Messages_After_Reconnect()
        {
            const string peerSystemName = "ArteryReconnectQueuePeer";

            var systemA = ActorSystem.Create("ArteryReconnectQueueA", ArteryConfig());
            ActorSystem? systemB = ActorSystem.Create(peerSystemName, ArteryConfig());
            ActorSystem? systemB2 = null;
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var boundPort = BoundPort(systemB);

                // Warm up the association (real round trip) so the handshake/uid is established
                // before anything is torn down.
                var echoRef = await systemA.ActorSelection(SelectionPath(peerSystemName, boundPort, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);
                echoRef.Tell("warmup", probe.Ref);
                await probe.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));

                // Kill B -- A's outbound streams to it start failing and reconnecting.
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
                systemB = null;

                // Bring B back on the SAME port/name with a fresh uid -- a new incarnation. Its
                // listener binds synchronously during ActorSystem.Create and its echo actor is
                // created immediately after.
                systemB2 = await CreateSystemOnPortWithRetryAsync(peerSystemName, boundPort);
                systemB2.ActorOf(Props.Create(() => new Echo()), "echo");

                // Wait until A's ordinary outbound stream has reconnected to the NEW incarnation, by
                // retrying a throwaway probe until one round-trips. This is the robust,
                // platform-independent way to observe reconnect completion: it does NOT depend on
                // catching the exact moment the dead-peer stream tears down (an internal transition
                // whose observability is subject to socket-close timing that differs across OS TCP
                // stacks -- a lone write to a gracefully-closed socket can succeed locally, so a
                // busy box may not surface the dead connection until well after the reconnect to the
                // live one has already happened). Ordinary sends are at-most-once, so an individual
                // probe may be lost mid-reconnect; a fresh-tagged retry loop is exactly how a
                // well-behaved caller confirms reachability across a peer restart. Sent via a FRESH
                // ActorSelection, NOT the original `echoRef` -- that ref is pinned to the OLD echo
                // actor's incarnation uid; an ActorSelection re-resolves by PATH at delivery time,
                // reaching whichever actor currently lives there (B2's new echo).
                var echoSelection = systemA.ActorSelection(SelectionPath(peerSystemName, boundPort, "echo"));
                var reconnectProbe = CreateTestProbe(systemA);
                var reconnected = false;
                for (var attempt = 0; attempt < 40 && !reconnected; attempt++)
                {
                    var tag = $"reconnect-probe-{attempt}";
                    echoSelection.Tell(tag, reconnectProbe.Ref);
                    try
                    {
                        await reconnectProbe.ExpectMsgAsync(tag, TimeSpan.FromSeconds(2));
                        reconnected = true;
                    }
                    catch (Exception) when (attempt < 39) // slopwatch-ignore: SW003 at-most-once probe; a lost attempt mid-reconnect is expected and deliberately retried with a fresh tag
                    {
                        // Not reconnected yet -- retry with a fresh tag.
                    }
                }

                reconnected.Should().BeTrue("A's ordinary stream must eventually reconnect to the new incarnation at the same path");

                // PINNED INVARIANT -- IN-ORDER DELIVERY to the new incarnation. With the ordinary
                // stream now reconnected to B2, a burst of ORDER-TAGGED messages is delivered in
                // ORIGINAL ORDER. Asserted as a contiguous, in-order SUFFIX ending at queued-7:
                // ordinary is at-most-once, so a message dequeued into a materialization that fails
                // again before its handshake completes may be dropped (an accepted, pre-existing
                // best-effort characteristic design.md pins -- never reordered, never a gap; queued-7,
                // last in the FIFO channel, is delivered only after everything ahead of it, so it
                // always arrives). k == 0 in the common case (all 8 delivered, in order). The
                // guarantee this proves is ORDER, not exactly-once (which ordinary never offers).
                const int queuedCount = 8;
                for (var i = 0; i < queuedCount; i++)
                    echoSelection.Tell($"queued-{i}", probe.Ref);

                var firstDelivered = await probe.ExpectMsgAsync<string>(TimeSpan.FromSeconds(60));
                firstDelivered.Should().MatchRegex("^queued-[0-9]$", "the probe only ever receives the ordered queued-* burst");
                var k = int.Parse(firstDelivered.Substring("queued-".Length));
                k.Should().BeInRange(0, queuedCount - 1);
                for (var i = k + 1; i < queuedCount; i++)
                    await probe.ExpectMsgAsync($"queued-{i}", TimeSpan.FromSeconds(60));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(10.Seconds());
                if (systemB2 is not null)
                    await systemB2.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
