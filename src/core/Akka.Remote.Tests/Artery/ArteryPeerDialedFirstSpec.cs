//-----------------------------------------------------------------------
// <copyright file="ArteryPeerDialedFirstSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Regression coverage for issue #8496: Artery dropped the FIRST ordinary message on a
    /// brand-new association whenever the peer dialed us first.
    ///
    /// <para>
    /// <b>The defect.</b> <see cref="OutboundHandshakeStage"/>'s <c>PreStart</c> shortcut treated
    /// "this association already has a <see cref="AssociationState.UniqueRemoteAddress"/>" as "our
    /// handshake is done". That field is also set by the INBOUND direction — when WE handle the
    /// peer's <see cref="HandshakeReq"/> — which proves we know the peer's uid but says nothing
    /// about whether the peer knows OURS. So our first outbound ordinary stream skipped its own
    /// <see cref="HandshakeReq"/>, and our first user message raced our <see cref="HandshakeRsp"/>
    /// (sent on a DIFFERENT TCP connection) into the peer's unknown-origin gate — where it was
    /// silently dropped, with no resend path for ordinary messages.
    /// </para>
    ///
    /// <para>
    /// Every test here drives the PRODUCTION path for both handshake directions (a real
    /// <see cref="InboundHandshakeStage"/> fed a real <see cref="HandshakeReq"/>/
    /// <see cref="HandshakeRsp"/>) rather than poking the registry directly, so the specs stay
    /// honest about which direction actually completed.
    /// </para>
    /// </summary>
    public class ArteryPeerDialedFirstSpec : AkkaSpec
    {
        public ArteryPeerDialedFirstSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static UniqueAddress NewLocal() => new(new Address("akka", "local-sys", "local-host", 2551), 111L);

        private static Address NewRemote() => new("akka", "remote-sys", "remote-host", 2552);

        private static IInboundEnvelope ControlInbound(IArteryControlMessage message, long originUid) =>
            new InboundEnvelope(message, null, null, originUid, SerializerId: 0, Manifest: "test-manifest");

        private static IInboundEnvelope OrdinaryInbound(object message, long originUid) =>
            new InboundEnvelope(message, null, "akka://remote-sys@remote-host:2552/user/recipient", originUid,
                SerializerId: 0, Manifest: "test-manifest");

        #region Stage-level

        [Fact(DisplayName = "OutboundHandshakeStage should send its OWN HandshakeReq when the association was created by an INBOUND handshake (peer dialed first), and hold user traffic until the peer answers")]
        public async Task OutboundHandshakeStage_should_not_shortcut_on_an_inbound_only_association()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var peer = new UniqueAddress(remoteAddress, 222L);

            var materializer = ActorMaterializer.Create(Sys);

            // The peer dialed US first: OUR InboundHandshakeStage handles ITS HandshakeReq, which
            // both CREATES the association keyed by the peer's address and sets
            // UniqueRemoteAddress. Nothing here proves the peer has registered OUR uid.
            var inboundSentControl = new List<(Address To, object Message)>();
            var inboundContext = new AssociationRegistryInboundContext(
                registry, localAddress, (to, msg) => inboundSentControl.Add((to, msg)));
            var (inPub, inSub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(new InboundHandshakeStage(inboundContext)), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await inSub.RequestAsync(1);
            await inPub.SendNextAsync(ControlInbound(new HandshakeReq(peer, localAddress.Address), peer.Uid));
            await AwaitConditionAsync(() => Task.FromResult(inboundSentControl.Count == 1), 3.Seconds());
            inboundSentControl[0].Message.Should().BeOfType<HandshakeRsp>("we answer the peer's Req, which is what put the association in this state");
            registry.AssociationFor(remoteAddress).CurrentState.UniqueRemoteAddress.Should().Be(peer);

            // Now WE materialize our own outbound ORDINARY stream to that peer for the first time
            // (forceReqOnStart: false -- this is a first materialization, not a reconnect).
            var sentControl = new List<object>();
            var outboundContext = new AssociationRegistryOutboundContext(
                registry, localAddress, remoteAddress, sentControl.Add);
            var stage = new OutboundHandshakeStage(
                outboundContext,
                retryInterval: TimeSpan.FromMilliseconds(200),
                handshakeTimeout: TimeSpan.FromSeconds(30),
                injectHandshakeInterval: TimeSpan.FromSeconds(30),
                isControlStream: false,
                forceReqOnStart: false);

            var (outPub, outSub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await outSub.RequestAsync(1);
            await outPub.SendNextAsync(new OutboundEnvelope("user-message-1", null, null));

            // THE REGRESSION: before the fix, the stage declared itself Completed at PreStart and
            // released this element immediately, having sent no HandshakeReq of its own -- so the
            // peer had no way of ever learning our uid, and dropped the message.
            await outSub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
            await AwaitConditionAsync(() => Task.FromResult(sentControl.Count > 0), 3.Seconds());
            sentControl.Should().AllBeOfType<HandshakeReq>("an association populated purely by an INBOUND handshake must not satisfy the PreStart shortcut");
            ((HandshakeReq)sentControl[0]).From.Should().Be(localAddress);
            ((HandshakeReq)sentControl[0]).To.Should().Be(remoteAddress);

            // The peer answers OUR Req -- the only event that proves it has registered our uid.
            await inSub.RequestAsync(1);
            await inPub.SendNextAsync(ControlInbound(new HandshakeRsp(peer), peer.Uid));

            var delivered = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(5));
            delivered.Message.Should().Be("user-message-1", "the held element is released once the peer answers our own HandshakeReq");
        }

        [Fact(DisplayName = "OutboundHandshakeStage should not treat the peer's own retried HandshakeReq as completion of OUR handshake")]
        public async Task OutboundHandshakeStage_should_not_complete_on_the_peers_own_req_retry()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var peer = new UniqueAddress(remoteAddress, 444L);

            var materializer = ActorMaterializer.Create(Sys);

            var inboundContext = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var (inPub, inSub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(new InboundHandshakeStage(inboundContext)), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await inSub.RequestAsync(1);
            await inPub.SendNextAsync(ControlInbound(new HandshakeReq(peer, localAddress.Address), peer.Uid));
            await AwaitConditionAsync(
                () => Task.FromResult(registry.AssociationFor(remoteAddress).CurrentState.UniqueRemoteAddress is not null),
                3.Seconds());

            var sentControl = new List<object>();
            var stage = new OutboundHandshakeStage(
                new AssociationRegistryOutboundContext(registry, localAddress, remoteAddress, sentControl.Add),
                retryInterval: TimeSpan.FromMilliseconds(200),
                handshakeTimeout: TimeSpan.FromSeconds(30),
                injectHandshakeInterval: TimeSpan.FromSeconds(30),
                isControlStream: false,
                forceReqOnStart: false);

            var (outPub, outSub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await outSub.RequestAsync(1);
            await outPub.SendNextAsync(new OutboundEnvelope("held-until-answered", null, null));
            await AwaitConditionAsync(() => Task.FromResult(sentControl.Count > 0), 3.Seconds());

            // The peer retries ITS OWN HandshakeReq -- it has not seen our HandshakeRsp yet. That
            // re-registers the peer's uid here and advances the association's handshake generation,
            // but proves nothing about whether the peer knows OUR uid, so our traffic stays held.
            await inPub.SendNextAsync(ControlInbound(new HandshakeReq(peer, localAddress.Address), peer.Uid));
            await outSub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            // Only the peer's answer to OUR Req releases it.
            await inPub.SendNextAsync(ControlInbound(new HandshakeRsp(peer), peer.Uid));

            var delivered = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(5));
            delivered.Message.Should().Be("held-until-answered");
        }

        [Fact(DisplayName = "OutboundHandshakeStage should still skip the HandshakeReq on a fresh materialization once OUR OWN handshake has been answered (fast path preserved)")]
        public async Task OutboundHandshakeStage_should_keep_the_fast_path_after_our_own_handshake_completed()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var peer = new UniqueAddress(remoteAddress, 333L);

            var materializer = ActorMaterializer.Create(Sys);

            // A GENUINE local outbound handshake completion: the peer's HandshakeRsp -- which it
            // only ever sends after registering the uid carried in a HandshakeReq of OURS --
            // arrives on our inbound pipeline (e.g. on the control stream).
            var inboundContext = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var (inPub, inSub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(new InboundHandshakeStage(inboundContext)), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await inSub.RequestAsync(1);
            await inPub.SendNextAsync(ControlInbound(new HandshakeRsp(peer), peer.Uid));
            await AwaitConditionAsync(
                () => Task.FromResult(registry.AssociationFor(remoteAddress).CurrentState.UniqueRemoteAddress is not null),
                3.Seconds());

            // A LATER stream (ordinary/large/an extra lane) materializes against that same
            // association for the first time. It must NOT re-run the handshake.
            var sentControl = new List<object>();
            var outboundContext = new AssociationRegistryOutboundContext(
                registry, localAddress, remoteAddress, sentControl.Add);
            var stage = new OutboundHandshakeStage(
                outboundContext,
                retryInterval: TimeSpan.FromMilliseconds(200),
                handshakeTimeout: TimeSpan.FromSeconds(30),
                injectHandshakeInterval: TimeSpan.FromSeconds(30),
                isControlStream: false,
                forceReqOnStart: false);

            var (outPub, outSub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await outSub.RequestAsync(1);
            await outPub.SendNextAsync(new OutboundEnvelope("flows-immediately", null, null));

            var delivered = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(5));
            delivered.Message.Should().Be("flows-immediately", "traffic must not be gated a second time once the peer has answered our own Req");
            sentControl.Should().BeEmpty("a redundant HandshakeReq on every later stream would cost an extra round trip per materialization");
        }

        [Fact(DisplayName = "InboundHandshakeStage should log a WARNING (not a DEBUG) when it drops an ordinary message from an unknown origin uid")]
        public async Task InboundHandshakeStage_should_warn_when_dropping_an_unknown_origin_message()
        {
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry, NewLocal(), (_, _) => { });

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(new InboundHandshakeStage(context)), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            await EventFilter.Warning(contains: "unknown origin uid").ExpectOneAsync(async () =>
            {
                await pub.SendNextAsync(OrdinaryInbound("dropped-message", originUid: 987654321L));
                await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            });
        }

        #endregion

        #region End-to-end

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.loglevel = DEBUG
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """);

        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target)
            {
                ReceiveAny(msg => target.Tell(msg, Sender));
            }
        }

        [Fact(DisplayName = "Artery should deliver A's FIRST ordinary message to B when B dialed A first and A's HandshakeRsp never arrives")]
        public async Task Artery_should_deliver_the_first_ordinary_message_when_the_peer_dialed_first()
        {
            // systemA never delivers its HandshakeRsp. That is the deterministic form of the race
            // the issue describes: B's ONLY other way of learning A's uid is a HandshakeReq that A
            // sends of its own accord. Before the fix A never sent one (its outbound ordinary
            // stream short-circuited on the association B's own Req had created), so A's first
            // message to B was dropped at B's unknown-origin gate and never resent.
            var systemA = ActorSystem.Create("ArteryPeerDialedFirstA", ActorSystemSetup.Create(
                BootstrapSetup.Create().WithConfig(ArteryConfig()),
                new ArteryTransportSetup(dropOutboundControlMessage: msg => msg is HandshakeRsp)));
            var systemB = ActorSystem.Create("ArteryPeerDialedFirstB", ArteryConfig());

            try
            {
                var addressA = RARP.For(systemA).Provider.DefaultAddress;
                var addressB = RARP.For(systemB).Provider.DefaultAddress;
                var transportA = (ArteryRemoting)RARP.For(systemA).Provider.Transport;

                var sinkProbeOnB = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new Forwarder(sinkProbeOnB.Ref)), "sink");

                // STEP 1 -- B dials A. B's control stream sends HandshakeReq(B), so A creates the
                // association for B's address and records B's uid, purely from the INBOUND
                // direction. (B's own user message stays held behind B's handshake; the test does
                // not depend on it arriving.)
                systemB.ActorSelection($"{addressA}/user/does-not-need-to-exist").Tell("b-dials-a", ActorRefs.NoSender);

                // Deterministic ordering gate -- no sleeps: A has processed B's HandshakeReq.
                await AwaitAssertAsync(
                    () => transportA.Registry.AssociationFor(addressB).CurrentState.UniqueRemoteAddress
                        .Should().NotBeNull("A must have recorded B's uid from B's inbound HandshakeReq before A sends anything"),
                    10.Seconds());

                // STEP 2 -- ONLY NOW does A send its first ordinary message to B.
                systemA.ActorSelection($"{addressB}/user/sink").Tell("a-first-message", ActorRefs.NoSender);

                await sinkProbeOnB.ExpectMsgAsync("a-first-message", 20.Seconds());
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(20.Seconds());
                await systemB.Terminate().AwaitWithTimeout(20.Seconds());
            }
        }

        #endregion
    }
}
