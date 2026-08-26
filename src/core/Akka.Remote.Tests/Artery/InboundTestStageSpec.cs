//-----------------------------------------------------------------------
// <copyright file="InboundTestStageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using AssociationRegistry = Akka.Remote.Artery.AssociationRegistry;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Stage-level tests for <see cref="InboundTestStage"/> (artery <c>advanced.test-mode</c>
    /// failure injection), including the load-bearing pre-handshake special case: an
    /// unknown-origin <see cref="HandshakeReq"/> must pass while any blackhole is present (or
    /// legitimate new associations would wedge), while every OTHER unknown-origin envelope --
    /// including a <see cref="HandshakeRsp"/> -- is dropped. Drives the stage directly with
    /// probes; no TCP.
    /// </summary>
    public class InboundTestStageSpec : AkkaSpec
    {
        public InboundTestStageSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static readonly UniqueAddress Local = new(new Address("akka", "local-sys", "10.0.0.1", 2551), 111L);
        private static readonly UniqueAddress Peer = new(new Address("akka", "remote-sys", "10.0.0.2", 2552), 222L);

        private static IInboundEnvelope Envelope(object message, long originUid) =>
            new InboundEnvelope(message, null, "akka://local-sys@10.0.0.1:2551/user/target", originUid, SerializerId: 0, Manifest: "test-manifest");

        private (SharedTestState State, AssociationRegistryInboundContext Context, TestPublisher.Probe<IInboundEnvelope> Pub, TestSubscriber.Probe<IInboundEnvelope> Sub) BuildHarness()
        {
            var state = new SharedTestState();
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry, Local, static (_, _) => { });
            var stage = new InboundTestStage(context, state);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            return (state, context, pub, sub);
        }

        [Fact(DisplayName = "known origin, no blackhole: envelopes pass through")]
        public async Task Should_Pass_Known_Origin_Without_Blackhole()
        {
            var (_, context, pub, sub) = BuildHarness();
            context.CompleteHandshake(Peer); // registers Peer.Uid -> Peer.Address in the shared registry

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope("m1", Peer.Uid));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("m1");
        }

        [Fact(DisplayName = "known origin, blackholed: envelopes are dropped; PassThrough heals")]
        public async Task Should_Drop_Known_Blackholed_Origin_And_Recover()
        {
            var (state, context, pub, sub) = BuildHarness();
            context.CompleteHandshake(Peer);

            // The inbound stage checks the SAME (localAddress, originAddress) key order as the
            // outbound stage -- a Send-direction entry at this node cuts inbound from the peer too
            // (this single-key-order discipline is what lets one node's command sever the link).
            state.Blackhole(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Send);

            await sub.RequestAsync(2);
            await pub.SendNextAsync(Envelope("dropped", Peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            state.PassThrough(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Send);
            await pub.SendNextAsync(Envelope("after-heal", Peer.Uid));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("after-heal");
        }

        [Fact(DisplayName = "unknown origin, no blackhole ever present: envelopes pass through")]
        public async Task Should_Pass_Unknown_Origin_Without_Blackhole()
        {
            var (_, _, pub, sub) = BuildHarness();

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope("m1", originUid: 999L));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("m1");
        }

        [Fact(DisplayName = "unknown origin + any blackhole present: HandshakeReq passes (pre-handshake special case)")]
        public async Task Should_Let_HandshakeReq_Through_While_Blackholed()
        {
            var (state, _, pub, sub) = BuildHarness();
            state.Blackhole(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Both);

            var req = new HandshakeReq(new UniqueAddress(new Address("akka", "new-sys", "10.0.0.9", 2559), 999L), Local.Address);

            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope(req, originUid: 999L));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().BeOfType<HandshakeReq>(
                "dropping an unknown origin's HandshakeReq would wedge every legitimate NEW association while any blackhole is active");
        }

        [Fact(DisplayName = "unknown origin + any blackhole present: every other envelope is dropped -- including a HandshakeRsp")]
        public async Task Should_Drop_Unknown_Origin_NonReq_While_Blackholed()
        {
            var (state, _, pub, sub) = BuildHarness();
            state.Blackhole(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Both);

            await sub.RequestAsync(3);
            await pub.SendNextAsync(Envelope("plain-user-message", originUid: 999L));
            await pub.SendNextAsync(Envelope(new HandshakeRsp(new UniqueAddress(new Address("akka", "new-sys", "10.0.0.9", 2559), 999L)), originUid: 999L));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            // A subsequent HandshakeReq still passes -- proving the two prior envelopes were
            // DROPPED by the unknown-origin branch, not stuck behind backpressure.
            var req = new HandshakeReq(new UniqueAddress(new Address("akka", "new-sys", "10.0.0.9", 2559), 999L), Local.Address);
            await pub.SendNextAsync(Envelope(req, originUid: 999L));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().BeOfType<HandshakeReq>();
        }

        [Fact(DisplayName = "unknown-origin gating LIFTS after a full heal: a fresh incarnation's envelopes pass again")]
        public async Task Should_Lift_Unknown_Origin_Gating_After_Heal()
        {
            var (state, _, pub, sub) = BuildHarness();
            state.Blackhole(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Both);
            state.PassThrough(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Both);

            // No blackhole is active anywhere on this transport any more, so a message from an
            // unknown origin (e.g. a fresh incarnation of a peer, a new uid the handshake hasn't
            // completed for yet) is no longer gated -- it must pass through untouched, exactly as
            // if no blackhole had ever been set.
            await sub.RequestAsync(1);
            await pub.SendNextAsync(Envelope("passes-after-heal", originUid: 999L));
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("passes-after-heal");

            // Known origins are unaffected either way.
            var (state2, context2, pub2, sub2) = BuildHarness();
            state2.Blackhole(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Both);
            state2.PassThrough(Local.Address, Peer.Address, ThrottleTransportAdapter.Direction.Both);
            context2.CompleteHandshake(Peer);
            await sub2.RequestAsync(1);
            await pub2.SendNextAsync(Envelope("known-origin-passes", Peer.Uid));
            (await sub2.ExpectNextAsync(TimeSpan.FromSeconds(3))).Message.Should().Be("known-origin-passes");
        }
    }
}
