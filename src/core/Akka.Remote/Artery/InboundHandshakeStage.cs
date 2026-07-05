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
    /// ("Handshake + association/UID (gate G2)"). A
    /// <c>GraphStage&lt;FlowShape&lt;IInboundEnvelope, IInboundEnvelope&gt;&gt;</c> -- dispatch is on
    /// <see cref="IInboundEnvelope.IsControl"/> plus a pattern match on <see cref="IInboundEnvelope.Message"/>
    /// inside the envelope, not a raw <c>is</c> type-test on the stream element itself.
    ///
    /// <para>
    /// One instance of this stage sees the inbound elements for ONE remote peer connection (per
    /// design.md's "Connection cardinality" note: at G2 the handshake stages ride the single
    /// ordinary connection, and the receiver sees one ordinary connection per remote peer) — so
    /// <c>isKnownOrigin</c> is per-stage-instance state, not global.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// On <see cref="HandshakeReq"/>: if <c>req.To</c> does not match the local address, logs a
    /// warning and DROPS the message — it does NOT fail the stream (a misdirected/stale request
    /// must not tear down an otherwise-healthy connection). Otherwise, completes the handshake for
    /// the requester via <see cref="IInboundContext.CompleteHandshake"/>, marks the origin known,
    /// and replies with a <see cref="HandshakeRsp"/> via <see cref="IInboundContext.SendControl"/>.
    /// The request itself is never propagated downstream.
    /// </description></item>
    /// <item><description>
    /// On <see cref="HandshakeRsp"/>: completes the handshake for the responder (this is what lets
    /// the peer's <see cref="OutboundHandshakeStage"/> observe completion — see that type's
    /// notification-mechanism note), marks the origin known, and swallows the message (never
    /// propagated downstream).
    /// </description></item>
    /// <item><description>
    /// Any other control envelope: dropped with a debug log (no other control-message types exist
    /// yet at G3 -- reliable system-message delivery lands in a later chunk).
    /// </description></item>
    /// <item><description>
    /// Any ordinary (non-control) envelope: dropped with a debug log while the origin is unknown;
    /// passed through once known.
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
            private bool _isKnownOrigin;
            private Address? _originAddress;

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
                            break;

                        case HandshakeRsp rsp:
                            HandleRsp(rsp);
                            break;

                        default:
                            // No other control-message types exist yet at G3 -- reliable
                            // system-message delivery (Ack/Nack/SystemMessageEnvelope) lands in a
                            // later chunk.
                            Log.Debug("Dropping inbound control envelope of unknown message type [{0}].", envelope.Message.GetType());
                            break;
                    }

                    Pull(_stage.In);
                    return;
                }

                if (!_isKnownOrigin)
                {
                    Log.Debug("Dropping inbound message [{0}] from unknown origin (no completed handshake on this connection yet).", envelope.Message.GetType());
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
                MarkKnownOrigin(req.From.Address);
                _stage.Context.SendControl(req.From.Address, new HandshakeRsp(_stage.Context.LocalAddress));
            }

            private void HandleRsp(HandshakeRsp rsp)
            {
                _stage.Context.CompleteHandshake(rsp.From);
                MarkKnownOrigin(rsp.From.Address);
            }

            private void MarkKnownOrigin(Address origin)
            {
                _isKnownOrigin = true;
                _originAddress = origin;
            }
        }
    }
}
