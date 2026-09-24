//-----------------------------------------------------------------------
// <copyright file="ArteryShutdownFlushSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers Artery's shutdown flush (<c>akka.remote.artery.advanced.flush-wait-on-shutdown</c>).
    /// Before it existed, <c>ArteryRemoting.Shutdown()</c> completed every association's outbound
    /// channels and drained them to <see cref="Dropped"/> in the same breath, so a message accepted
    /// microseconds before shutdown never reached the socket -- the last things a system sends
    /// (acks, graceful notices, handshake replies) were exactly the ones lost. Shutdown now waits,
    /// within a bound, for the outbound streams to finish writing what they had already accepted
    /// AND for those sockets to actually finish closing, and drains only what is left over.
    ///
    /// <para>
    /// Four properties, one test each: the flush really writes; an idle peer never costs anything
    /// it does not need to; the bound really bounds; and whatever the bound cuts short is still
    /// accounted as <see cref="Dropped"/>. Every assertion is on arrival, order, or completion --
    /// except the idle-peer test, which exists specifically to measure elapsed time (see its own
    /// remarks for why that is the only way to prove a negative here). The awaits that carry a
    /// <see cref="TimeSpan"/> are liveness bounds, so a genuine hang fails the test instead of
    /// wedging the suite.
    /// </para>
    /// </summary>
    public class ArteryShutdownFlushSpec : AkkaSpec
    {
        public ArteryShutdownFlushSpec(ITestOutputHelper output) : base(ArteryConfig, output)
        {
        }

        /// <summary>
        /// Artery on an ephemeral port with test-mode available (the blackhole tests below need it;
        /// test-mode off composes an identical pipeline, so sharing one config across all four
        /// tests changes nothing for the first). <c>flush-wait-on-shutdown</c> is raised above its
        /// 2s default so a slow CI agent cannot turn the "flush really writes" test into a timeout
        /// test -- no assertion there depends on the value itself, only on which side of it the
        /// streams land. The idle-peer test DOES depend on the value (it asserts a fraction of it),
        /// so it is called out explicitly in that test's own remarks.
        /// </summary>
        private static readonly Config ArteryConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.test-mode = on
            akka.remote.artery.advanced.flush-wait-on-shutdown = 5s
            akka.loglevel = INFO
            """);

        private static ArteryRemoting TransportOf(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        /// <summary>Forwards everything it receives to <paramref name="target"/>.</summary>
        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target)
            {
                ReceiveAny(msg => target.Forward(msg));
            }
        }

        /// <summary>
        /// Records every <see cref="Dropped"/> its system publishes. Used instead of a
        /// <see cref="TestProbe"/> whenever the system under test is not the spec's own
        /// <see cref="AkkaSpec.Sys"/>: a probe lives in the spec's system, and telling it from
        /// another system would route the assertion through the very transport being shut down.
        /// </summary>
        private sealed class DroppedRecorder : ReceiveActor
        {
            public DroppedRecorder(ConcurrentQueue<Dropped> recorded)
            {
                Receive<Dropped>(dropped => recorded.Enqueue(dropped));
            }
        }

        /// <summary>
        /// Unwraps the payload an Artery outbound envelope carried. A remote
        /// <see cref="ActorSelection"/> send travels as an <see cref="ActorSelectionMessage"/>, so
        /// that is what a <see cref="Dropped"/> from the outbound queue reports; the marker the
        /// test sent is one level in.
        /// </summary>
        private static object PayloadOf(object message) =>
            message is ActorSelectionMessage selection ? selection.Message : message;

        /// <summary>
        /// Sends one-way markers until one lands at this spec's test actor, establishing the
        /// association (connection plus handshake) before the burst under test. One-way on purpose:
        /// the sender is a different <see cref="ActorSystem"/>, so handing it a probe from THIS
        /// system as the reply-to would serialize a path the peer cannot answer.
        /// </summary>
        private async Task AwaitAssociationAsync(ActorSelection selection)
        {
            var attempt = 0;
            await AwaitAssertAsync(async () =>
            {
                var marker = $"warmup-{++attempt}";
                selection.Tell(marker, ActorRefs.NoSender);
                await FishForMessageAsync(msg => Equals(msg, marker), TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        }

        [Fact(DisplayName = "Should_DeliverQueuedMessages_When_ShutdownFlushesToALivePeer")]
        public async Task Should_DeliverQueuedMessages_When_ShutdownFlushesToALivePeer()
        {
            // Sys is the RECEIVER here and outlives the transport under test, so it can witness what
            // the flush wrote. The sender is the system whose transport gets shut down.
            var senderSys = ActorSystem.Create("artery-flush-sender", ArteryConfig);
            // Without this, Shutdown()'s own INFO lines (including the "did not finish writing
            // within" timeout branch and the final "shut down" line) never reach the test's
            // output, which is exactly the diagnostic a flake needs -- senderSys is a SEPARATE
            // system from Sys, so it is not covered by the spec's own base-class logger.
            InitializeLogger(senderSys, "sender");
            try
            {
                Sys.ActorOf(Props.Create(() => new Forwarder(TestActor)), "flush-receiver");
                var selection = senderSys.ActorSelection(
                    $"akka://{Sys.Name}@127.0.0.1:{AddressOf(Sys).Port}/user/flush-receiver");

                // Establish the association FIRST. Shutdown refuses to materialize new streams, so a
                // burst sent to a never-contacted peer would have nothing to flush through -- and
                // that is not the scenario this fix is about: the reported failure was a node that
                // had been talking to its peer all along, replied, and terminated right after.
                await AwaitAssociationAsync(selection);

                var dropped = new ConcurrentQueue<Dropped>();
                var recorder = senderSys.ActorOf(Props.Create(() => new DroppedRecorder(dropped)), "dropped-recorder");
                senderSys.EventStream.Subscribe(recorder, typeof(Dropped));

                // A burst big enough that the outbound queue is still holding most of it when
                // Shutdown lands one statement later. That is the whole point: these messages were
                // ACCEPTED, and before the flush existed they went straight to Dropped.
                const int messageCount = 500;
                var markers = Enumerable.Range(0, messageCount).Select(i => $"flushed-{i}").ToArray();
                foreach (var marker in markers)
                    selection.Tell(marker, ActorRefs.NoSender);

                (await TransportOf(senderSys).Shutdown().AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                    .Should().BeTrue("the shutdown flush is bounded, so Shutdown must return");

                // Property 1: every accepted message reached the peer, all within ONE overall
                // bound -- not a fresh 30s allowance PER MESSAGE. A warmup marker from an earlier
                // attempt can still be in flight, so collect by marker rather than by count.
                var received = new HashSet<string>();
                var receiveBudget = TimeSpan.FromSeconds(30);
                var receiveStopwatch = Stopwatch.StartNew();
                while (received.Count < messageCount)
                {
                    var remaining = receiveBudget - receiveStopwatch.Elapsed;
                    var message = await ExpectMsgAsync<string>(remaining);
                    if (message.StartsWith("flushed-", StringComparison.Ordinal))
                        received.Add(message);
                }

                received.Should().BeEquivalentTo(markers);

                // Property 2: and none of them was written off as Dropped along the way. This is
                // what separates a real flush from a lucky race -- the drain runs on every shutdown,
                // so a message that was NOT flushed leaves a Dropped behind.
                dropped.Select(d => PayloadOf(d.Message)).Should().NotIntersectWith(markers);
            }
            finally
            {
                await senderSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(30));
            }
        }

        [Fact(DisplayName = "Should_ReturnWellUnderTheBound_When_ThePeerIsIdle")]
        public async Task Should_ReturnWellUnderTheBound_When_ThePeerIsIdle()
        {
            // Sys gets a live, healthy association (an OUTBOUND connection to peerSys and an
            // INBOUND one accepting its handshake/control traffic), then goes idle, then shuts
            // down. Both close promptly: Unbind() stops the listener, which stops its
            // connection-actor children, and each inbound stage watches its own connection actor.
            var peerSys = ActorSystem.Create("artery-flush-idle-peer", ArteryConfig);
            try
            {
                peerSys.ActorOf(Props.Create(() => new Forwarder(TestActor)), "idle-peer-receiver");
                var selection = Sys.ActorSelection(
                    $"akka://{peerSys.Name}@127.0.0.1:{AddressOf(peerSys).Port}/user/idle-peer-receiver");

                await AwaitAssociationAsync(selection);

                // The real assertion: the "still running ... force-stopping" INFO line only fires
                // when WaitForStreamsToStopAsync actually hits its bound, so its absence is the
                // regression guard. The elapsed-time check backs it up -- timed INSIDE the filter's
                // action so ExpectAsync's own confirm-absence wait doesn't count against it.
                var elapsed = TimeSpan.Zero;
                await EventFilter.Info(start: "Artery streams were still running").ExpectAsync(0, async () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    (await TransportOf(Sys).Shutdown().AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                        .Should().BeTrue("the shutdown flush is bounded, so Shutdown must return");
                    elapsed = stopwatch.Elapsed;
                });
                elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
            }
            finally
            {
                await peerSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(30));
            }
        }

        [Fact(DisplayName = "Should_CompleteWithinItsBound_When_TheFlushCannotFinish")]
        public async Task Should_CompleteWithinItsBound_When_TheFlushCannotFinish()
        {
            await WithStalledPeerAsync(async _ =>
            {
                // The INFO the timeout branch logs is the deterministic proof that the bound -- not
                // the streams -- ended the flush. Asserting the log rather than an elapsed time
                // keeps this a completion test, not a stopwatch test.
                await EventFilter
                    .Info(start: "Artery outbound streams did not finish writing within")
                    .ExpectOneAsync(async () =>
                    {
                        // Liveness: shutdown returns even though the flush never can. A regression
                        // that waits unbounded hangs here instead of quietly stalling every
                        // shutdown behind a stuck peer.
                        (await TransportOf(Sys).Shutdown().AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                            .Should().BeTrue("the shutdown flush is bounded, so Shutdown must return");
                    });
            });
        }

        [Fact(DisplayName = "Should_PublishDropped_When_TheFlushBoundExpiresWithMessagesStillQueued")]
        public async Task Should_PublishDropped_When_TheFlushBoundExpiresWithMessagesStillQueued()
        {
            await WithStalledPeerAsync(async markers =>
            {
                Sys.EventStream.Subscribe(TestActor, typeof(Dropped));

                (await TransportOf(Sys).Shutdown().AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                    .Should().BeTrue("the shutdown flush is bounded, so Shutdown must return");

                // Everything the flush could not get out is accounted, one Dropped per message --
                // the guarantee the bound must not weaken.
                //
                // markers[0] is deliberately excluded: OutboundHandshakeStage holds exactly one
                // element while it waits for a handshake that never completes ("never drops; never
                // pull further while one is held"), so the head of the queue may already have left
                // the channel. Everything behind it cannot have.
                var expected = new HashSet<string>(markers.Skip(1));
                while (expected.Count > 0)
                {
                    var dropped = await ExpectMsgAsync<Dropped>(TimeSpan.FromSeconds(30));
                    if (PayloadOf(dropped.Message) is string marker)
                        expected.Remove(marker);
                }
            });
        }

        /// <summary>
        /// Runs <paramref name="body"/> against a peer that accepts this system's connection but
        /// never completes its handshake, which is what leaves the outbound stream unable to write
        /// and the queue unable to drain -- the exact shape a flush cannot finish.
        ///
        /// <para>
        /// The stall is built out of test-mode failure injection rather than sockets: the peer
        /// blackholes this system in BOTH directions before a single message is sent. The peer's
        /// inbound side lets a pre-handshake <c>HandshakeReq</c> through (it cannot tell yet who
        /// sent it), but the peer's outbound side drops the reply, so the sender's
        /// <c>OutboundHandshakeStage</c> stays in its request-in-progress state, holds one element,
        /// and stops pulling. The TCP connection itself stays healthy the whole time, so nothing
        /// faults and nothing completes -- which is what makes this a bound test rather than a
        /// failure test.
        /// </para>
        /// </summary>
        private async Task WithStalledPeerAsync(Func<string[], Task> body)
        {
            var peerSys = ActorSystem.Create("artery-flush-stalled-peer", ArteryConfig);
            try
            {
                (await TransportOf(peerSys).ManagementCommand(
                        new SetThrottle(AddressOf(Sys), ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                    .Should().BeTrue();

                var selection = Sys.ActorSelection(
                    $"akka://{peerSys.Name}@127.0.0.1:{AddressOf(peerSys).Port}/user/never-reached");

                var markers = Enumerable.Range(0, 8).Select(i => $"stalled-{i}").ToArray();
                foreach (var marker in markers)
                    selection.Tell(marker, ActorRefs.NoSender);

                await body(markers);
            }
            finally
            {
                await peerSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(30));
            }
        }
    }
}
