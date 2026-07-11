//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundLanesSpec.cs" company="Akka.NET Project">
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
    /// Covers Artery TCP remoting's OUTBOUND LANES feature (<c>akka.remote.artery.advanced.outbound-lanes</c>,
    /// <see cref="ArterySettings.OutboundLanes"/>): fanning ordinary-stream sends across N independent,
    /// bounded lane channels -- hashed by recipient uid (<see cref="Association.SelectLane"/>), cached per
    /// <see cref="RemoteActorRef"/> (<see cref="RemoteActorRef.CachedSendQueueIndex"/>) -- all still merged
    /// (Akka.Streams <c>MergeHub</c>) onto ONE TCP connection per association.
    ///
    /// <para>
    /// <b>Maintainer policy, honored throughout this file:</b> no wall-clock thresholds. Every assertion is
    /// progress/order/completion/bounded-state, matching the sibling <see cref="ArteryBackpressureSpec"/>/
    /// <see cref="ArteryLargeMessageStreamSpec"/> files. Ordering proofs use a trailing "ask" to the SAME
    /// recipient as a mailbox-FIFO synchronization barrier rather than a sleep/delay.
    /// </para>
    ///
    /// <para>
    /// Gate B (zero behavioral change at the <c>outbound-lanes = 1</c> shipping default) is covered by the
    /// pre-existing Artery suite passing unmodified (192 specs at the base commit this feature was built on)
    /// plus <see cref="ArteryConfigSpec"/>'s settings-default assertions -- not re-proven here; this file is
    /// scoped to lanes &gt; 1 behavior.
    /// </para>
    /// </summary>
    public class ArteryOutboundLanesSpec : AkkaSpec
    {
        public ArteryOutboundLanesSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(int outboundLanes) => ConfigurationFactory.ParseString($"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-lanes = {outboundLanes}
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string SelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private static Address RemoteAddressOf(ActorSystem system) =>
            new("akka", system.Name, "127.0.0.1", BoundPort(system));

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// Marker request an <see cref="OrderRecorder"/> replies to with everything it has received so far,
        /// in receipt order -- the ordering test's synchronization barrier (mailbox FIFO guarantees this is
        /// answered only after every PRIOR message to the SAME recipient has already been processed).
        /// </summary>
        private sealed class GetReceived
        {
            public static readonly GetReceived Instance = new();
            private GetReceived() { }
        }

        /// <summary>
        /// Records every <see cref="string"/> it receives, in receipt order, and hands the list back on
        /// <see cref="GetReceived"/>.
        /// </summary>
        private sealed class OrderRecorder : ReceiveActor
        {
            private readonly List<string> _received = new();

            public OrderRecorder()
            {
                Receive<string>(msg => _received.Add(msg));
                Receive<GetReceived>(_ => Sender.Tell(_received.ToArray()));
            }
        }

        // ----------------------------------------------------------------------------------
        // Pure unit tests for Association.SelectLane -- no ActorSystem needed.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "SelectLane should be a deterministic, pure function of (uid, lanes)")]
        public void SelectLane_should_be_deterministic()
        {
            Association.SelectLane(123456789L, 4).Should().Be(Association.SelectLane(123456789L, 4));
            Association.SelectLane(-987654321L, 8).Should().Be(Association.SelectLane(-987654321L, 8));
        }

        [Theory(DisplayName = "SelectLane should always return 0 when lanes = 1 (gate B: single-lane collapse)")]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        public void SelectLane_should_return_zero_when_lanes_is_one(long uid)
        {
            Association.SelectLane(uid, 1).Should().Be(0);
        }

        [Fact(DisplayName = "SelectLane should return a value in [0, lanes) for every input, including the extreme uid values Math.Abs would throw on")]
        public void SelectLane_should_stay_in_range_for_extreme_uids()
        {
            foreach (var uid in new[] { 0L, 1L, -1L, long.MinValue, long.MaxValue, long.MinValue + 1 })
            {
                var lane = Association.SelectLane(uid, 4);
                lane.Should().BeInRange(0, 3, $"uid {uid} must map into a valid lane, never throw (the Math.Abs(long.MinValue) edge this must avoid)");
            }
        }

        [Fact(DisplayName = "SelectLane should spread a range of distinct uids across every configured lane")]
        public void SelectLane_should_spread_uids_across_all_lanes()
        {
            var observedLanes = Enumerable.Range(0, 1000)
                .Select(uid => Association.SelectLane(uid, 4))
                .ToHashSet();

            observedLanes.Should().BeEquivalentTo(new[] { 0, 1, 2, 3 }, "1000 distinct sequential uids over 4 lanes must exercise every lane");
        }

        // ----------------------------------------------------------------------------------
        // Test 1: per-recipient ordering at outbound-lanes = 4 with many concurrent senders to
        // ~16 distinct remote recipients.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Outbound lanes: many concurrent senders to 16 distinct remote recipients each see their OWN messages in send order")]
        public async Task Should_Preserve_PerRecipient_Order_With_Many_Concurrent_Senders_Across_Lanes()
        {
            const int recipientCount = 16;
            const int perRecipientCount = 30;

            var config = ArteryConfig(outboundLanes: 4);
            var systemA = ActorSystem.Create("ArteryLanesOrderingA", config);
            var systemB = ActorSystem.Create("ArteryLanesOrderingB", config);
            try
            {
                var refs = new IActorRef[recipientCount];
                for (var i = 0; i < recipientCount; i++)
                {
                    systemB.ActorOf(Props.Create(() => new OrderRecorder()), $"recorder-{i}");
                    refs[i] = await systemA.ActorSelection(SelectionPath(systemB, $"recorder-{i}")).ResolveOne(TimeSpan.FromSeconds(10));
                }

                // Many concurrent senders -- ONE task per recipient, each sending its OWN sequential
                // 0..N-1 stream as fast as possible, all 16 tasks racing each other concurrently. Akka's
                // per-sender-per-recipient ordering guarantee only promises order within a SINGLE
                // sender's sequence, so each recipient must be driven by exactly one logical sender here.
                var senderTasks = new Task[recipientCount];
                for (var i = 0; i < recipientCount; i++)
                {
                    var recipient = refs[i];
                    senderTasks[i] = Task.Run(() =>
                    {
                        for (var m = 0; m < perRecipientCount; m++)
                            recipient.Tell(m.ToString(), ActorRefs.NoSender);
                    });
                }

                await Task.WhenAll(senderTasks);

                // Synchronization barrier per recipient: GetReceived is enqueued onto the SAME lane,
                // strictly after all perRecipientCount sends above -- mailbox FIFO means it is only
                // answered once every prior message to this SAME recipient has already been recorded.
                foreach (var recipient in refs)
                {
                    var probe = CreateTestProbe(systemA);
                    recipient.Tell(GetReceived.Instance, probe.Ref);
                    var received = await probe.ExpectMsgAsync<string[]>(TimeSpan.FromSeconds(20));

                    received.Should().Equal(
                        Enumerable.Range(0, perRecipientCount).Select(m => m.ToString()),
                        "each recipient must see its OWN sender's messages in EXACTLY the order they were sent, regardless of how many OTHER recipients' concurrent traffic shared (or didn't share) its lane");
                }
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        // ----------------------------------------------------------------------------------
        // Test 2: lane selection is cached per RemoteActorRef; the SAME recipient always resolves
        // to the SAME lane; DISTINCT recipients spread across lanes.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Outbound lanes: the routing decision is cached per RemoteActorRef, the SAME recipient always reuses its cached lane, and DISTINCT recipients spread across lanes")]
        public async Task Should_Cache_Lane_Routing_Decision_Per_RemoteActorRef_And_Spread_Distinct_Recipients()
        {
            const int recipientCount = 8;
            const int lanes = 4;

            var config = ArteryConfig(outboundLanes: lanes);
            var systemA = ActorSystem.Create("ArteryLanesCacheA", config);
            var systemB = ActorSystem.Create("ArteryLanesCacheB", config);
            try
            {
                var refs = new RemoteActorRef[recipientCount];
                for (var i = 0; i < recipientCount; i++)
                {
                    systemB.ActorOf(Props.Create(() => new Echo()), $"echo-{i}");
                    var resolved = await systemA.ActorSelection(SelectionPath(systemB, $"echo-{i}")).ResolveOne(TimeSpan.FromSeconds(10));
                    refs[i] = (RemoteActorRef)resolved;
                }

                // Not yet resolved: the sentinel is untouched until the FIRST ordinary send through
                // ArteryRemoting.EnqueueOutbound (resolving a ref via ActorSelection/Identify does not,
                // by itself, route a message through the FINAL target's own cache -- Identify travels to
                // the selection's anchor, a different ref entirely).
                foreach (var r in refs)
                    r.CachedSendQueueIndex.Should().Be(-1, "the cache must be untouched before this ref's first ordinary send");

                // First send per recipient -- resolves (and caches) the route.
                var probe = CreateTestProbe(systemA);
                foreach (var r in refs)
                {
                    r.Tell("ping", probe.Ref);
                    await probe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(10));
                }

                var firstResolution = refs.Select(r => r.CachedSendQueueIndex).ToArray();
                foreach (var lane in firstResolution)
                    lane.Should().BeInRange(0, lanes - 1, "every cached route must be a valid ordinary-lane index (large-message-destinations is not configured in this test)");

                // SAME recipient, SAME lane: sending again must not change the cached decision.
                foreach (var r in refs)
                {
                    r.Tell("ping-again", probe.Ref);
                    await probe.ExpectMsgAsync("ping-again", TimeSpan.FromSeconds(10));
                }

                for (var i = 0; i < recipientCount; i++)
                    refs[i].CachedSendQueueIndex.Should().Be(firstResolution[i], "the cached routing decision must be STABLE across repeated sends to the same ref -- it is computed once, not recomputed per send");

                // DISTINCT recipients spread across lanes: with 8 real, distinct remote-actor uids over
                // 4 lanes, seeing more than a single lane confirms the hash actually distributes rather
                // than collapsing everything onto one lane.
                firstResolution.Distinct().Count().Should().BeGreaterThan(1,
                    "distinct recipients (distinct uids) must spread across more than one lane, not all collapse onto the same one");
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(15.Seconds());
                await systemB.Terminate().AwaitWithTimeout(15.Seconds());
            }
        }

        // ----------------------------------------------------------------------------------
        // Test 3: per-lane drop accounting -- overflowing ONE lane leaves every OTHER lane (and
        // the control channel) unaffected.
        // ----------------------------------------------------------------------------------

        [Fact(DisplayName = "Outbound lanes: overflowing ONE lane names that lane in its Dropped events, leaves OTHER lanes' traffic unaffected, and never quarantines (ordinary overflow stays a soft drop)")]
        public async Task Should_Overflow_Only_The_Targeted_Lane_And_Leave_Other_Lanes_Unaffected()
        {
            const int lanes = 4;
            const int capacity = 8;
            // Port 0 is not an assignable listener port -- deterministic, immediate connection failure,
            // no reservation/no reserve-then-release race (same idiom as ArteryBackpressureSpec).
            const int deadPort = 0;
            var deadAddress = new Address("akka", "dead-sys", "127.0.0.1", deadPort);

            var config = ConfigurationFactory.ParseString($"""
                akka.remote.artery.advanced.outbound-message-queue-size = {capacity}
                """).WithFallback(ArteryConfig(outboundLanes: lanes));

            var systemA = ActorSystem.Create("ArteryLanesDropA", config);
            var systemHealthy = ActorSystem.Create("ArteryLanesDropHealthy", config);
            try
            {
                systemHealthy.ActorOf(Props.Create(() => new Echo()), "echo");

                // Explicit #uid suffixes give us deterministic, KNOWN lane placement (SelectLane(uid, 4)
                // == uid for these small values) without needing a live peer -- the target never needs
                // to actually exist since this association's connection is permanently unreachable.
                var floodTarget = (RemoteActorRef)RARP.For(systemA).Provider.ResolveActorRef(
                    $"akka://dead-sys@127.0.0.1:{deadPort}/user/flood#0"); // uid 0 -> lane 0
                var quietTarget = (RemoteActorRef)RARP.For(systemA).Provider.ResolveActorRef(
                    $"akka://dead-sys@127.0.0.1:{deadPort}/user/quiet#1"); // uid 1 -> lane 1

                var droppedProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(droppedProbe.Ref, typeof(Dropped));
                var quarantineProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(quarantineProbe.Ref, typeof(QuarantinedEvent));

                // Flood lane 0 well past capacity.
                const int floodCount = capacity + 200;
                for (var i = 0; i < floodCount; i++)
                    floodTarget.Tell($"flood-{i}", ActorRefs.NoSender);

                // A MODEST send to lane 1 -- comfortably under capacity, must never overflow just
                // because a DIFFERENT lane on the SAME association is being flooded.
                const int quietCount = 3;
                for (var i = 0; i < quietCount; i++)
                    quietTarget.Tell($"quiet-{i}", ActorRefs.NoSender);

                var dropped = await droppedProbe
                    .ReceiveWhileAsync<Dropped>(_ => true, max: TimeSpan.FromSeconds(20), idle: TimeSpan.FromSeconds(3), msgs: floodCount)
                    .ToListAsync();

                dropped.Should().NotBeEmpty("flooding well past the lane's capacity against an unreachable peer must overflow it");
                dropped.Should().OnlyContain(d => d.Reason.Contains("lane [0]"),
                    "every Dropped event from this flood must identify LANE 0 -- never lane 1, which received only the modest, well-under-capacity quiet traffic");
                dropped.Count.Should().BeGreaterOrEqualTo(floodCount - capacity - 20,
                    "essentially the whole overflow past capacity must be published as Dropped, allowing slack for the few elements that may have been mid-flight toward the doomed connection attempt");

                await quarantineProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

                var association = ((ArteryRemoting)RARP.For(systemA).Provider.Transport).Registry.AssociationFor(deadAddress);
                association.LaneQueueCount(0).Should().BeLessOrEqualTo(capacity, "lane 0's occupied size must never exceed its own configured capacity");
                association.LaneQueueCount(1).Should().BeLessOrEqualTo(quietCount, "lane 1 must never have MORE than what was actually sent to it -- it was never flooded");
                association.LaneQueueCount(2).Should().Be(0, "lane 2 received no traffic at all in this test");
                association.LaneQueueCount(3).Should().Be(0, "lane 3 received no traffic at all in this test");

                // NO WEDGE: a completely saturated association to a dead peer must not affect a
                // DIFFERENT, healthy association.
                var echoRef = await systemA.ActorSelection(
                    $"akka://{systemHealthy.Name}@127.0.0.1:{BoundPort(systemHealthy)}/user/echo").ResolveOne(TimeSpan.FromSeconds(10));
                var probe = CreateTestProbe(systemA);
                echoRef.Tell("still-alive", probe.Ref);
                await probe.ExpectMsgAsync("still-alive", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemHealthy.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
