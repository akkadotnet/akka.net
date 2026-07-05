//-----------------------------------------------------------------------
// <copyright file="ArteryOutboundElement.cs" company="Akka.NET Project">
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
    /// One element queued onto an <see cref="Association"/>'s bounded outbound channel (Decision
    /// 7/9): the message to encode plus the already-resolved sender/recipient wire paths. Produced
    /// by <c>ArteryRemoting.Send</c> (ordinary user messages -- both paths set, sender optional) and
    /// by the control-send hooks (<see cref="IOutboundContext.SendControl"/> /
    /// <see cref="IInboundContext.SendControl"/>) for <see cref="HandshakeReq"/>/<see cref="HandshakeRsp"/>
    /// (both paths <see langword="null"/> -- control messages carry their own addressing inside the
    /// payload and are recognized on decode by serializer id/manifest, not by an envelope path tag;
    /// see design.md "Handshake + association/UID (gate G2)").
    ///
    /// <para>
    /// This element flows through <see cref="OutboundHandshakeStage"/> as an opaque <c>object</c>
    /// (see the element-type note on <see cref="IInboundContext"/>) -- the stage holds/emits
    /// whatever it is handed without inspecting it, so it is safe to carry the wire-path metadata
    /// alongside the message all the way to the encode step. A <see cref="HandshakeReq"/> the stage
    /// injects itself arrives at the encode step un-wrapped (a bare <see cref="IArteryControlMessage"/>),
    /// which the encode step treats identically to a wrapped element with both paths
    /// <see langword="null"/>.
    /// </para>
    /// </summary>
    /// <param name="Message">The message to serialize as the envelope's payload.</param>
    /// <param name="SenderPath">
    /// The sender's wire-format path (address + path, per <c>ActorPath.ToSerializationFormatWithAddress</c>),
    /// or <see langword="null"/> for no sender (encodes as the ABSENT tag).
    /// </param>
    /// <param name="RecipientPath">
    /// The recipient's wire-format path, or <see langword="null"/> for control messages (which are
    /// recognized by serializer id/manifest on decode, not by recipient).
    /// </param>
    internal sealed record ArteryOutboundElement(object Message, string? SenderPath, string? RecipientPath);
}
