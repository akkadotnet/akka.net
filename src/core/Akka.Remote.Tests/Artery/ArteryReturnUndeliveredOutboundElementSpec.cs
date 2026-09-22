//-----------------------------------------------------------------------
// <copyright file="ArteryReturnUndeliveredOutboundElementSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
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
    /// P2 follow-up: <c>ArteryHandshakeSpec</c>'s "return pending element to channel on PostStop"
    /// test wires <see cref="OutboundHandshakeStage"/>'s <c>returnUndelivered</c> delegate BY HAND
    /// (<c>envelope =&gt; association.TryEnqueueControl(envelope)</c>) rather than going through the
    /// actual production delegate <see cref="ArteryRemoting.MaterializeOutboundStream"/> /
    /// <c>MaterializeOrdinaryOutboundWithLanes</c> wire in --
    /// <see cref="ArteryRemoting"/>'s private <c>ReturnUndeliveredOutboundElement</c>. That leaves
    /// the stream-id switch, the per-lane routing (the <c>lane</c> argument), and the full-channel
    /// <see cref="Dropped"/> path all untested by anything.
    ///
    /// <para>
    /// This spec reaches the private production method directly via reflection -- the same
    /// technique <see cref="ArteryOutboundRestartBackoffSpec"/> uses for
    /// <c>ScheduleOutboundRestart</c> -- on a live (but otherwise idle) <see cref="ArteryRemoting"/>
    /// instance, for the ORDINARY stream: the case with a real <c>lane</c> argument to get wrong.
    /// </para>
    /// </summary>
    public class ArteryReturnUndeliveredOutboundElementSpec : AkkaSpec
    {
        // outbound-lanes = 2 so a lane argument other than the 0 default is reachable and its
        // routing is actually exercised; outbound-message-queue-size = 1 so a single filler send
        // is enough to reproduce the full-channel Dropped path deterministically, with no flooding
        // loop needed.
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-lanes = 2
            akka.remote.artery.advanced.outbound-message-queue-size = 1
            """);

        public ArteryReturnUndeliveredOutboundElementSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        private static Address DeadPeerAddress() => new("akka", "peer-does-not-exist", "127.0.0.1", 1);

        private static MethodInfo ReturnUndeliveredOutboundElementMethod { get; } =
            typeof(ArteryRemoting).GetMethod("ReturnUndeliveredOutboundElement", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ArteryRemoting.ReturnUndeliveredOutboundElement not found -- has it been renamed?");

        [Fact(DisplayName = "P2: the production ReturnUndelivered wiring must return an ORDINARY-lane element to that SAME lane's channel, not lane 0")]
        public void ReturnUndeliveredOutboundElement_should_requeue_to_the_correct_ordinary_lane()
        {
            var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
            var remoteAddress = DeadPeerAddress();
            var association = transport.Registry.AssociationFor(remoteAddress);

            var held = new OutboundEnvelope("held-ordinary-payload", null, null);

            // Lane 1, deliberately not the lane-0 default -- proves the `lane` argument is
            // actually threaded through to the right channel, not hardcoded/defaulted away.
            ReturnUndeliveredOutboundElementMethod.Invoke(
                transport, new object[] { remoteAddress, association, ArteryStreamId.Ordinary, held, 1 });

            association.LaneReader(1).TryRead(out var returned).Should().BeTrue(
                "the held element must be returned to lane 1's own channel -- the lane it was materialized for");
            returned!.Message.Should().Be("held-ordinary-payload");

            association.LaneReader(0).TryRead(out _).Should().BeFalse(
                "the held element must NOT land on lane 0 -- a lane argument that is ignored or defaulted would put it there instead");
        }

        [Fact(DisplayName = "P2: the production ReturnUndelivered wiring must publish Dropped, not discard silently, when the association's channel has no room")]
        public async Task ReturnUndeliveredOutboundElement_should_publish_dropped_when_channel_is_full()
        {
            var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
            var remoteAddress = DeadPeerAddress();
            var association = transport.Registry.AssociationFor(remoteAddress);

            // outbound-message-queue-size = 1 above -- this single TryEnqueueOutbound call already
            // fills lane 0 to capacity, so the return-undelivered attempt below has no room.
            association.TryEnqueueOutbound(new OutboundEnvelope("filler", null, null), 0).Should().BeTrue();

            Sys.EventStream.Subscribe(TestActor, typeof(Dropped));

            var held = new OutboundEnvelope("held-with-no-room", null, null);
            ReturnUndeliveredOutboundElementMethod.Invoke(
                transport, new object[] { remoteAddress, association, ArteryStreamId.Ordinary, held, 0 });

            var dropped = await ExpectMsgAsync<Dropped>(TimeSpan.FromSeconds(3));
            dropped.Message.Should().Be("held-with-no-room");
            dropped.Recipient.Should().Be(Sys.DeadLetters);

            // The filler is still there, untouched -- this method must never evict an existing
            // queued element to make room for the one it is trying to return.
            association.LaneReader(0).TryRead(out var stillQueued).Should().BeTrue();
            stillQueued!.Message.Should().Be("filler");
        }
    }
}
