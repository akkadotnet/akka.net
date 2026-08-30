//-----------------------------------------------------------------------
// <copyright file="SystemMessageAckerStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
// Akka.Remote.SysAck/Akka.Remote.SysNack (classic AckedDelivery.cs) are enclosing-namespace types of
// Akka.Remote.Tests.Artery and would otherwise shadow Akka.Remote.Artery.SysAck/SysNack (design.md gate
// G3) in unqualified lookups -- alias to the Artery ones explicitly.
using SysAck = Akka.Remote.Artery.Ack;
using SysNack = Akka.Remote.Artery.Nack;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Stage-level tests for <see cref="SystemMessageAckerStage"/>, the INBOUND half of reliable
    /// system-message delivery (design.md gate G3). Uses <c>TestSource</c>/<c>TestSink</c> probes
    /// to drive/observe the stage directly (mirrors <c>ArteryHandshakeSpec</c>'s patterns) -- no
    /// TCP, no real association transport.
    /// </summary>
    public class SystemMessageAckerStageSpec : AkkaSpec
    {
        public SystemMessageAckerStageSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static UniqueAddress NewLocal() => new(new Address("akka", "local-sys", "local-host", 2551), 111L);

        private static UniqueAddress NewRemotePeer(long uid = 222L) => new(new Address("akka", "remote-sys", "remote-host", 2552), uid);

        private sealed record TestSystemMessage(string Name) : ISystemMessage;

        private static IInboundEnvelope Envelope(SystemMessageEnvelope sme, long originUid) =>
            new InboundEnvelope(sme, null, null, originUid, SerializerId: 0, Manifest: "test-manifest");

        private static SystemMessageEnvelope Sme(long seqNo, UniqueAddress ackReplyTo, string name = "m", string recipientPath = "akka://local-sys@local-host:2551/user/target") =>
            new(new TestSystemMessage(name), seqNo, ackReplyTo, recipientPath);

        private (List<(Address To, object Message)> SentControl, TestPublisher.Probe<IInboundEnvelope> Pub, TestSubscriber.Probe<IInboundEnvelope> Sub) BuildHarness()
        {
            var sentControl = new List<(Address To, object Message)>();
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry, NewLocal(), (to, msg) => sentControl.Add((to, msg)));
            var stage = new SystemMessageAckerStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            return (sentControl, pub, sub);
        }

        [Fact(DisplayName = "happy-path: seq == expected(1) delivers the inner message (non-control, resolved recipient path) and replies SysAck(1)")]
        public async Task Should_Deliver_On_Expected_Seq_And_Ack()
        {
            var (sentControl, pub, sub) = BuildHarness();
            var peer = NewRemotePeer();

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(1L, peer, "m1", "akka://local-sys@local-host:2551/user/watcher"), peer.Uid));

            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
            delivered.IsControl.Should().BeFalse("a delivered system message re-emits as a plain (non-control) envelope so ordinary dispatch handles it");
            delivered.Message.Should().BeOfType<TestSystemMessage>();
            ((TestSystemMessage)delivered.Message).Name.Should().Be("m1");
            delivered.RecipientPath.Should().Be("akka://local-sys@local-host:2551/user/watcher");

            sentControl.Should().ContainSingle();
            sentControl[0].To.Should().Be(peer.Address);
            sentControl[0].Message.Should().BeOfType<SysAck>();
            ((SysAck)sentControl[0].Message).SeqNo.Should().Be(1L);
            ((SysAck)sentControl[0].Message).From.Should().Be(NewLocal());
        }

        [Fact(DisplayName = "happy-path: successive in-order envelopes advance `expected` and each get their own SysAck")]
        public async Task Should_Advance_Expected_And_Ack_Each_In_Order_Envelope()
        {
            var (sentControl, pub, sub) = BuildHarness();
            var peer = NewRemotePeer();

            await sub.RequestAsync(2);
            await pub.SendNextAsync(Envelope(Sme(1L, peer, "m1"), peer.Uid));
            await pub.SendNextAsync(Envelope(Sme(2L, peer, "m2"), peer.Uid));

            ((TestSystemMessage)(await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).Name.Should().Be("m1");
            ((TestSystemMessage)(await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).Name.Should().Be("m2");

            sentControl.Should().HaveCount(2);
            ((SysAck)sentControl[0].Message).SeqNo.Should().Be(1L);
            ((SysAck)sentControl[1].Message).SeqNo.Should().Be(2L);
        }

        [Fact(DisplayName = "duplicate: seq < expected is dropped (not delivered) and re-Acked at expected-1 (covers a lost SysAck)")]
        public async Task Should_Drop_Duplicate_And_ReAck()
        {
            var (sentControl, pub, sub) = BuildHarness();
            var peer = NewRemotePeer();

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(1L, peer, "m1"), peer.Uid));
            await sub.ExpectNextAsync(TimeSpan.FromSeconds(3)); // expected is now 2

            // Re-send seq 1 -- a duplicate (the sender never saw our first SysAck).
            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(1L, peer, "m1-dup"), peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300)); // a duplicate must never be delivered downstream

            sentControl.Should().HaveCount(2, "the duplicate must be re-Acked (not silently ignored) so the sender's resend timer can eventually observe it");
            sentControl[1].Message.Should().BeOfType<SysAck>();
            ((SysAck)sentControl[1].Message).SeqNo.Should().Be(1L, "re-SysAck must be at expected-1, i.e. the highest CONTIGUOUS seq actually delivered");
        }

        [Fact(DisplayName = "gap: seq > expected is dropped (not delivered) and NACK'd at expected-1, with NO inbound reorder buffer")]
        public async Task Should_Drop_Gap_And_Nack()
        {
            var (sentControl, pub, sub) = BuildHarness();
            var peer = NewRemotePeer();

            // Nothing delivered yet -- expected is still 1. Seq 3 arrives first: a gap.
            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(3L, peer, "m3"), peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300)); // a gap must never be delivered downstream (no inbound reorder buffer)

            sentControl.Should().ContainSingle();
            sentControl[0].Message.Should().BeOfType<SysNack>();
            ((SysNack)sentControl[0].Message).SeqNo.Should().Be(0L, "SysNack must carry expected-1, i.e. the highest contiguous seq actually delivered so far (none yet)");
        }

        [Fact(DisplayName = "reordering tolerated: out-of-order arrival (1, 3, 2, resent-3) is restored to in-order delivery purely by the SENDER's resend, with no inbound buffering of the gap")]
        public async Task Should_Tolerate_Reordering_Via_Sender_Resend_Without_Inbound_Buffer()
        {
            var (sentControl, pub, sub) = BuildHarness();
            var peer = NewRemotePeer();

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(1L, peer, "m1"), peer.Uid));
            ((TestSystemMessage)(await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).Name.Should().Be("m1"); // expected -> 2

            // Seq 3 arrives before seq 2 -- a gap (expected is 2). Dropped, NOT buffered, SysNack(1).
            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(3L, peer, "m3-early"), peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            // Seq 2 now arrives (this is what the SENDER's SysNack-triggered resend restores order
            // with) -- matches expected, delivered, expected -> 3.
            await pub.SendNextAsync(Envelope(Sme(2L, peer, "m2"), peer.Uid));
            ((TestSystemMessage)(await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).Name.Should().Be("m2");

            // The sender's resend (triggered by the SysNack) now redelivers seq 3 -- matches expected(3), delivered.
            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(Sme(3L, peer, "m3-resent"), peer.Uid));
            ((TestSystemMessage)(await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message).Name.Should().Be("m3-resent");

            // Exactly one SysNack was sent (for the early/out-of-order seq 3), alongside three Acks
            // (seq 1, seq 2, seq 3) -- proves the stage never buffered anything itself; ordering was
            // restored entirely by what the "sender" (the test) chose to redeliver.
            sentControl.Should().HaveCount(4);
            sentControl[0].Message.Should().BeOfType<SysAck>();
            sentControl[1].Message.Should().BeOfType<SysNack>();
            sentControl[2].Message.Should().BeOfType<SysAck>();
            sentControl[3].Message.Should().BeOfType<SysAck>();
        }

        [Fact(DisplayName = "pass-through: ordinary messages and unrelated control messages (e.g. ArteryHeartbeat) flow through unchanged")]
        public async Task Should_Pass_Through_Unrelated_Messages_Unchanged()
        {
            var (_, pub, sub) = BuildHarness();

            var ordinary = new InboundEnvelope("hello", null, "akka://local-sys@local-host:2551/user/x", 1L, 0, "test-manifest");
            var heartbeat = new InboundEnvelope(new ArteryHeartbeat(), null, null, 1L, 0, ArteryControlMessageSerializer.HeartbeatManifest);

            await sub.RequestAsync(2);
            await pub.SendNextAsync(ordinary);
            await pub.SendNextAsync(heartbeat);

            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().Be(ordinary);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Should().Be(heartbeat);
        }
    }
}
