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
    /// Stream tests for the G2 handshake <see cref="Akka.Streams.Stage.GraphStage{TShape}"/>s:
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
    /// <c>GraphStage&lt;FlowShape&lt;object, object&gt;&gt;</c> as design.md specifies.
    /// </para>
    /// </summary>
    public class ArteryHandshakeSpec : AkkaSpec
    {
        public ArteryHandshakeSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static UniqueAddress NewLocal() => new(new Address("akka", "local-sys", "local-host", 2551), 111L);

        private static Address NewRemote() => new("akka", "remote-sys", "remote-host", 2552);

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
            var (pub, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);
            var firstReq = await sub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            firstReq.Should().BeOfType<HandshakeReq>();
            ((HandshakeReq)firstReq).From.Should().Be(localAddress);
            ((HandshakeReq)firstReq).To.Should().Be(remoteAddress);

            await pub.SendNextAsync("user-message-1");

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
            delivered.Should().Be("user-message-1");
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
            var (_, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Should().BeOfType<HandshakeReq>();

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Should().BeOfType<HandshakeReq>("the retry timer should resend while incomplete");

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Should().BeOfType<HandshakeReq>("resends should keep happening every retry interval");
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
            var (_, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Should().BeOfType<HandshakeReq>();

            var error = await sub.ExpectErrorAsync();
            error.Should().BeOfType<HandshakeTimeoutException>();
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
            var (pub, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var wrongTo = new Address("akka", "some-other-sys", "other-host", 9999);
            var peer = new UniqueAddress(NewRemote(), 222L);
            await pub.SendNextAsync(new HandshakeReq(peer, wrongTo));

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
            var (pub, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var remoteAddress = NewRemote();
            var peer = new UniqueAddress(remoteAddress, 222L);
            await pub.SendNextAsync(new HandshakeReq(peer, localAddress.Address));

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
            var (pub, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            await pub.SendNextAsync("ordinary-before-handshake");
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            var peer = new UniqueAddress(NewRemote(), 222L);
            await pub.SendNextAsync(new HandshakeReq(peer, localAddress.Address));
            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            await pub.SendNextAsync("ordinary-after-handshake");
            (await sub.ExpectNextAsync(TimeSpan.FromSeconds(2))).Should().Be("ordinary-after-handshake");
        }

        [Fact(DisplayName = "InboundHandshakeStage should complete the handshake on HandshakeRsp and swallow it (never propagated)")]
        public async Task InboundHandshakeStage_should_complete_handshake_on_rsp_and_swallow_it()
        {
            var registry = new AssociationRegistry();
            var localAddress = NewLocal();
            var context = new AssociationRegistryInboundContext(registry, localAddress, (_, _) => { });
            var stage = new InboundHandshakeStage(context);

            var materializer = ActorMaterializer.Create(Sys);
            var (pub, sub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(stage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            var peer = new UniqueAddress(NewRemote(), 333L);
            await pub.SendNextAsync(new HandshakeRsp(peer));

            await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
            registry.TryGetByUid(peer.Uid).Should().NotBeNull();
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

            var (outPub, outSub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(outboundStage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            var (inPub, inSub) = this.SourceProbe<object>()
                .ViaMaterialized(Flow.FromGraph(inboundStage), Keep.Left)
                .ToMaterialized(this.SinkProbe<object>(), Keep.Both)
                .Run(materializer);

            await outSub.RequestAsync(1);
            var req = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(2));
            req.Should().BeOfType<HandshakeReq>();

            // Deliberately grant NO downstream demand during the hold window: with zero demand the
            // stage cannot emit anything (a push would be a Reactive Streams violation the probe
            // catches), so this window is immune to retry-timer phase. Granting demand here made
            // the assertion a timer-phase coin flip — the 200ms retry interval races the 200ms
            // window and a (legal, idempotent) retry HandshakeReq lands inside it on slow CI
            // agents; see PR #8320 CI failure.
            await outPub.SendNextAsync("payload-1");
            await outSub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

            // Feed the (simulated) remote peer's Rsp into OUR inbound pipeline.
            var remoteUniqueAddress = new UniqueAddress(remoteAddress, 222L);
            await inSub.RequestAsync(1);
            await inPub.SendNextAsync(new HandshakeRsp(remoteUniqueAddress));
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
            object delivered;
            var drainedRetries = 0;
            while (true)
            {
                await outSub.RequestAsync(1);
                delivered = await outSub.ExpectNextAsync(TimeSpan.FromSeconds(3));
                if (delivered is HandshakeReq)
                {
                    drainedRetries++;
                    drainedRetries.Should().BeLessThan(10, "retry HandshakeReqs must stop once the handshake completes");
                    continue;
                }

                break;
            }

            delivered.Should().Be("payload-1");
        }

        #endregion
    }
}
