//-----------------------------------------------------------------------
// <copyright file="InboundEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The uniform inbound stream element (design.md's pipelines are typed
    /// <c>Flow[InboundEnvelope]</c> in Pekko), produced by <see cref="ArteryInboundProcessingStage"/>
    /// and carried through <see cref="InboundHandshakeStage"/> to the final dispatch step
    /// (<c>ArteryRemoting.DispatchInbound</c>), which resolves <see cref="RecipientPath"/> /
    /// <see cref="SenderPath"/> to actual <see cref="Akka.Actor.IActorRef"/>s and <c>Tell</c>s.
    ///
    /// <para>
    /// Unlike the pre-G3 shape, CONTROL messages (<see cref="HandshakeReq"/> / <see cref="HandshakeRsp"/>)
    /// are now wrapped in this type too, instead of flowing naked -- <see cref="InboundHandshakeStage"/>
    /// dispatches on <see cref="IsControl"/> and pattern-matches <see cref="Message"/> inside the
    /// envelope, rather than type-testing the raw stream element.
    /// </para>
    ///
    /// <para>
    /// This is also where the coming G5 lane metadata (recipient hash) and G6 pooling (design.md's
    /// amortization model) attach: this type is the seam. Kept minimal for G3 -- exactly what
    /// today's stages plus the reliable system-message delivery stages (next chunk) need.
    /// </para>
    /// </summary>
    internal interface IInboundEnvelope
    {
        /// <summary>The deserialized payload -- an <see cref="IArteryControlMessage"/> when <see cref="IsControl"/>.</summary>
        object Message { get; }

        /// <summary>
        /// The sender's wire-format path, or <see langword="null"/> when the envelope carried no
        /// sender (ABSENT tag, or a control message, which never has one) -- the dispatch step
        /// falls back to dead letters as the resolved sender in that case.
        /// </summary>
        string? SenderPath { get; }

        /// <summary>
        /// The recipient's wire-format path. Always present for an ordinary-stream message; always
        /// <see langword="null"/> for a control envelope (control messages are recognized by
        /// serializer id/manifest, not by recipient -- see <see cref="IOutboundEnvelope.RecipientPath"/>).
        /// A decoded ordinary frame with no resolvable recipient (e.g. a COMPRESSED tag, unsupported
        /// until ref compression lands) is dropped by <see cref="ArteryInboundProcessingStage"/>
        /// before this type is ever constructed.
        /// </summary>
        string? RecipientPath { get; }

        /// <summary>
        /// The sender when it arrived as a numeric record (owner resolved: an omitted owner is
        /// <see cref="OriginUid"/>); <see cref="SenderPath"/> is then <see langword="null"/>.
        /// </summary>
        ArteryNumericRef? SenderRef { get; }

        /// <summary>
        /// The recipient when it arrived as a numeric record; <see cref="RecipientPath"/> is then
        /// <see langword="null"/> even for an ordinary-stream message.
        /// </summary>
        ArteryNumericRef? RecipientRef { get; }

        /// <summary>The sending system's UID, decoded from the envelope's fixed header.</summary>
        long OriginUid { get; }

        /// <summary>The serializer id used to deserialize <see cref="Message"/>, decoded from the envelope's fixed header.</summary>
        int SerializerId { get; }

        /// <summary>
        /// The manifest string decoded from the envelope's header (resolved BEFORE payload
        /// deserialization -- design.md's "Decode order (structural, not an optimization)"), used
        /// to deserialize <see cref="Message"/>. Retained on the envelope (not just consumed and
        /// discarded once <see cref="Message"/> is produced) because the G5 lane model
        /// deserializes AFTER partitioning to a lane -- the envelope has to carry the manifest
        /// (and, eventually, the raw payload bytes) across that lane boundary. Also useful for
        /// dead-letter/debug fidelity in the meantime (a decode/dispatch failure can still report
        /// what manifest was on the wire).
        /// </summary>
        string Manifest { get; }

        /// <summary>
        /// Cheap, precomputed (at construction, not re-tested per stage) flag: <see langword="true"/>
        /// when <see cref="Message"/> is an <see cref="IArteryControlMessage"/>. Stages dispatch on
        /// this instead of re-running an <c>is</c> type-test on every element at every stage.
        /// </summary>
        bool IsControl { get; }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Default (and, at G3, only) <see cref="IInboundEnvelope"/> implementation. Promotes/renames
    /// the former <c>ArteryInboundEnvelope</c> record -- see the type-level remarks on
    /// <see cref="IInboundEnvelope"/> for why control messages are now wrapped here too instead of
    /// flowing naked.
    /// </summary>
    /// <param name="Message">The deserialized payload.</param>
    /// <param name="SenderPath">The sender's wire-format path, or <see langword="null"/> when absent.</param>
    /// <param name="RecipientPath">The recipient's wire-format path, or <see langword="null"/> for a control envelope.</param>
    /// <param name="OriginUid">The sending system's UID.</param>
    /// <param name="SerializerId">The serializer id used to deserialize <see cref="Message"/>.</param>
    /// <param name="Manifest">The manifest string decoded from the envelope's header -- see <see cref="IInboundEnvelope.Manifest"/>.</param>
    /// <param name="Numeric">The numeric sender and/or recipient (an omitted owner resolved to <see cref="IInboundEnvelope.OriginUid"/>), or <see langword="null"/>.</param>
    internal sealed record InboundEnvelope(
        object Message,
        string? SenderPath,
        string? RecipientPath,
        long OriginUid,
        int SerializerId,
        string Manifest,
        NumericWireRefs? Numeric = null) : IInboundEnvelope
    {
        /// <inheritdoc/>
        public ArteryNumericRef? SenderRef => Numeric?.Sender;

        /// <inheritdoc/>
        public ArteryNumericRef? RecipientRef => Numeric?.Recipient;

        /// <inheritdoc/>
        public bool IsControl { get; } = Message is IArteryControlMessage;
    }

    /// <summary>INTERNAL API. Delivers one deserialized ordinary-stream message to its recipient; see <see cref="InboundWireRefs"/>.</summary>
    internal delegate void InboundOrdinaryDispatch(object message, in InboundWireRefs refs);

    /// <summary>
    /// INTERNAL API.
    ///
    /// The decoded sender and recipient of one inbound ordinary frame, carried from the decoder (or across
    /// an inbound lane) to <c>ArteryRemoting.DispatchOrdinaryMessage</c>, which takes it by <c>in</c>. Each
    /// side is either a literal wire path or a numeric ref; a recipient is always one of the two for an
    /// ordinary message.
    ///
    /// <para>
    /// A literal sender may be left as the frame's undecoded UTF-8 bytes (<see cref="ResolveSenderPath"/> builds
    /// the string only if the dispatcher asks), so a recipient that never reads its sender costs nothing.
    /// The bytes alias the received frame and are retained exactly like the payload slice that travels with
    /// them, so they stay valid for as long as the payload does.
    /// </para>
    /// </summary>
    internal readonly struct InboundWireRefs
    {
        private const byte SenderNumericFlag = 1;
        private const byte RecipientNumericFlag = 2;

        private readonly object? _sender; // string (decoded) | byte[] (undecoded UTF-8 at _senderOffset/_senderCount) | null
        private readonly int _senderOffset;
        private readonly int _senderCount;
        private readonly long _senderId;
        private readonly long _recipientOwnerUid;
        private readonly long _recipientId;
        private readonly byte _flags;

        /// <summary>Initializes a new instance of the <see cref="InboundWireRefs"/> struct.</summary>
        public InboundWireRefs(
            string? senderPath, ArteryNumericRef? senderRef,
            string? recipientPath, ArteryNumericRef? recipientRef,
            long originUid)
            : this(senderPath, default, senderRef, recipientPath, recipientRef, originUid)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InboundWireRefs"/> struct with a literal sender
        /// that is still undecoded UTF-8 (<paramref name="senderUtf8"/>, ignored when empty).
        /// </summary>
        public InboundWireRefs(
            string? senderPath, ReadOnlyMemory<byte> senderUtf8, ArteryNumericRef? senderRef,
            string? recipientPath, ArteryNumericRef? recipientRef,
            long originUid)
        {
            RecipientPath = recipientPath;
            OriginUid = originUid;
            _senderOffset = 0;
            _senderCount = 0;
            _sender = senderPath;
            if (senderPath is null && !senderUtf8.IsEmpty)
            {
                if (MemoryMarshal.TryGetArray(senderUtf8, out var segment))
                {
                    _sender = segment.Array;
                    _senderOffset = segment.Offset;
                    _senderCount = segment.Count;
                }
                else
                {
                    _sender = senderUtf8.ToArray();
                    _senderCount = senderUtf8.Length;
                }
            }

            _flags = 0;
            _senderId = senderRef?.Id ?? 0;
            _recipientOwnerUid = recipientRef?.OwnerUid ?? 0;
            _recipientId = recipientRef?.Id ?? 0;
            if (senderRef is not null)
                _flags |= SenderNumericFlag;
            if (recipientRef is not null)
                _flags |= RecipientNumericFlag;
        }

        /// <summary>The recipient's literal wire path, or <see langword="null"/> for a numeric recipient.</summary>
        public string? RecipientPath { get; }

        /// <summary>The sending system's UID (the frame's origin).</summary>
        public long OriginUid { get; }

        /// <summary>The sender, when it arrived as a numeric record. Its owner is always <see cref="OriginUid"/> (see <c>ArteryRemoting.TryResolveInboundSender</c>).</summary>
        public ArteryNumericRef? SenderRef =>
            (_flags & SenderNumericFlag) != 0 ? new ArteryNumericRef(OriginUid, _senderId) : null;

        /// <summary>The recipient, when it arrived as a numeric record.</summary>
        public ArteryNumericRef? RecipientRef =>
            (_flags & RecipientNumericFlag) != 0 ? new ArteryNumericRef(_recipientOwnerUid, _recipientId) : null;

        /// <summary>The sender's literal wire path, decoding it now if it was left undecoded; <see langword="null"/> for no sender or a numeric sender.</summary>
        public string? ResolveSenderPath()
        {
            if (_sender is string path)
                return path;

            return _sender is byte[] bytes ? Encoding.UTF8.GetString(bytes, _senderOffset, _senderCount) : null;
        }
    }
}
