//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundLanesRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers the outbound-lanes assembly's LOSS/LIVENESS behavior at the connection seam
    /// (<c>ArteryRemoting.MaterializeOrdinaryOutboundWithLanes</c>): a transient TCP fault must
    /// never discard an association's in-flight set (the elements already dequeued from the
    /// restart-safe lane channels into the stream stages), and an idle-but-healthy assembly must
    /// never need a trailing "nudge" send to flush what it already accepted. The connection is
    /// wrapped in an inner <c>RestartFlow.OnFailuresWithBackoff</c> (Pekko's
    /// <c>connectionFlowWithRestart</c>) so connect faults retry the SOCKET without settling the
    /// lane chains.
    ///
    /// <para>
    /// <b>Maintainer policy, honored throughout this file (same as <see cref="ArteryOutboundLanesSpec"/>):</b>
    /// no wall-clock thresholds. Every assertion is progress/order/completion/bounded-state;
    /// <c>Task.Delay</c> appears only as STIMULUS (spacing sparse traffic), never as an assertion.
    /// </para>
    /// </summary>
    public class ArteryOutboundLanesRestartSpec : AkkaSpec
    {
        public ArteryOutboundLanesRestartSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(int outboundLanes, int? canonicalPort = null) => ConfigurationFactory.ParseString($"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {canonicalPort ?? 0}
            akka.remote.artery.advanced.outbound-lanes = {outboundLanes}
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private static Address RemoteAddressOf(ActorSystem system) =>
            new("akka", system.Name, "127.0.0.1", BoundPort(system));

        private static Association AssociationFor(ActorSystem from, Address remoteAddress) =>
            ((ArteryRemoting)RARP.For(from).Provider.Transport).Registry.AssociationFor(remoteAddress);

        /// <summary>
        /// Forwards everything it receives to a receiver-side probe -- the receiving system's test
        /// kit then counts receipts IN-PROCESS, so awaiting delivery generates ZERO additional
        /// traffic on the wire under test (the whole point of the end-of-burst spec).
        /// </summary>
        private sealed class ForwardToProbe : ReceiveActor
        {
            public ForwardToProbe(IActorRef target)
            {
                ReceiveAny(msg => target.Tell(msg));
            }
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        // ----------------------------------------------------------------------------------
        // Test 1: end-of-burst -- exactly N messages through a lanes>1 path, then TOTAL wire
        // silence; all N must arrive with no trailing nudge. Guards against the dump-proven
        // failure mode where a seam fault settled the assembly and discarded the in-flight set:
        // every element the transport accepted must either reach the peer or surface as Dropped,
        // and a healthy assembly must flush everything it dequeued without further stimulus.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Outbound lanes: an exact burst of N messages is fully delivered with NO trailing traffic (end-of-burst, no nudge)")]
        public async Task Should_Deliver_Exact_Burst_Without_Trailing_Nudge_Across_Lanes()
        {
            const int lanes = 4;
            const int minRecipients = 4;
            const int maxRecipients = 16;
            const int perRecipientCount = 125;

            var config = ArteryConfig(outboundLanes: lanes);
            var systemA = ActorSystem.Create("ArteryLanesBurstA", config);
            var systemB = ActorSystem.Create("ArteryLanesBurstB", config);
            try
            {
                // ONE receiver-side probe shared by every recipient: receipt counting is entirely
                // local to systemB from here on.
                //
                // Multi-lane engagement is ASSERTED, not assumed: recipient uids are pseudo-random,
                // so a fixed recipient count can (rarely) hash every recipient onto ONE lane and
                // silently turn this into a single-lane test. Recipients are added -- deterministic
                // names, real resolved uids -- until Association.SelectLane (the EXACT routing
                // function the transport itself uses per ResolveSendRoute) covers at least 2
                // distinct lanes.
                var receiverProbe = CreateTestProbe(systemB);
                var refs = new List<IActorRef>();
                var lanesCovered = new HashSet<int>();
                for (var i = 0; i < maxRecipients && (refs.Count < minRecipients || lanesCovered.Count < 2); i++)
                {
                    systemB.ActorOf(Props.Create(() => new ForwardToProbe(receiverProbe.Ref)), $"sink-{i}");
                    var resolved = await systemA.ActorSelection(SelectionPath(systemB, $"sink-{i}")).ResolveOne(TimeSpan.FromSeconds(10));
                    refs.Add(resolved);
                    lanesCovered.Add(Association.SelectLane(resolved.Path.Uid, lanes));
                }

                lanesCovered.Count.Should().BeGreaterOrEqualTo(2,
                    "the burst must genuinely engage MULTIPLE lanes -- with up to {0} distinct real uids over {1} lanes, coverage below 2 means the lane hash is broken", maxRecipients, lanes);

                var recipientCount = refs.Count;
                var total = recipientCount * perRecipientCount;

                // The burst: exactly `total` sends, interleaved across the recipients, fired as
                // fast as the loop runs. NOTHING is sent on this association after the loop ends --
                // the delivery await below is in-process on systemB.
                for (var m = 0; m < perRecipientCount; m++)
                    for (var i = 0; i < recipientCount; i++)
                        refs[i].Tell($"r{i}-{m}", ActorRefs.NoSender);

                var received = (await receiverProbe.ReceiveNAsync(total, TimeSpan.FromSeconds(30)).ToListAsync())
                    .Cast<string>()
                    .ToList();

                received.Should().HaveCount(total, "every message the transport accepted must arrive without any trailing nudge send");

                // Per-recipient order must also hold (same recipient => same lane => FIFO).
                for (var i = 0; i < recipientCount; i++)
                {
                    var forRecipient = received.Where(msg => msg.StartsWith($"r{i}-")).ToArray();
                    forRecipient.Should().Equal(
                        Enumerable.Range(0, perRecipientCount).Select(m => $"r{i}-{m}"),
                        "recipient {0}'s messages must arrive complete and in send order", i);
                }
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        // ----------------------------------------------------------------------------------
        // Test 2: sparse trickle -- single messages over otherwise-idle lanes. Guards against
        // (a) an idle assembly wedging between sends (each message must round-trip on its own,
        // with no follow-up traffic to shake it loose) and (b) restart thrash: an idle, healthy
        // one-way connection must NOT be churned by the inner RestartFlow (half-closed read-side
        // EOF is not a failure) -- asserted via the association's own restart marker.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Outbound lanes: a sparse one-message-at-a-time trickle round-trips every message over idle lanes, with no restart churn")]
        public async Task Should_Deliver_Sparse_Trickle_Over_Idle_Lanes_Without_Restart_Churn()
        {
            const int iterations = 10;

            var config = ArteryConfig(outboundLanes: 4);
            var systemA = ActorSystem.Create("ArteryLanesTrickleA", config);
            var systemB = ActorSystem.Create("ArteryLanesTrickleB", config);
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var echoRef = await systemA.ActorSelection(SelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);

                // INNER-tier tripwire: every inner connection restart logs RestartFlow's
                // "Restarting graph due to failure." warning (RestartFlow.cs) on systemA, so a
                // zero-count filter over the whole trickle catches restart churn the OUTER marker
                // below cannot see (HasOutboundEverRestarted only observes whole-assembly
                // restarts). An EventFilter on a hand-created system is silently VACUOUS unless
                // that system's config carries the TestEventListener logger (see ArteryConfig) --
                // so first PROVE this filter is armed by asserting it catches one synthetic
                // warning logged directly through systemA's own event stream.
                await CreateEventFilter(systemA).Warning(contains: "Restarting graph").ExpectOneAsync(() =>
                {
                    systemA.Log.Warning("Restarting graph due to failure. (synthetic probe: proves the filter below is armed, never emitted by production code with this suffix)");
                    return Task.CompletedTask;
                });

                await CreateEventFilter(systemA).Warning(contains: "Restarting graph").ExpectAsync(0, async () =>
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        echoRef.Tell($"trickle-{i}", probe.Ref);
                        await probe.ExpectMsgAsync($"trickle-{i}", TimeSpan.FromSeconds(10));

                        // STIMULUS spacing only (never an assertion): leave the lanes + connection
                        // genuinely idle between sends so any restart churn / idle-completion defect
                        // has room to fire before the next message.
                        await Task.Delay(TimeSpan.FromMilliseconds(500));
                    }
                });

                // No restart thrash: the ordinary assembly of a healthy, mostly-idle association
                // must never have been torn down and rebuilt across the whole trickle. This is the
                // regression tripwire for the RestartFlow-vs-half-close completion semantics (a
                // one-way connection's read side EOFs immediately; that must not count as failure
                // OR completion of the write side).
                var association = AssociationFor(systemA, RemoteAddressOf(systemB));
                association.HasOutboundEverRestarted.Should().BeFalse(
                    "an idle-but-healthy lanes assembly must not be restarted between sparse sends");
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        // ----------------------------------------------------------------------------------
        // Test 3: connect race -- a FRESH association whose first connect attempt fails (nothing
        // is listening yet) and then succeeds (the peer comes up on the same port). The priming
        // burst -- enqueued before any connection ever existed -- must survive: the inner
        // RestartFlow retries the SOCKET with backoff while the lane chains (and the elements
        // they hold) stay up. Before the fix, every failed connect settled the whole assembly and
        // discarded the elements already dequeued into the stages (one held per active lane, per
        // attempt) -- silently.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Outbound lanes: a priming burst enqueued before the peer is reachable survives a failed-then-successful connect")]
        public async Task Should_Deliver_Priming_Burst_When_First_Connect_Fails_Then_Succeeds()
        {
            const int burstSize = 50;
            const string systemBName = "ArteryLanesRaceB";

            // Phase 1: bind a real port, then FREE it -- self-bind-then-release, the port the
            // reborn peer will reclaim. (Not a reserve-then-release probe of a foreign port: the
            // same ActorSystem name/config rebinds it below, and the only consumer in between is
            // the system under test, whose connect attempts are EXPECTED to fail.)
            var firstIncarnation = ActorSystem.Create(systemBName, ArteryConfig(outboundLanes: 4));
            var port = BoundPort(firstIncarnation);
            await firstIncarnation.Terminate().AwaitWithTimeout(15.Seconds());

            var systemA = ActorSystem.Create("ArteryLanesRaceA", ArteryConfig(outboundLanes: 4));
            ActorSystem? systemB = null;
            try
            {
                // Provider-resolved ref: NO wire round-trip, so the association is stone cold --
                // the burst below is its first-ever ordinary traffic and the FIRST connect attempt
                // is guaranteed to hit a dead port.
                var target = RARP.For(systemA).Provider.ResolveActorRef(
                    $"akka://{systemBName}@127.0.0.1:{port}/user/collector");

                for (var i = 0; i < burstSize; i++)
                    target.Tell($"prime-{i}", ActorRefs.NoSender);

                // Phase 2: the peer comes up on the SAME port while the sender's inner connection
                // restart is backing off. Bind can transiently lose the rebind race against the
                // previous incarnation's teardown -- retry the CREATE (stimulus, not assertion).
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        systemB = ActorSystem.Create(systemBName, ArteryConfig(outboundLanes: 4, canonicalPort: port));
                        break;
                    }
                    catch (Exception) when (attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500));
                    }
                }

                var receiverProbe = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new ForwardToProbe(receiverProbe.Ref)), "collector");

                // Generous window: inner connect retries back off per outbound-restart-backoff
                // (1s default, growing), plus the control-stream handshake once the peer is up.
                var received = (await receiverProbe.ReceiveNAsync(burstSize, TimeSpan.FromSeconds(45)).ToListAsync())
                    .Cast<string>()
                    .ToList();

                received.Should().Equal(
                    Enumerable.Range(0, burstSize).Select(i => $"prime-{i}"),
                    "the ENTIRE priming burst must survive the failed first connect, complete and in send order -- " +
                    "a lost element here means the connect fault settled the lane chains and discarded their in-flight elements");
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                if (systemB is not null)
                    await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }
    }
}
