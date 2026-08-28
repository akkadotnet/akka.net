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
    /// <see cref="OutboundHandshakeStage"/> holds all ordinary/large traffic until a
    /// <see cref="HandshakeRsp"/> answers a <see cref="HandshakeReq"/> of ITS OWN — and the only
    /// thing that sends a Rsp is <c>HandleReq</c> below, immediately after it registers the
    /// requester's uid. So by the time a sender releases its first ordinary envelope, this side has
    /// already registered that sender's uid.
    ///
    /// <para>
    /// That construction does NOT follow from the sender merely having an association with a known
    /// <see cref="AssociationState.UniqueRemoteAddress"/> — our own <see cref="HandshakeReq"/> sets
    /// that field on the sender, and it tells the sender nothing about whether WE know its uid. The
    /// outbound stage used to shortcut on exactly that (issue #8496); see
    /// <see cref="AssociationState.OutboundHandshakeCompleted"/>.
    /// </para>
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
    /// Any ordinary (non-control) envelope: dropped while the origin is unknown — with a
    /// rate-limited WARNING, since that drop is unrecoverable loss (ordinary messages are never
    /// resent); passed through once known (per the registry-based check above).
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
            /// <summary>
            /// Minimum gap between unknown-origin drop warnings on this connection: the drop is
            /// unrecoverable loss and must be visible, but a peer that restarted mid-flight can
            /// produce a burst of them, so the rest are folded into a suppressed count. No
            /// synchronization -- the stream runs one stage callback at a time.
            /// </summary>
            private static readonly TimeSpan UnknownOriginWarnInterval = TimeSpan.FromSeconds(10);

            private readonly InboundHandshakeStage _stage;
            private DateTime _lastUnknownOriginWarning = DateTime.MinValue;
            private long _suppressedUnknownOriginDrops;

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
                    var now = DateTime.UtcNow;
                    if (_lastUnknownOriginWarning == DateTime.MinValue ||
                        now - _lastUnknownOriginWarning >= UnknownOriginWarnInterval)
                    {
                        Log.Warning(
                            "Dropping inbound message [{0}] from unknown origin uid [{1}]: no completed handshake for this uid yet. " +
                            "The message is LOST - ordinary messages are not resent. [{2}] further drop(s) suppressed since the last warning.",
                            envelope.Message.GetType(), envelope.OriginUid, _suppressedUnknownOriginDrops);
                        _lastUnknownOriginWarning = now;
                        _suppressedUnknownOriginDrops = 0;
                    }
                    else
                    {
                        _suppressedUnknownOriginDrops++;
                    }

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

            // CompleteOutboundHandshake, not CompleteHandshake: a Rsp is the only proof that the
            // peer has registered OUR uid, which is what gates our ordinary outbound streams
            // (issue #8496). Re-completing with the same uid stays an idempotent no-op, so the
            // duplicate Rsps our own idempotent Req retries produce cost nothing.
            private void HandleRsp(HandshakeRsp rsp) => _stage.Context.CompleteOutboundHandshake(rsp.From);
        }
    }
}
