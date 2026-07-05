//-----------------------------------------------------------------------
// <copyright file="SystemMessageAckerStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Inbound half of reliable system-message delivery (design.md gate G3, "Reliable
    /// system-message delivery"). A <c>GraphStage&lt;FlowShape&lt;IInboundEnvelope, IInboundEnvelope&gt;&gt;</c>
    /// composed on EVERY accepted (inbound) Artery connection, positioned right after
    /// <see cref="InboundHandshakeStage"/> and before the final dispatch sink (mirrors the reference
    /// pipeline in design.md's type-level "Inbound pipeline" diagram: <c>... InboundHandshake ->
    /// InboundQuarantineCheck -> [control only: SystemMessageAcker -- dedup + ACK] -> messageDispatcherSink</c>).
    ///
    /// <para>
    /// <b>Per-sender-uid expected-seq map, NO inbound reorder buffer (design.md, verified against
    /// Pekko's deliberately simpler Artery protocol -- see <see cref="SystemMessageEnvelope"/>'s
    /// type-level remarks).</b> For each inbound <see cref="SystemMessageEnvelope"/>:
    /// <list type="bullet">
    /// <item><description><c>seq == expected</c>: DELIVER downstream (re-emitted as a plain,
    /// non-control <see cref="IInboundEnvelope"/> wrapping the inner system message, so the existing
    /// dispatch sink's ordinary-message path handles it -- see <c>ArteryRemoting.DispatchInbound</c>'s
    /// <c>ISystemMessage</c> check, which calls <c>SendSystemMessage</c> instead of <c>Tell</c>),
    /// advance <c>expected</c>, reply <see cref="Ack"/>.</description></item>
    /// <item><description><c>seq &lt; expected</c>: duplicate -- drop, re-reply <see cref="Ack"/> for
    /// <c>expected - 1</c> (covers a lost Ack: the sender's resend timer will keep retrying until it
    /// sees this).</description></item>
    /// <item><description><c>seq &gt; expected</c>: gap -- drop, reply <see cref="Nack"/> for
    /// <c>expected - 1</c>. NO buffering of the out-of-order frame: the SENDER restores order by
    /// resending its whole unacknowledged window (immediately, on receiving the Nack).</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Eviction policy (design.md's open decision -- "Pekko has an unbounded-growth TODO; do at
    /// least incarnation-based cleanup").</b> This stage's expected-seq dictionary is scoped to the
    /// ONE accepted TCP connection it is materialized on (design.md: exactly one control connection
    /// per association). A genuinely new incarnation (the peer's <c>ActorSystem</c> restarted under a
    /// new uid) requires a brand-new inbound connection -- which materializes a BRAND-NEW
    /// <see cref="SystemMessageAckerStage"/> instance with an empty dictionary. So incarnation-based
    /// cleanup falls out of the connection lifecycle for free; no additional eviction bookkeeping is
    /// implemented (a single long-lived connection that somehow cycled through many peer incarnations
    /// without ever reconnecting could still accumulate stale-uid entries -- the same open TODO Pekko
    /// has, not fully closed here either, per design.md's explicit "do at least" floor).
    /// <see cref="ClearSystemMessageDelivery"/> deliberately never crosses the wire in this
    /// implementation (see its own type docs) so there is no wire-triggered eviction path to wire up
    /// here.
    /// </para>
    /// </summary>
    internal sealed class SystemMessageAckerStage : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        public SystemMessageAckerStage(IInboundContext context)
        {
            Context = context;
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);
        }

        public IInboundContext Context { get; }

        public Inlet<IInboundEnvelope> In { get; } = new("SystemMessageAcker.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("SystemMessageAcker.out");

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly SystemMessageAckerStage _stage;

            /// <summary>Next expected seq number, per sender uid. Absent == 1 (nothing seen yet).</summary>
            private readonly Dictionary<long, long> _expectedByOriginUid = new();

            public Logic(SystemMessageAckerStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var envelope = Grab(_stage.In);

                if (envelope.Message is SystemMessageEnvelope sme)
                {
                    HandleSystemMessageEnvelope(envelope.OriginUid, sme);
                    return;
                }

                // Pass-through: ordinary messages, Ack/Nack replies (destined for this system's OWN
                // outbound SystemMessageDeliveryStage via ArteryRemoting's control-subscriber
                // broadcast), heartbeat, quarantine notices, etc.
                Push(_stage.Out, envelope);
            }

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(System.Exception e) => FailStage(e);

            public void OnDownstreamFinish(System.Exception cause) => InternalOnDownstreamFinish(cause);

            private void HandleSystemMessageEnvelope(long originUid, SystemMessageEnvelope sme)
            {
                var expected = _expectedByOriginUid.TryGetValue(originUid, out var e) ? e : 1L;

                if (sme.SeqNo == expected)
                {
                    _expectedByOriginUid[originUid] = expected + 1;
                    SendAck(sme.AckReplyTo, expected);

                    // Re-emitted as an ORDINARY-shaped envelope -- IsControl computes to false since
                    // the inner message is not an IArteryControlMessage, so it flows straight through
                    // ArteryRemoting.DispatchInbound's existing ordinary-message path unchanged
                    // (that path is updated to dispatch via SendSystemMessage, not Tell, when the
                    // resolved Message is an ISystemMessage).
                    Push(_stage.Out, new InboundEnvelope(sme.Message, null, sme.RecipientPath, originUid, SerializerId: 0, Manifest: string.Empty));
                    return;
                }

                if (sme.SeqNo < expected)
                {
                    // Duplicate -- covers a lost Ack: re-Ack so the sender's resend timer can
                    // eventually observe it and shrink its buffer.
                    SendAck(sme.AckReplyTo, expected - 1);
                }
                else
                {
                    // Gap -- no inbound reorder buffer; the sender restores order by resending its
                    // whole unacknowledged window on Nack.
                    SendNack(sme.AckReplyTo, expected - 1);
                }

                Pull(_stage.In);
            }

            private void SendAck(UniqueAddress replyTo, long seqNo) =>
                _stage.Context.SendControl(replyTo.Address, new Ack(seqNo, _stage.Context.LocalAddress));

            private void SendNack(UniqueAddress replyTo, long seqNo) =>
                _stage.Context.SendControl(replyTo.Address, new Nack(seqNo, _stage.Context.LocalAddress));
        }
    }
}
