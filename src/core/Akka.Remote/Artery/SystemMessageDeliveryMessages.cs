//-----------------------------------------------------------------------
// <copyright file="SystemMessageDeliveryMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Dispatch.SysMsg;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The protocol messages for reliable system-message delivery (design.md gate G3, "Reliable
    /// system-message delivery"). Verified against Pekko <c>SystemMessageDelivery.scala</c>; reuses
    /// ONLY Akka.NET classic's wrap-safe <c>SeqNo</c> struct (<see cref="Akka.Remote.SeqNo"/>) for
    /// the raw <see langword="long"/> comparisons below -- the classic selective-NACK
    /// reorder-buffer protocol (<see cref="Akka.Remote.AckedSendBuffer{T}"/> /
    /// <see cref="Akka.Remote.AckedReceiveBuffer{T}"/>) is NOT reused; this is deliberately a new,
    /// simpler protocol (single strictly-monotonic seqNo, single-point Ack/Nack, NO inbound
    /// reorder buffer -- the sender restores order by resending).
    /// </summary>
    internal interface IArterySystemMessageDeliveryMessage : IArteryControlMessage
    {
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Acknowledges every <see cref="SystemMessageEnvelope"/> up to and including
    /// <paramref name="SeqNo"/> as delivered. Sent by the RECEIVING side's
    /// <see cref="SystemMessageAckerStage"/>, consumed by the SENDING side's
    /// <see cref="SystemMessageDeliveryStage"/>.
    ///
    /// <para>
    /// <b>Best-effort (design.md invariant 6).</b> Loss of an <see cref="Ack"/> is entirely covered
    /// by the sender's resend timer (a duplicate that arrives after a lost Ack is simply re-Acked
    /// and dropped) -- correctness never depends on any individual Ack/Nack actually arriving.
    /// </para>
    /// <para>
    /// <b>#6414 stale-ack guard (design.md invariant 3, MANDATORY).</b> <paramref name="From"/> is
    /// the ACKING system's OWN unique address at the moment it sent this reply. The consuming
    /// <see cref="SystemMessageDeliveryStage"/> MUST only act on this message when
    /// <paramref name="From"/>'s uid (equivalently, the envelope's own decoded origin uid --  the
    /// two always agree) matches the association's CURRENT
    /// <see cref="AssociationState.UniqueRemoteAddress"/> -- see that stage's
    /// <c>ControlMessageReceived</c> guard. A late Ack/Nack from a PRIOR incarnation (this is the
    /// classic <c>AckedSendBuffer&lt;T&gt;.Acknowledge</c>'s <c>CumulativeAck &gt; MaxSeq</c> bug,
    /// #6414, re-guarded here structurally rather than by a seq comparison) must NEVER be
    /// processed -- it must never mutate the buffer or trigger a quarantine.
    /// </para>
    /// </summary>
    /// <param name="SeqNo">The highest sequence number received (and every sequence number below it).</param>
    /// <param name="From">The acking system's own unique address (see the stale-ack guard remarks above).</param>
    internal sealed record Ack(long SeqNo, UniqueAddress From) : IArterySystemMessageDeliveryMessage;

    /// <summary>
    /// INTERNAL API.
    ///
    /// Signals a sequence-number GAP: <paramref name="SeqNo"/> is the highest CONTIGUOUS sequence
    /// number the receiver has actually delivered (everything after it, up to whatever the sender
    /// has already sent, is missing and must be resent). Sent by <see cref="SystemMessageAckerStage"/>
    /// when an inbound <see cref="SystemMessageEnvelope"/> arrives with a seq greater than expected;
    /// consumed by <see cref="SystemMessageDeliveryStage"/>, which pops its buffer's prefix up to
    /// <paramref name="SeqNo"/> and IMMEDIATELY resends the (now-shrunk) unacknowledged tail, without
    /// waiting for the next resend-timer tick.
    ///
    /// <para>Same best-effort / stale-ack-guard semantics as <see cref="Ack"/> -- see its remarks.</para>
    /// </summary>
    /// <param name="SeqNo">The highest contiguous sequence number actually delivered so far.</param>
    /// <param name="From">The nacking system's own unique address.</param>
    internal sealed record Nack(long SeqNo, UniqueAddress From) : IArterySystemMessageDeliveryMessage;

    /// <summary>
    /// INTERNAL API.
    ///
    /// Wraps one outbound <see cref="ISystemMessage"/> for reliable delivery: a per-incarnation
    /// monotonic sequence number (starting at 1), the sender's own unique address (so the receiver
    /// knows where to route its <see cref="Ack"/>/<see cref="Nack"/> reply -- mirrors
    /// <see cref="HandshakeReq"/>'s <c>From</c> field, which serves the identical purpose for the
    /// handshake reply), and the resolved recipient path (since this rides the CONTROL stream, not
    /// the ordinary stream, so the wire envelope's own recipient TAG is never populated for a
    /// control message -- see <see cref="IOutboundEnvelope.RecipientPath"/>'s remarks; the recipient
    /// has to live somewhere, so it is a field on this payload instead).
    ///
    /// <para>
    /// <b>How the inner message is nested (documented per the task).</b> <see cref="Message"/> is
    /// encoded via <see cref="Akka.Serialization.V2.MessagePackSerializer{TProtocol}.WriteEnvelopePayload"/>
    /// / decoded via <c>ReadEnvelopePayload</c> -- an EXISTING helper on the V2 MessagePack serializer
    /// base class that already implements exactly "nest a serializer id + manifest + raw bytes,
    /// recursively through <c>Serialization.FindSerializerFor</c>/<c>Deserialize</c>" for wrapping an
    /// arbitrary payload inside a hand-rolled msgpack message. <see cref="ISystemMessage"/> types
    /// (<see cref="Watch"/>/<see cref="Unwatch"/>/<see cref="DeathWatchNotification"/>/
    /// <see cref="Terminate"/>) have classic (non-V2) serializers
    /// (<see cref="Akka.Remote.Serialization.SystemMessageSerializer"/>) -- <c>WriteEnvelopePayload</c>
    /// already handles the classic-serializer case (falls back to <c>serializer.ToBinary(payload)</c>
    /// when the resolved serializer is not a <see cref="Akka.Serialization.SerializerV2"/>), so no new
    /// nesting mechanism was written for this -- it is reused as-is. This was the "cleanest" option
    /// investigated: a bespoke inner encoding would have duplicated this helper for no benefit.
    /// </para>
    /// <para>
    /// <b>Simplification vs. a fully general envelope (documented):</b> classic
    /// <c>RemoteActorRef.SendSystemMessage</c> always calls <c>Remote.Send(message, sender: null, this)</c>
    /// -- a system message never carries a sender ref in practice. This type therefore does NOT carry
    /// a <c>SenderPath</c> field (unlike <see cref="IOutboundEnvelope"/>'s general shape); the inbound
    /// dispatch path always resolves the sender as dead letters for delivered system messages,
    /// mirroring today's ordinary-message fallback for an absent sender.
    /// </para>
    /// </summary>
    /// <param name="Message">The system message being reliably delivered.</param>
    /// <param name="SeqNo">This envelope's monotonic sequence number (1-based per incarnation).</param>
    /// <param name="AckReplyTo">The sender's own unique address -- where the receiver routes its Ack/Nack.</param>
    /// <param name="RecipientPath">The resolved wire-format path of the recipient actor.</param>
    internal sealed record SystemMessageEnvelope(ISystemMessage Message, long SeqNo, UniqueAddress AckReplyTo, string RecipientPath) : IArterySystemMessageDeliveryMessage;

    /// <summary>
    /// INTERNAL API.
    ///
    /// Resets one association's OUTBOUND <see cref="SystemMessageDeliveryStage"/> state (seqNo back
    /// to 1, unacknowledged buffer emptied) for <paramref name="Incarnation"/>. Idempotent: applying
    /// it twice for the same (or an already-superseded) incarnation is a no-op the second time.
    ///
    /// <para>
    /// <b>Local-only, documented simplification vs. Pekko (this is the "where you simplify, say so"
    /// callout).</b> This message is enqueued directly onto an association's own outbound CONTROL
    /// channel (the SAME mechanism <see cref="ArteryQuarantined"/> uses) purely as a way to get an
    /// async, in-order instruction into <see cref="SystemMessageDeliveryStage"/>'s single-threaded
    /// <c>GraphStageLogic</c> from <see cref="ArteryRemoting.Quarantine"/> (a different execution
    /// context) -- reusing the existing queue plumbing rather than inventing a new callback channel.
    /// <see cref="SystemMessageDeliveryStage"/> intercepts and CONSUMES this message (never forwards
    /// it to <c>Out</c>/the encoder); it never actually reaches the wire in this implementation. Pekko
    /// may send the equivalent across the wire to evict the peer's inbound acker state early; this
    /// port does not need that, because <see cref="SystemMessageAckerStage"/>'s inbound expected-seq
    /// state is scoped to the ACCEPTED CONNECTION it is materialized on (design.md: "1 control
    /// connection" per association) -- a genuinely new incarnation requires a new inbound connection,
    /// which materializes a brand-new stage instance with empty state for free. This satisfies
    /// design.md's "do at least incarnation-based cleanup" open decision without a wire round-trip.
    /// It still gets a real entry in <see cref="ArteryControlMessageSerializer"/> (per the task) so
    /// the serializer is complete/robust even though, in normal operation, this type is never
    /// actually serialized onto a socket.
    /// </para>
    /// <para>
    /// Also applied automatically (same idempotent reset) whenever
    /// <see cref="SystemMessageDeliveryStage"/> observes <see cref="AssociationState.Incarnation"/>
    /// advance on its own (a remote restart detected via a genuinely new handshake uid) -- this
    /// explicit message exists for the OTHER case: quarantining the CURRENT (unchanged) uid, which
    /// does NOT bump <see cref="AssociationState.Incarnation"/> by itself.
    /// </para>
    /// </summary>
    /// <param name="Incarnation">The association incarnation this reset applies to.</param>
    internal sealed record ClearSystemMessageDelivery(int Incarnation) : IArterySystemMessageDeliveryMessage;
}
