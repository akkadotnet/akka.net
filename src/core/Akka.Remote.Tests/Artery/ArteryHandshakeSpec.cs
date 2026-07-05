//-----------------------------------------------------------------------
// <copyright file="ArteryHandshakeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Stream tests for the handshake <see cref="Akka.Streams.Stage.GraphStage{TShape}"/>s:
    /// <see cref="OutboundHandshakeStage"/> and <see cref="InboundHandshakeStage"/>. Uses
    /// <c>TestSource</c>/<c>TestSink</c> probes to drive/observe the stages directly (no TCP, no
    /// real association transport — that lands in a later chunk). See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)").
    ///
    /// <para>
    /// <b>Package-reference note (flagged per the task):</b> <c>Akka.Remote.Tests</c> did not
    /// previously reference <c>Akka.Streams</c> or <c>Akka.Streams.TestKit</c>. This change adds
    /// a <c>ProjectReference</c> to <c>Akka.Streams.TestKit.csproj</c> (see
    /// <c>Akka.Remote.Tests.csproj</c>) — a first-party project, no new NuGet package. It is
    /// required because <c>Akka.Remote</c> itself now references <c>Akka.Streams</c> (also new,
    /// see <c>Akka.Remote.csproj</c>) so that the handshake stages can be
    /// <c>GraphStage&lt;FlowShape&lt;IOutboundEnvelope, IOutboundEnvelope&gt;&gt;</c> /
    /// <c>GraphStage&lt;FlowShape&lt;IInboundEnvelope, IInboundEnvelope&gt;&gt;</c> as design.md
    /// specifies (re-typed from the earlier <c>object</c>-element shape at the G3 opening refactor).
    /// </para>
    /// </summary>
    public class ArteryHandshakeSpec : AkkaSpec
    {
        public ArteryHandshakeSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static UniqueAddress NewLocal() => new(new Address("akka", "local-sys", "local-host", 2551), 111L);

        private static Address NewRemote() => new("akka", "remote-sys", "remote-host", 2552);

        /// <summary>
        /// An ordinary (non-control) inbound test envelope. SerializerId is not exercised by these
        /// stages, so an arbitrary placeholder value is used. <c>originUid</c> DOES matter as of
        /// task group 6 (control stream): <see cref="InboundHandshakeStage"/> now gates ordinary
        /// envelopes on <see cref="IInboundContext.IsKnownOrigin"/>, a registry lookup keyed by
        /// this exact value -- see that stage's "Known origin is now a SHARED-registry check" remarks.
        /// </summary>
        private static IInboundEnvelope OrdinaryInbound(object message, string? senderPath = null, string recipientPath = "akka://remote-sys@remote-host:2552/user/recipient", long originUid = 0L) =>
            new InboundEnvelope(message, senderPath, recipientPath, originUid, SerializerId: 0, Manifest: "test-manifest");

        /// <summary>A control inbound test envelope wrapping a handshake message.</summary>
        private static IInboundEnvelope ControlInbound(IArteryControlMessage message, long originUid) =>
            new InboundEnvelope(message, null, null, originUid, SerializerId: 0, Manifest: "test-manifest");

        #region OutboundHandshakeStage

        [Fact(DisplayName = "OutboundHandshakeStage should inject HandshakeReq first, hold the pending element until completion, then flow it")]
        public async Task OutboundHandshakeStage_should_inject_req_first_hold_then_flow_after_completion()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var context = new AssociationRegistryOutboundContext(registry, localAddress, remoteAddress, _ => { });
            var stage = new OutboundHandshakeStage(
                context,
                retryInterval: TimeSpan.FromSeconds(1),
                handshakeTimeout: TimeSpan.FromSeconds(10),
                injectHandshakeInterval: TimeSpan.FromSeconds(10));

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);
            var firstReq = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            firstReq.IsControl.Should().BeTrue("the injected handshake request is a control envelope");
            firstReq.Message.Should().BeOfType<HandshakeReq>();
            ((HandshakeReq)firstReq.Message).From.Should().Be(localAddress);
            ((HandshakeReq)firstReq.Message).To.Should().Be(remoteAddress);

            await pub.SendNextAsync(new OutboundEnvelope("user-message-1", null, null));

            await sub.RequestAsync(1);
            // Well within the 1s retry interval, so nothing (neither a resend nor the held
            // element) should arrive yet - the element is genuinely held.
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            // Simulate what InboundHandshakeStage does when the peer's HandshakeRsp arrives on
            // OUR inbound pipeline for the return direction.
            registry.CompleteHandshake(remoteAddress, new UniqueAddress(remoteAddress, 222L));

            // Nothing re-triggers the stage directly; per the documented notification mechanism,
            // the retry timer (still running) is what notices completion and delivers the held
            // element - bounded by one retry interval.
            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
            delivered.IsControl.Should().BeFalse("the held user envelope is not a control message");
            delivered.Message.Should().Be("user-message-1");
        }

        [Fact(DisplayName = "OutboundHandshakeStage should emit the injected control envelope ahead of a held user envelope, which retains its original sender/recipient paths")]
        public async Task OutboundHandshakeStage_should_emit_control_envelope_ahead_of_held_user_envelope_with_paths_retained()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var context = new AssociationRegistryOutboundContext(registry, localAddress, remoteAddress, _ => { });
            var stage = new OutboundHandshakeStage(
                context,
                retryInterval: TimeSpan.FromSeconds(1),
                handshakeTimeout: TimeSpan.FromSeconds(10),
                injectHandshakeInterval: TimeSpan.FromSeconds(10));

            const string senderPath = "akka://local-sys@local-host:2551/user/sender";
            const string recipientPath = "akka://remote-sys@remote-host:2552/user/recipient";

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            // The FIRST element out of the stage must be the injected control envelope, ahead of
            // any held user traffic -- carrying no sender/recipient path of its own.
            await sub.RequestAsync(1);
            var firstElement = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            firstElement.IsControl.Should().BeTrue("the injected handshake request is a control envelope");
            firstElement.Message.Should().BeOfType<HandshakeReq>();
            firstElement.SenderPath.Should().BeNull("control envelopes carry no sender/recipient path");
            firstElement.RecipientPath.Should().BeNull("control envelopes carry no sender/recipient path");

            // A user envelope with real sender/recipient paths arrives behind the (still
            // incomplete) handshake and must be held, not dropped or reordered.
            await pub.SendNextAsync(new OutboundEnvelope("held-payload", senderPath, recipientPath));

            await sub.RequestAsync(1);
            // Well within the 1s retry interval -- the held element is genuinely held, not merely
            // slow to arrive.
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            registry.CompleteHandshake(remoteAddress, new UniqueAddress(remoteAddress, 222L));

            // The retry timer (still running) is what notices completion and releases the held
            // element - bounded by one retry interval; a legal idempotent retry HandshakeReq may
            // race in first, so tolerate (and count) those the same way the end-to-end test does.
            IOutboundEnvelope delivered;
            var drainedRetries = 0;
            while (true)
            {
                delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
                if (delivered.Message is HandshakeReq)
                {
                    drainedRetries++;
                    drainedRetries.Should().BeLessThan(10, "retry HandshakeReqs must stop once the handshake completes");
                    await sub.RequestAsync(1);
                    continue;
                }

                break;
            }

            delivered.IsControl.Should().BeFalse("the held user envelope is not a control message");
            delivered.Message.Should().Be("held-payload");
            delivered.SenderPath.Should().Be(senderPath, "the held element's sender path must survive being queued behind the handshake");
            delivered.RecipientPath.Should().Be(recipientPath, "the held element's recipient path must survive being queued behind the handshake");
        }

        [Fact(DisplayName = "OutboundHandshakeStage should resend HandshakeReq at the retry interval while incomplete")]
        public async Task OutboundHandshakeStage_should_resend_req_before_completion()
        {
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryOutboundContext(registry, NewLocal(), NewRemote(), _ => { });
            var stage = new OutboundHandshakeStage(
                context,
                retryInterval: TimeSpan.FromMilliseconds(150),
                handshakeTimeout: TimeSpan.FromSeconds(30),
                injectHandshakeInterval: TimeSpan.FromSeconds(30));

            var materializer = ActorMaterializer.Create(Sys);
            var (_, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Message.Should().BeOfType<HandshakeReq>();

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Message.Should().BeOfType<HandshakeReq>("the retry timer should resend while incomplete");

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Message.Should().BeOfType<HandshakeReq>("resends should keep happening every retry interval");
        }

        [Fact(DisplayName = "OutboundHandshakeStage should fail with HandshakeTimeoutException when the handshake never completes")]
        public async Task OutboundHandshakeStage_should_fail_on_timeout()
        {
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryOutboundContext(registry, NewLocal(), NewRemote(), _ => { });
            var stage = new OutboundHandshakeStage(
                context,
                retryInterval: TimeSpan.FromMilliseconds(100),
                handshakeTimeout: TimeSpan.FromMilliseconds(400),
                injectHandshakeInterval: TimeSpan.FromSeconds(30));

            var materializer = ActorMaterializer.Create(Sys);
            var (_, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Message.Should().BeOfType<HandshakeReq>();

            var error = await sub.ExpectErrorAsync();
            error.Should().BeOfType<HandshakeTimeoutException>();
        }

        [Fact(DisplayName = "OutboundHandshakeStage (isControlStream: false) should route the injected HandshakeReq via Context.SendControl instead of its own Out (task 6.3: ordinary stream's Req travels over the control channel)")]
        public async Task OutboundHandshakeStage_non_control_should_route_req_via_send_control()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var sentControl = new System.Collections.Generic.List<object>();
            var context = new AssociationRegistryOutboundContext(registry, localAddress, remoteAddress, sentControl.Add);
            var stage = new OutboundHandshakeStage(
                context,
                retryInterval: TimeSpan.FromMilliseconds(150),
                handshakeTimeout: TimeSpan.FromSeconds(30),
                injectHandshakeInterval: TimeSpan.FromSeconds(30),
                isControlStream: false);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            // Request demand and wait long enough for at least one retry-interval tick to have
            // fired -- the Req must NOT appear on this stage's own Out (it travels via SendControl
            // instead), so nothing should be delivered here even though the stage is actively
            // (re)injecting on the side channel the whole time.
            await sub.RequestAsync(1);
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(400));

            await AwaitConditionAsync(() => Task.FromResult(sentControl.Count > 0), TimeSpan.FromSeconds(2));
            sentControl.Should().AllBeOfType<HandshakeReq>("the ordinary stream's injected Req must travel via the control side channel, not inline");
            ((HandshakeReq)sentControl[0]).From.Should().Be(localAddress);
            ((HandshakeReq)sentControl[0]).To.Should().Be(remoteAddress);

            // A user element sent while incomplete is still held (never dropped) -- gating
            // behavior is unchanged by the control-routing change.
            await pub.SendNextAsync(new OutboundEnvelope("user-message", null, null));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            registry.CompleteHandshake(remoteAddress, new UniqueAddress(remoteAddress, 555L));

            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(3));
            delivered.Message.Should().Be("user-message");
        }

        [Fact(DisplayName = "OutboundHandshakeStage (isControlStream: false) should flow a user element through immediately on liveness re-injection, without holding it (task 6.3: the Req no longer competes for this stream's Out slot)")]
        public async Task OutboundHandshakeStage_non_control_should_not_hold_elements_for_liveness_reinject()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();
            var sentControl = new System.Collections.Generic.List<object>();
            var context = new AssociationRegistryOutboundContext(registry, localAddress, remoteAddress, sentControl.Add);

            // Already-associated at PreStart (Completed immediately) -- exercises the
            // "ShouldReinjectForLiveness" path directly rather than the initial handshake path.
            registry.CompleteHandshake(remoteAddress, new UniqueAddress(remoteAddress, 777L));

            var stage = new OutboundHandshakeStage(
                context,
                retryInterval: TimeSpan.FromSeconds(30),
                handshakeTimeout: TimeSpan.FromSeconds(30),
                injectHandshakeInterval: TimeSpan.FromMilliseconds(20), // "due" for reinjection well before the delay below elapses
                isControlStream: false);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            // Deterministically clear the injectHandshakeInterval window (set at PreStart, since
            // the association is already completed) before sending -- otherwise this assertion
            // would race PreStart's timestamp against however fast the test harness happens to
            // shuttle the SourceProbe/SinkProbe round trip on a given run.
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            await pub.SendNextAsync(new OutboundEnvelope("immediate-payload", null, null));

            // The user element flows through on THIS pull -- it is not held behind a Req that
            // would otherwise have shared this stream's Out slot.
            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            delivered.Message.Should().Be("immediate-payload");

            sentControl.Should().ContainSingle().Which.Should().BeOfType<HandshakeReq>("liveness re-injection still fires, just via the control side channel");
        }

        #endregion

        #region InboundHandshakeStage

        [Fact(DisplayName = "InboundHandshakeStage should drop a HandshakeReq with a mismatched To address without failing the stream")]
        public async Task InboundHandshakeStage_should_drop_wrong_to_handshake_req()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var sentControl = new List<(Address To, object Message)>();
            var context = new AssociationRegistryInboundContext(registry, localAddress, (to, msg) => sentControl.Add((to, msg)));
            var stage = new InboundHandshakeStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var wrongTo = new Address("akka", "some-other-sys", "other-host", 9999);
            var peer = new UniqueAddress(NewRemote(), 222L);
            await pub.SendNextAsync(ControlInbound(new HandshakeReq(peer, wrongTo), peer.Uid));

            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            registry.TryGetByUid(peer.Uid).Should().BeNull("a misdirected HandshakeReq must not complete a handshake");
            sentControl.Should().BeEmpty("a misdirected HandshakeReq must not get a HandshakeRsp reply");
        }

        [Fact(DisplayName = "InboundHandshakeStage should complete the handshake and reply on a correctly-addressed HandshakeReq")]
        public async Task InboundHandshakeStage_should_complete_handshake_and_reply_on_correct_req()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var sentControl = new List<(Address To, object Message)>();
            var context = new AssociationRegistryInboundContext(registry, localAddress, (to, msg) => sentControl.Add((to, msg)));
            var stage = new InboundHandshakeStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var remoteAddress = NewRemote();
            var peer = new UniqueAddress(remoteAddress, 222L);
            await pub.SendNextAsync(ControlInbound(new HandshakeReq(peer, localAddress.Address), peer.Uid));

            // The request itself is never propagated downstream.
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            registry.TryGetByUid(peer.Uid).Should().NotBeNull();
            sentControl.Should().ContainSingle();
            sentControl[0].To.Should().Be(remoteAddress);
            sentControl[0].Message.Should().BeOfType<HandshakeRsp>();
            ((HandshakeRsp)sentControl[0].Message).From.Should().Be(localAddress);
        }

        [Fact(DisplayName = "InboundHandshakeStage should drop ordinary messages while origin is unknown, then pass them through once known")]
        public async Task InboundHandshakeStage_should_gate_ordinary_messages_on_known_origin()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var context = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var stage = new InboundHandshakeStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var peer = new UniqueAddress(NewRemote(), 222L);

            // Task group 6: "known origin" is now a shared-registry lookup keyed by the envelope's
            // OWN OriginUid (see OrdinaryInbound's remarks) -- NOT a per-connection flag set only
            // by seeing a Req/Rsp on this exact stage instance (that was the G2 shape, when
            // handshake still rode the ordinary connection). So this envelope's uid (222L, matching
            // `peer`) is unknown to the registry until the HandshakeReq below completes it.
            await pub.SendNextAsync(OrdinaryInbound("ordinary-before-handshake", originUid: peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            await pub.SendNextAsync(ControlInbound(new HandshakeReq(peer, localAddress.Address), peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            await pub.SendNextAsync(OrdinaryInbound("ordinary-after-handshake", originUid: peer.Uid));
            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            delivered.IsControl.Should().BeFalse();
            delivered.Message.Should().Be("ordinary-after-handshake");
        }

        [Fact(DisplayName = "InboundHandshakeStage should gate on the envelope's OWN OriginUid, not on which connection carried the handshake (task 6.2/6.3: handshake now travels on a SEPARATE control connection from ordinary traffic)")]
        public async Task InboundHandshakeStage_should_gate_by_registry_not_by_owning_connection()
        {
            // Simulates task group 6's actual topology: ONE InboundHandshakeStage instance for an
            // ORDINARY connection that NEVER itself sees a HandshakeReq/Rsp (those arrive on a
            // separate CONTROL connection, processed by a DIFFERENT stage instance sharing the
            // SAME AssociationRegistry). Proves the ordinary connection's own stage instance still
            // gates correctly via the shared registry.
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var ordinaryContext = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var ordinaryStage = new InboundHandshakeStage(ordinaryContext);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(ordinaryStage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var peer = new UniqueAddress(NewRemote(), 333L);
            await pub.SendNextAsync(OrdinaryInbound("too-early", originUid: peer.Uid));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            // The handshake completes via the SHARED REGISTRY directly -- standing in for a
            // separate control connection's own InboundHandshakeStage instance calling
            // Context.CompleteHandshake. This ordinary-connection stage instance never itself
            // processes a HandshakeReq/Rsp.
            registry.CompleteHandshake(peer.Address, peer);

            await pub.SendNextAsync(OrdinaryInbound("now-known", originUid: peer.Uid));
            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            delivered.Message.Should().Be("now-known");
        }

        [Fact(DisplayName = "InboundHandshakeStage should complete the handshake on HandshakeRsp and swallow it (never propagated)")]
        public async Task InboundHandshakeStage_should_complete_handshake_on_rsp_and_swallow_it()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var context = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var stage = new InboundHandshakeStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var peer = new UniqueAddress(NewRemote(), 333L);
            await pub.SendNextAsync(ControlInbound(new HandshakeRsp(peer), peer.Uid));

            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            registry.TryGetByUid(peer.Uid).Should().NotBeNull();
        }

        [Fact(DisplayName = "InboundHandshakeStage should pass through a non-handshake control message unchanged (task 6.2: heartbeat/quarantine dispatch to IControlMessageSubscriber happens downstream)")]
        public async Task InboundHandshakeStage_should_pass_through_other_control_messages()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var context = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var stage = new InboundHandshakeStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var peer = new UniqueAddress(NewRemote(), 444L);
            await pub.SendNextAsync(ControlInbound(new ArteryHeartbeat(), peer.Uid));

            var delivered = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            delivered.IsControl.Should().BeTrue("a non-handshake control message is still a control envelope");
            delivered.Message.Should().BeOfType<ArteryHeartbeat>();
        }

        #endregion

        #region End-to-end (shared AssociationRegistry)

        [Fact(DisplayName = "End-to-end: a HandshakeRsp on the local inbound pipeline completes the association the local OutboundHandshakeStage is waiting on")]
        public async Task End_to_end_inbound_rsp_completes_outbound_handshake()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var remoteAddress = NewRemote();

            var outboundContext = new AssociationRegistryOutboundContext(registry, localAddress, remoteAddress, _ => { });
            var outboundStage = new OutboundHandshakeStage(
                outboundContext,
                retryInterval: TimeSpan.FromMilliseconds(200),
                handshakeTimeout: TimeSpan.FromSeconds(10),
                injectHandshakeInterval: TimeSpan.FromSeconds(10));

            var inboundContext = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var inboundStage = new InboundHandshakeStage(inboundContext);

            var materializer = ActorMaterializer.Create(Sys);

            var (outPub, outSub) = this.SourceProbe<IOutboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(outboundStage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IOutboundEnvelope>(), Keep.Both)
                .Run(materializer);

            var (inPub, inSub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(inboundStage), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await outSub.RequestAsync(1);
            var req = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            req.Message.Should().BeOfType<HandshakeReq>();

            // Deliberately grant NO downstream demand during the hold window: with zero demand the
            // stage cannot emit anything (a push would be a Reactive Streams violation the probe
            // catches), so this window is immune to retry-timer phase. Granting demand here made
            // the assertion a timer-phase coin flip — the 200ms retry interval races the 200ms
            // window and a (legal, idempotent) retry HandshakeReq lands inside it on slow CI
            // agents; see PR #8320 CI failure.
            await outPub.SendNextAsync(new OutboundEnvelope("payload-1", null, null));
            await outSub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            // Feed the (simulated) remote peer's Rsp into OUR inbound pipeline.
            var remoteUniqueAddress = new UniqueAddress(remoteAddress, 222L);
            await inSub.RequestAsync(1);
            await inPub.SendNextAsync(ControlInbound(new HandshakeRsp(remoteUniqueAddress), remoteUniqueAddress.Uid));
            await inSub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            // Deterministic gate: wait until the inbound stage's CompleteHandshake has actually
            // been recorded before expecting the outbound stage to observe it.
            await AwaitConditionAsync(() => Task.FromResult(registry.TryGetByUid(remoteUniqueAddress.Uid) != null),
                TimeSpan.FromSeconds(3));

            // The SAME AssociationRegistry backs both contexts, so the outbound stage's polling
            // of AssociationState observes the completion the inbound stage just recorded. Retry
            // HandshakeReqs queued while demand was withheld may drain first — they are legal
            // protocol traffic (requests are idempotent); only the held payload's release order
            // relative to OTHER USER MESSAGES matters, so skip Reqs while fishing.
            IOutboundEnvelope delivered;
            var drainedRetries = 0;
            while (true)
            {
                await outSub.RequestAsync(1);
                delivered = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(3));
                if (delivered.Message is HandshakeReq)
                {
                    drainedRetries++;
                    drainedRetries.Should().BeLessThan(10, "retry HandshakeReqs must stop once the handshake completes");
                    continue;
                }

                break;
            }

            delivered.Message.Should().Be("payload-1");
        }

        #endregion
    }
}
