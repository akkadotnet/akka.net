//-----------------------------------------------------------------------
// <copyright file="InboundHandshakeStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Inbound half of the Artery handshake, faithful to
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)"; routing changes per task group 6, "Control
    /// Stream", task 6.2). A <c>GraphStage&lt;FlowShape&lt;IInboundEnvelope, IInboundEnvelope&gt;&gt;</c>
    /// -- dispatch is on <see cref="IInboundEnvelope.IsControl"/> plus a pattern match on
    /// <see cref="IInboundEnvelope.Message"/> inside the envelope, not a raw <c>is</c> type-test
    /// on the stream element itself.
    ///
    /// <para>
    /// <b>"Known origin" is now a SHARED-registry check, not per-connection state (task 6.2/6.3).</b>
    /// One instance of this stage sees the inbound elements for ONE physical TCP connection. At
    /// G2 (single ordinary connection carrying both handshake and user traffic) that made a
    /// per-instance <c>isKnownOrigin</c> flag correct: the Req/Rsp that completed the handshake
    /// necessarily flowed through the SAME instance before any user traffic could. Once 6.3 routes
    /// handshake messages onto a SEPARATE control connection, an ordinary connection's own
    /// <see cref="InboundHandshakeStage"/> instance would never itself observe a Req/Rsp and would
    /// perpetually gate/drop everything. So the gate is now <see cref="IInboundContext.IsKnownOrigin"/>
    /// — a lookup against the SHARED <see cref="AssociationRegistry"/> keyed by the envelope's own
    /// <see cref="IInboundEnvelope.OriginUid"/> (always present in the decoded header, regardless
    /// of which connection/stream carried the envelope). This is safe because the SENDING side's
    /// <see cref="OutboundHandshakeStage"/> holds all ordinary/large traffic behind its own
    /// handshake-completion gate, which — by construction — cannot complete before the RECEIVING
    /// side has already processed the peer's <see cref="HandshakeReq"/> (registering the uid) and
    /// sent its <see cref="HandshakeRsp"/>. So by the time a receiver's ordinary connection ever
    /// delivers a real user envelope for some uid, that uid is already registered.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// On <see cref="HandshakeReq"/>: if <c>req.To</c> does not match the local address, logs a
    /// warning and DROPS the message — it does NOT fail the stream (a misdirected/stale request
    /// must not tear down an otherwise-healthy connection). Otherwise, completes the handshake for
    /// the requester via <see cref="IInboundContext.CompleteHandshake"/> and replies with a
    /// <see cref="HandshakeRsp"/> via <see cref="IInboundContext.SendControl"/>. The request
    /// itself is never propagated downstream.
    /// </description></item>
    /// <item><description>
    /// On <see cref="HandshakeRsp"/>: completes the handshake for the responder (this is what lets
    /// the peer's <see cref="OutboundHandshakeStage"/> observe completion — see that type's
    /// notification-mechanism note) and swallows the message (never propagated downstream).
    /// </description></item>
    /// <item><description>
    /// Any OTHER control envelope (task 6.2: <c>ArteryHeartbeat</c>/<c>ArteryHeartbeatRsp</c>/
    /// <c>ArteryQuarantined</c>, and later reliable system-message ACK/NACK): NOT handshake-internal
    /// -- pushed downstream unchanged (still <see cref="IInboundEnvelope.IsControl"/> true) so
    /// <c>ArteryRemoting.DispatchInbound</c> can hand it to the registered
    /// <see cref="IControlMessageSubscriber"/>s.
    /// </description></item>
    /// <item><description>
    /// Any ordinary (non-control) envelope: dropped with a debug log while the origin is unknown;
    /// passed through once known (per the registry-based check above).
    /// </description></item>
    /// </list>
    /// </summary>
    internal sealed class InboundHandshakeStage : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        public InboundHandshakeStage(IInboundContext context)
        {
            Context = context;
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);
        }

        public IInboundContext Context { get; }

        public Inlet<IInboundEnvelope> In { get; } = new("InboundHandshake.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("InboundHandshake.out");

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly InboundHandshakeStage _stage;

            public Logic(InboundHandshakeStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var envelope = Grab(_stage.In);

                if (envelope.IsControl)
                {
                    switch (envelope.Message)
                    {
                        case HandshakeReq req:
                            HandleReq(req);
                            Pull(_stage.In);
                            return;

                        case HandshakeRsp rsp:
                            HandleRsp(rsp);
                            Pull(_stage.In);
                            return;

                        default:
                            // Not handshake-internal (heartbeat, quarantine notice, future
                            // system-message ACK/NACK, ...) -- pass through so ArteryRemoting can
                            // dispatch to its registered IControlMessageSubscribers (task 6.2).
                            Push(_stage.Out, envelope);
                            return;
                    }
                }

                if (!_stage.Context.IsKnownOrigin(envelope.OriginUid))
                {
                    Log.Debug(
                        "Dropping inbound message [{0}] from unknown origin uid [{1}] (no completed handshake for this uid yet).",
                        envelope.Message.GetType(), envelope.OriginUid);
                    Pull(_stage.In);
                    return;
                }

                Push(_stage.Out, envelope);
            }

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);

            private void HandleReq(HandshakeReq req)
            {
                if (!Equals(req.To, _stage.Context.LocalAddress.Address))
                {
                    Log.Warning(
                        "Dropping HandshakeReq from [{0}] addressed to [{1}], which does not match the local address [{2}].",
                        req.From, req.To, _stage.Context.LocalAddress.Address);
                    return;
                }

                _stage.Context.CompleteHandshake(req.From);
                _stage.Context.SendControl(req.From.Address, new HandshakeRsp(_stage.Context.LocalAddress));
            }

            private void HandleRsp(HandshakeRsp rsp) => _stage.Context.CompleteHandshake(rsp.From);
        }
    }
}
