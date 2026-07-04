//-----------------------------------------------------------------------
// <copyright file="HandshakeMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using Akka.Actor;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Marker interface for the Artery control-stream handshake protocol messages
    /// (<see cref="HandshakeReq"/> / <see cref="HandshakeRsp"/>), used as the anchor protocol
    /// type for <see cref="ArteryControlMessageSerializer"/>.
    /// </summary>
    internal interface IArteryControlMessage
    {
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Handshake request. Sent by <see cref="OutboundHandshakeStage"/> to establish (or
    /// re-establish, after a remote restart) the association with <paramref name="To"/>.
    /// See <c>openspec/changes/artery-tcp-remoting/design.md</c>
    /// ("Handshake + association/UID (gate G2)").
    /// </summary>
    /// <param name="From">The sender's own unique address (address + UID).</param>
    /// <param name="To">
    /// The address the sender believes it is associating with. <see cref="InboundHandshakeStage"/>
    /// rejects (drops, does not fail the stream) a request whose <paramref name="To"/> does not
    /// match the local address.
    /// </param>
    internal sealed record HandshakeReq(UniqueAddress From, Address To) : IArteryControlMessage;

    /// <summary>
    /// INTERNAL API.
    ///
    /// Handshake response, sent by <see cref="InboundHandshakeStage"/> in reply to a
    /// <see cref="HandshakeReq"/>, completing the requester's knowledge of the responder's UID.
    /// </summary>
    /// <param name="From">The responder's own unique address (address + UID).</param>
    internal sealed record HandshakeRsp(UniqueAddress From) : IArteryControlMessage;
}
