//-----------------------------------------------------------------------
// <copyright file="ArteryBackpressureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
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
    /// End-to-end tests for design.md task group 8, "Bounded Queues And Backpressure" -- task 8.5,
    /// "Add slow receiver tests proving queues do not grow unbounded". Tasks 8.1-8.4 (the bounded
    /// channels themselves + their overflow policies -- ordinary drops to dead letters, control/
    /// system quarantines) landed already with task groups 6/7; see <see cref="AssociationRegistrySpec"/>
    /// for the pure, deterministic unit-level proofs of the bounded-queue shape itself
    /// (fill-to-capacity / reject-overflow / resume-after-drain, both queues, no ActorSystem
    /// needed). This file proves the SAME "cannot grow unbounded" property one level up, through
    /// the full <see cref="ArteryRemoting"/> transport, against a genuinely unresponsive peer.
    ///
    /// <para>
    /// <b>Maintainer policy, honored throughout this file:</b> no wall-clock thresholds, no gap/
    /// latency arithmetic. Every assertion is PROGRESS, ORDER, COMPLETION, or BOUNDED STATE.
    /// Timeouts that appear below are all liveness awaits ("this must eventually happen"), never a
    /// measurement of how long something took.
    /// </para>
    ///
    /// <para>
    /// <b>How "slow/unresponsive receiver" is simulated, and why (read before editing).</b> Every
    /// message dispatch on the INBOUND side (<c>ArteryRemoting.DispatchInbound</c>) is a
    /// non-blocking <c>Tell</c> into the recipient's mailbox -- a receiving actor that never
    /// processes its mailbox does NOT, by itself, create any backpressure on the sender: the
    /// inbound stream's sink keeps pulling (and the TCP socket keeps being read) regardless of how
    /// slowly, or never, the recipient's mailbox drains. The only way a real association's bounded
    /// OUTBOUND channel fills up today is if its materialized outbound stream stops draining it
    /// entirely -- which is exactly what happens when the outbound TCP connection fails (see
    /// <c>ArteryRemoting.MaterializeOutboundStream</c>'s documented G2 scope: "no reconnect/retry --
    /// if the connection fails, this association's outbound stream simply ends ... nothing will
    /// ever drain it again until the process is restarted"). These tests target that exact,
    /// deterministic trigger -- an outbound connection to a host:port nobody is listening on --
    /// rather than trying to throttle a live TCP stream's throughput from the receiving side (which
    /// would be flaky, platform-dependent, and would require wall-clock measurement to even
    /// detect). From the SENDING association's point of view, a peer that never accepts a
    /// connection and a peer that accepts one and then never reads from it are indistinguishable --
    /// both leave the sender's bounded channel with no consumer, which is exactly the property
    /// task 8.5 asks to prove: the queue fills, the overflow policy applies, and nothing grows
    /// without bound.
    /// </para>
    /// </summary>
    public class ArteryBackpressureSpec : AkkaSpec
    {
        public ArteryBackpressureSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// A minimal actor that just watches <paramref name="target"/> at construction -- one
        /// reliable-delivery <c>Watch</c> system message per instance (mirrors
        /// <c>ArterySystemMessageDeliverySpec.PlainWatcher</c>).
        ///
        /// <para>
        /// <b>Each instance MUST watch a DISTINCT remote path</b> (read before reusing this for a
        /// bigger flood). <c>RemoteActorRef.SendSystemMessage</c> intercepts <c>Watch</c>/<c>Unwatch</c>
        /// and hands them to the LOCAL <c>RemoteWatcher</c> actor (<c>RemoteActorRef.IsWatchIntercepted</c>),
        /// which dedupes multiple LOCAL watchers of the SAME remote actor into a single underlying
        /// wire-level <c>Watch</c> (<c>RemoteWatcher.AddWatching</c> calls <c>Context.Watch(watchee)</c>
        /// from ITS OWN cell, which is a no-op for a watchee it already watches). N instances all
        /// watching the SAME target therefore produce just ONE real system message, not N --
        /// distinct watchees defeat the dedup and reliably flood N distinct <c>Watch</c> system
        /// messages onto the association's control channel.
        /// </para>
        /// </summary>
        private sealed class PlainWatcher : ReceiveActor
        {
            public PlainWatcher(IActorRef target)
            {
                Context.Watch(target);
                Receive<Terminated>(_ => { });
            }
        }

        [Fact(DisplayName = "Ordinary outbound queue: flooding an association to an unresponsive peer dead-letters the overflow, keeps the queue's occupied size bounded at capacity, and does not wedge sends to a different, healthy association")]
        public async Task Should_DeadLetter_Ordinary_Overflow_Against_Unresponsive_Peer_Without_Wedging_Other_Associations()
        {
            const int capacity = Association.DefaultOutboundQueueCapacity;
            // Port 0 is not an assignable listener port, so it is a permanently-unreachable peer --
            // deterministic connection failure, no reservation, no reserve-then-release race.
            const int deadPort = 0;
            var deadAddress = new Address("akka", "dead-sys", "127.0.0.1", deadPort);

            var systemA = ActorSystem.Create("ArteryBackpressureOrdinaryA", ArteryConfig());
            var systemHealthy = ActorSystem.Create("ArteryBackpressureOrdinaryHealthy", ArteryConfig());
            try
            {
                systemHealthy.ActorOf(Props.Create(() => new Echo()), "echo");

                // A RemoteActorRef pointing at a host:port nobody is listening on -- constructed
                // purely locally (no network round trip), same idiom as RemoteDeathWatchSpec's
                // synthetic unreachable-ref tests.
                var deadTarget = RARP.For(systemA).Provider.ResolveActorRef(
                    $"akka://dead-sys@127.0.0.1:{deadPort}/user/target");

                var deadLetterProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(deadLetterProbe.Ref, typeof(DeadLetter));

                // Deliberately well past capacity: unambiguous overflow even accounting for the
                // handful of elements that may be pulled out of the channel toward the (doomed)
                // TCP connect attempt before it gives up.
                const int floodCount = capacity + 1000;
                for (var i = 0; i < floodCount; i++)
                    deadTarget.Tell($"flood-{i}", ActorRefs.NoSender);

                // BOUNDED STATE: the association's occupied queue size must never exceed its own
                // capacity. Sampled repeatedly -- no elapsed-time arithmetic, just "is this value
                // ever over the bound".
                var association = ((ArteryRemoting)RARP.For(systemA).Provider.Transport).Registry.AssociationFor(deadAddress);
                for (var i = 0; i < 25; i++)
                {
                    association.OutboundQueueCount.Should().BeLessOrEqualTo(capacity,
                        "the bounded channel must never hold more than its configured capacity, no matter how fast the producer floods it");
                    await Task.Yield();
                }

                // PROGRESS/COMPLETION (liveness collection, not a timing measurement): the overflow
                // must show up as dead letters. Collected via the idle-timeout completion signal
                // rather than an exact count -- precisely how many of the flood got pulled toward
                // the doomed connection attempt before it failed is not deterministic, only that
                // AT LEAST the queue-bound-respecting majority of the flood does.
                var deadLetters = await deadLetterProbe
                    .ReceiveWhileAsync<DeadLetter>(_ => true, max: TimeSpan.FromSeconds(20), idle: TimeSpan.FromSeconds(3), msgs: floodCount)
                    .ToListAsync();

                deadLetters.Count.Should().BeGreaterOrEqualTo(floodCount - capacity - 10,
                    "essentially the whole overflow past capacity must land in dead letters, allowing a small " +
                    "slack for the few elements that may have been mid-flight toward the doomed connection " +
                    "attempt when it failed");

                // NO WEDGE: a completely saturated/dead association must not affect A's ability to
                // talk to a DIFFERENT, healthy association.
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

        [Fact(DisplayName = "Control channel: flooding system messages against an association whose handshake is complete but whose peer is unresponsive quarantines EXACTLY ONCE (re-entrancy guard holds even though the quarantine notices themselves also overflow the same full channel), dead-letters the overflow, and does not wedge sends to a different, healthy association")]
        public async Task Should_Quarantine_Exactly_Once_On_Control_Overflow_Against_Unresponsive_Peer_Without_Wedging_Other_Associations()
        {
            const int controlCapacity = Association.DefaultControlQueueCapacity;
            // Port 0 is not an assignable listener port, so it is a permanently-unreachable peer --
            // deterministic connection failure, no reservation, no reserve-then-release race.
            const int deadPort = 0;
            var deadAddress = new Address("akka", "dead-sys", "127.0.0.1", deadPort);
            const long fakePeerUid = 123_456_789L;

            var systemA = ActorSystem.Create("ArteryBackpressureControlA", ArteryConfig());
            var systemHealthy = ActorSystem.Create("ArteryBackpressureControlHealthy", ArteryConfig());
            try
            {
                systemHealthy.ActorOf(Props.Create(() => new Echo()), "echo");

                var transportA = (ArteryRemoting)RARP.For(systemA).Provider.Transport;

                // Fake-complete the handshake against the dead address using the SAME production
                // method (AssociationRegistry.CompleteHandshake) the real InboundHandshakeStage
                // calls after a genuine HandshakeRsp arrives -- just without a live peer on the
                // other end (OutboundHandshakeStage polls this SAME registry's AssociationState,
                // so it observes "complete" exactly as it would for a real handshake). This is
                // what lets the overflow policy reach QUARANTINE (design.md Decision 7):
                // HandleControlOverflow only quarantines once a peer uid is known.
                transportA.Registry.CompleteHandshake(deadAddress, new UniqueAddress(deadAddress, fakePeerUid));

                var quarantineProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(quarantineProbe.Ref, typeof(QuarantinedEvent));

                var deadLetterProbe = CreateTestProbe(systemA);
                systemA.EventStream.Subscribe(deadLetterProbe.Ref, typeof(DeadLetter));

                // Flood real reliable system messages (Watch) -- these travel the CONTROL channel
                // (design.md invariant 5: system messages are never hashed onto ordinary lanes),
                // the SAME bounded channel handshake/heartbeat/quarantine-notice traffic uses.
                //
                // IMPORTANT: each PlainWatcher must watch a DISTINCT remote path. RemoteActorRef.
                // SendSystemMessage intercepts Watch/Unwatch and hands them to the LOCAL
                // RemoteWatcher actor (RemoteActorRef.IsWatchIntercepted), which dedupes multiple
                // local watchers of the SAME remote actor into a single underlying wire-level Watch
                // (RemoteWatcher.AddWatching calls Context.Watch(watchee) from ITS OWN cell, which
                // is a no-op for a watchee it already watches) -- so N instances watching the SAME
                // target would produce just ONE real system message, not N. Distinct watchees defeat
                // the dedup and reliably flood N distinct Watch system messages onto the channel.
                const int floodCount = controlCapacity + 400;
                for (var i = 0; i < floodCount; i++)
                {
                    var distinctTarget = RARP.For(systemA).Provider.ResolveActorRef(
                        $"akka://dead-sys@127.0.0.1:{deadPort}/user/target-{i}");
                    systemA.ActorOf(Props.Create(() => new PlainWatcher(distinctTarget)));
                }

                // PROGRESS/COMPLETION: exactly one QuarantinedEvent for this uid, despite
                // HandleControlOverflow being re-entered for EVERY overflowing message -- including
                // the ArteryQuarantined + ClearSystemMessageDelivery notices Quarantine() itself
                // tries to send right back over the SAME, still-full channel. The re-entrancy guard
                // (association.IsQuarantined check inside HandleControlOverflow) must hold.
                var quarantined = await quarantineProbe.ExpectMsgAsync<QuarantinedEvent>(TimeSpan.FromSeconds(20));
                quarantined.Address.Should().Be(deadAddress);
                quarantined.Uid.Should().Be(fakePeerUid);

                await quarantineProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

                // BOUNDED STATE: the control channel's occupied size stays at (or below) its own
                // capacity even after this whole flood.
                var association = transportA.Registry.AssociationFor(deadAddress);
                association.ControlQueueCount.Should().BeLessOrEqualTo(controlCapacity);

                // The overflow (both the flooded Watch system messages AND the re-entrant
                // quarantine notices themselves) must show up as dead letters.
                var deadLetters = await deadLetterProbe
                    .ReceiveWhileAsync<DeadLetter>(_ => true, max: TimeSpan.FromSeconds(20), idle: TimeSpan.FromSeconds(3), msgs: floodCount + 4)
                    .ToListAsync();
                deadLetters.Should().NotBeEmpty("a control channel this saturated must produce dead letters for the overflow");

                // NO WEDGE: a quarantined/dead association must not affect A's ability to talk to a
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
