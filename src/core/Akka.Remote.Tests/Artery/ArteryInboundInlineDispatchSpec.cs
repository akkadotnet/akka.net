//-----------------------------------------------------------------------
// <copyright file="ArteryInboundInlineDispatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AssociationRegistry = Akka.Remote.Artery.AssociationRegistry;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// With one inbound lane, an Ordinary connection's processing stage checks and dispatches ordinary
    /// frames itself, on its own thread, applying what the handshake, quarantine and test stages
    /// applied to them; control frames still go through those stages, in order with the frames
    /// around them.
    /// </summary>
    public class ArteryInboundInlineDispatchSpec : AkkaSpec
    {
        public ArteryInboundInlineDispatchSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static readonly UniqueAddress Local = new(new Address("akka", "local-sys", "local-host", 2551), 111L);
        private static readonly UniqueAddress Peer = new(new Address("akka", "peer", "remote-host", 2552), 222L);
        private static readonly UniqueAddress Quarantined = new(new Address("akka", "quarantined", "remote-host", 2553), 333L);
        private static readonly UniqueAddress Stranger = new(new Address("akka", "stranger", "remote-host", 2554), 444L);

        private static byte[] Preamble(ArteryStreamId streamId) => new byte[] { (byte)'A', (byte)'K', (byte)'K', (byte)'A', (byte)streamId };

        private byte[] Frame(long originUid, object message, string? recipient = "akka://local-sys@local-host:2551/user/target")
        {
            using var encoded = ArteryEnvelopeCodec.Encode(Sys.Serialization, originUid, null, recipient, message);
            return encoded.WrittenSpan.ToArray();
        }

        private static string Describe(object message) => message as string ?? message.GetType().Name;

        /// <summary>
        /// Runs one read through the processing stage and the handshake stage, as an accepted
        /// connection would. The log holds, in order, what was dispatched inline ("d:") and what
        /// came out of the stages ("s:").
        /// </summary>
        private async Task<(List<string> Log, List<(Address To, object Message)> SentControl)> Run(
            ArteryStreamId streamId, byte[] frames, SharedTestState? testState = null)
        {
            var sentControl = new List<(Address To, object Message)>();
            var registry = new AssociationRegistry();
            registry.CompleteHandshake(Peer.Address, Peer);
            registry.CompleteHandshake(Quarantined.Address, Quarantined);
            registry.AssociationFor(Quarantined.Address).Quarantine(Quarantined.Uid);
            var context = new AssociationRegistryInboundContext(registry, Local, (to, msg) => sentControl.Add((to, msg)));
            var log = new List<string>();
            var stage = new ArteryInboundProcessingStage(65536, 65536, Sys.Serialization, Sys.Scheduler,
                inboundLanes: 1, inboundContext: context, dispatchOrdinary: (message, _, _) => log.Add("d:" + Describe(message)),
                testState: testState);

            await Source.Single(new ReadOnlySequence<byte>(Preamble(streamId).Concat(frames).ToArray()))
                .Via(stage)
                .Via(new InboundHandshakeStage(context))
                .RunWith(Sink.ForEach<IInboundEnvelope>(envelope => log.Add("s:" + Describe(envelope.Message))), Sys.Materializer())
                .WaitAsync(TimeSpan.FromSeconds(5));
            return (log, sentControl);
        }

        [Fact(DisplayName = "One inbound lane: ordinary frames are checked and dispatched in order by the processing stage")]
        public async Task Should_CheckAndDispatchInline_When_OneInboundLane()
        {
            var frames = Frame(Peer.Uid, "p1")
                .Concat(Frame(Stranger.Uid, "s1"))
                .Concat(Frame(Quarantined.Uid, "q1"))
                .Concat(Frame(Peer.Uid, "p2")).ToArray();

            var (log, sentControl) = await Run(ArteryStreamId.Ordinary, frames);

            log.Should().Equal("d:p1", "d:p2");
            sentControl.Should().ContainSingle();
            sentControl[0].To.Should().Be(Quarantined.Address);
            sentControl[0].Message.Should().Be(new ArteryQuarantined(Local, Quarantined.Uid));
        }

        [Fact(DisplayName = "One inbound lane: a control frame passes the stages between the ordinary frames around it")]
        public async Task Should_KeepOrder_When_ControlFrameIsMidRead()
        {
            var frames = Frame(Peer.Uid, "p1")
                .Concat(Frame(Peer.Uid, new ArteryHeartbeat(), recipient: null))
                .Concat(Frame(Peer.Uid, "p2")).Concat(Frame(Peer.Uid, "p3")).ToArray();

            var (log, _) = await Run(ArteryStreamId.Ordinary, frames);

            log.Should().Equal("d:p1", "s:" + nameof(ArteryHeartbeat), "d:p2", "d:p3");
        }

        [Fact(DisplayName = "One inbound lane: frames after a handshake frame in the same read see the origin it registers")]
        public async Task Should_SeeRegisteredOrigin_When_HandshakeFrameIsMidRead()
        {
            var frames = Frame(Stranger.Uid, "x1")
                .Concat(Frame(Stranger.Uid, new HandshakeReq(Stranger, Local.Address), recipient: null))
                .Concat(Frame(Stranger.Uid, "x2")).Concat(Frame(Stranger.Uid, "x3")).ToArray();

            var (log, sentControl) = await Run(ArteryStreamId.Ordinary, frames);

            log.Should().Equal("d:x2", "d:x3");
            sentControl.Select(c => c.Message).Should().Equal(new HandshakeRsp(Local));
        }

        [Fact(DisplayName = "One inbound lane: a blackholed origin is dropped before the quarantine check, as by the test stage")]
        public async Task Should_DropBlackholedOrigin_When_TestModeIsOn()
        {
            var testState = new SharedTestState();
            testState.Blackhole(Local.Address, Quarantined.Address, ThrottleTransportAdapter.Direction.Both);
            var frames = Frame(Peer.Uid, "p1").Concat(Frame(Quarantined.Uid, "q1")).Concat(Frame(Peer.Uid, "p2")).ToArray();

            var (log, sentControl) = await Run(ArteryStreamId.Ordinary, frames, testState);

            log.Should().Equal("d:p1", "d:p2");
            sentControl.Should().BeEmpty("the staged path drops a blackholed origin in the test stage, before any quarantine notice");
        }

        [Fact(DisplayName = "One inbound lane: a control connection still sends every frame through the stages")]
        public async Task Should_LeaveFramesStaged_When_ConnectionIsControl()
        {
            var frames = Frame(Peer.Uid, "p1").Concat(Frame(Peer.Uid, new ArteryHeartbeat(), recipient: null)).ToArray();

            var (log, _) = await Run(ArteryStreamId.Control, frames);

            log.Should().Equal("s:p1", "s:" + nameof(ArteryHeartbeat));
        }
    }
}
