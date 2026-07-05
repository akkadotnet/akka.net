//-----------------------------------------------------------------------
// <copyright file="InboundEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

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
    internal sealed record InboundEnvelope(object Message, string? SenderPath, string? RecipientPath, long OriginUid, int SerializerId, string Manifest) : IInboundEnvelope
    {
        /// <inheritdoc/>
        public bool IsControl { get; } = Message is IArteryControlMessage;
    }
}
