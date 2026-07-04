//-----------------------------------------------------------------------
// <copyright file="ArteryInboundEnvelope.cs" company="Akka.NET Project">
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
    /// A decoded, deserialized ordinary-stream (user) message, produced by
    /// <see cref="ArteryInboundProcessingStage"/> and carried opaquely (as an <c>object</c> element
    /// -- see the element-type note on <see cref="IInboundContext"/>) through
    /// <see cref="InboundHandshakeStage"/> to the final dispatch step
    /// (<c>ArteryRemoting.DispatchInbound</c>), which resolves <see cref="RecipientPath"/> /
    /// <see cref="SenderPath"/> to actual <see cref="Akka.Actor.IActorRef"/>s and <c>Tell</c>s.
    ///
    /// <para>
    /// Control messages (<see cref="HandshakeReq"/> / <see cref="HandshakeRsp"/>) are never wrapped
    /// in this type -- they are recognized on decode (they deserialize to an
    /// <see cref="IArteryControlMessage"/>) and passed through as themselves, since
    /// <see cref="InboundHandshakeStage"/> pattern-matches on their concrete type and swallows them.
    /// </para>
    /// </summary>
    /// <param name="Message">The deserialized payload.</param>
    /// <param name="SenderPath">
    /// The sender's wire-format path, or <see langword="null"/> when the envelope carried no sender
    /// (ABSENT tag) -- the dispatch step falls back to dead letters as the resolved sender in that case.
    /// </param>
    /// <param name="RecipientPath">
    /// The recipient's wire-format path. Always present for an ordinary-stream message; a decoded
    /// frame with no resolvable recipient (e.g. a COMPRESSED tag, unsupported until ref compression
    /// lands) is dropped by <see cref="ArteryInboundProcessingStage"/> before this type is ever
    /// constructed.
    /// </param>
    internal sealed record ArteryInboundEnvelope(object Message, string? SenderPath, string RecipientPath);
}
