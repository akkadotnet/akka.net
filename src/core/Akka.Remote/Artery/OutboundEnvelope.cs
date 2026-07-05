//-----------------------------------------------------------------------
// <copyright file="OutboundEnvelope.cs" company="Akka.NET Project">
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
    /// The uniform outbound stream element (design.md's pipelines are typed
    /// <c>Flow[OutboundEnvelope]</c> in Pekko -- the envelope IS the element type; heterogeneity
    /// (control vs. ordinary user message) lives INSIDE it, not in per-element runtime type
    /// dispatch on a naked <c>object</c> stream). Produced by <c>ArteryRemoting.Send</c> (ordinary
    /// user messages -- both paths set, sender optional) and by the control-send hooks
    /// (<see cref="IOutboundContext.SendControl"/> / <see cref="IInboundContext.SendControl"/>) for
    /// <see cref="HandshakeReq"/>/<see cref="HandshakeRsp"/> (both paths <see langword="null"/> --
    /// control messages carry their own addressing inside the payload and are recognized on decode
    /// by serializer id/manifest, not by an envelope path tag; see design.md "Handshake +
    /// association/UID (gate G2)").
    ///
    /// <para>
    /// This is also where the coming G5 lane metadata (recipient hash, so an inbound/outbound lane
    /// partitioner never has to re-derive it) and G6 pooling (design.md's amortization model --
    /// "one message = one envelope = one stream element") attach: this type is the seam. Kept
    /// minimal for G3 -- exactly what today's stages plus <see cref="OutboundHandshakeStage"/> need.
    /// </para>
    /// </summary>
    internal interface IOutboundEnvelope
    {
        /// <summary>The message to serialize as the envelope's payload.</summary>
        object Message { get; }

        /// <summary>
        /// The sender's wire-format path (address + path, per <c>ActorPath.ToSerializationFormatWithAddress</c>),
        /// or <see langword="null"/> for no sender (encodes as the ABSENT tag).
        /// </summary>
        string? SenderPath { get; }

        /// <summary>
        /// The recipient's wire-format path, or <see langword="null"/> for control messages (which
        /// are recognized by serializer id/manifest on decode, not by recipient).
        /// </summary>
        string? RecipientPath { get; }

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
    /// Default (and, at G3, only) <see cref="IOutboundEnvelope"/> implementation. Replaces/absorbs
    /// the former <c>ArteryOutboundElement</c> record -- see the type-level remarks on
    /// <see cref="IOutboundEnvelope"/> for why the envelope is now the uniform stream element
    /// instead of a naked <c>object</c>.
    /// </summary>
    /// <param name="Message">The message to serialize as the envelope's payload.</param>
    /// <param name="SenderPath">The sender's wire-format path, or <see langword="null"/> for no sender.</param>
    /// <param name="RecipientPath">The recipient's wire-format path, or <see langword="null"/> for control messages.</param>
    internal sealed record OutboundEnvelope(object Message, string? SenderPath, string? RecipientPath) : IOutboundEnvelope
    {
        /// <inheritdoc/>
        public bool IsControl { get; } = Message is IArteryControlMessage;
    }
}
