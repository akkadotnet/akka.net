//-----------------------------------------------------------------------
// <copyright file="IInboundContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The seam <see cref="InboundHandshakeStage"/> needs from the surrounding transport: who
    /// "we" are, a way to complete the handshake for whichever peer just sent a
    /// <see cref="HandshakeReq"/> or <see cref="HandshakeRsp"/>, and a reply hook to send a
    /// control message back to an arbitrary remote address. Sized down to exactly what G2 needs;
    /// mirrors the role Pekko's <c>InboundContext</c> plays for the handshake stage.
    ///
    /// <para>
    /// <b>Element-type note (G3).</b> <see cref="SendControl"/> takes the raw control message
    /// (e.g. a <see cref="HandshakeRsp"/>), not a stream element -- the caller
    /// (<c>ArteryRemoting.SendControlToAddress</c>) is what wraps it in an
    /// <see cref="OutboundEnvelope"/> before it re-enters the outbound pipeline. The handshake
    /// stages themselves are now typed <c>GraphStage&lt;FlowShape&lt;IInboundEnvelope, IInboundEnvelope&gt;&gt;</c>
    /// / <c>GraphStage&lt;FlowShape&lt;IOutboundEnvelope, IOutboundEnvelope&gt;&gt;</c> -- see
    /// <see cref="InboundHandshakeStage"/> / <see cref="OutboundHandshakeStage"/>.
    /// </para>
    /// </summary>
    internal interface IInboundContext
    {
        /// <summary>
        /// This system's own unique address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

        /// <summary>
        /// Completes the handshake for the association keyed by <c>peer.Address</c>, registering
        /// <paramref name="peer"/>'s uid. Called by <see cref="InboundHandshakeStage"/> for both
        /// directions: when a <see cref="HandshakeReq"/> arrives (registering the requester), and
        /// when a <see cref="HandshakeRsp"/> arrives (registering the responder, completing the
        /// return direction the local <see cref="OutboundHandshakeStage"/> is waiting on).
        /// </summary>
        AssociationState CompleteHandshake(UniqueAddress peer);

        /// <summary>
        /// Sends <paramref name="message"/> over the control channel to <paramref name="to"/>.
        /// Used by <see cref="InboundHandshakeStage"/> to reply with a <see cref="HandshakeRsp"/>.
        /// </summary>
        void SendControl(Address to, object message);

        /// <summary>
        /// Whether <paramref name="originUid"/> is a known (handshake-completed) association,
        /// per the SHARED <see cref="AssociationRegistry"/> reverse index — NOT a per-connection
        /// flag. This is what lets <see cref="InboundHandshakeStage"/> gate ordinary-stream
        /// envelopes on a connection that never itself carried a <see cref="HandshakeReq"/>/
        /// <see cref="HandshakeRsp"/> (task group 6, "Control Stream": handshake messages travel
        /// over a SEPARATE control connection once 6.3 lands, so the ordinary connection's own
        /// <see cref="InboundHandshakeStage"/> instance would otherwise never observe one).
        /// </summary>
        bool IsKnownOrigin(long originUid);
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Default <see cref="IInboundContext"/> backed by an <see cref="AssociationRegistry"/>.
    /// Sharing the SAME <see cref="AssociationRegistry"/> instance between a system's
    /// <see cref="AssociationRegistryOutboundContext"/>(s) and its single
    /// <see cref="AssociationRegistryInboundContext"/> is what lets
    /// <see cref="OutboundHandshakeStage"/> observe, by polling
    /// <see cref="IOutboundContext.AssociationState"/>, a completion that
    /// <see cref="InboundHandshakeStage"/> recorded via <see cref="CompleteHandshake"/> — see the
    /// notification-mechanism note on <see cref="OutboundHandshakeStage"/>.
    /// </summary>
    internal sealed class AssociationRegistryInboundContext : IInboundContext
    {
        private readonly AssociationRegistry _registry;
        private readonly Action<Address, object> _sendControl;

        public AssociationRegistryInboundContext(
            AssociationRegistry registry,
            UniqueAddress localAddress,
            Action<Address, object> sendControl)
        {
            _registry = registry;
            LocalAddress = localAddress;
            _sendControl = sendControl;
        }

        /// <inheritdoc/>
        public UniqueAddress LocalAddress { get; }

        /// <inheritdoc/>
        public AssociationState CompleteHandshake(UniqueAddress peer) => _registry.CompleteHandshake(peer.Address, peer);

        /// <inheritdoc/>
        public void SendControl(Address to, object message) => _sendControl(to, message);

        /// <inheritdoc/>
        public bool IsKnownOrigin(long originUid) => _registry.TryGetByUid(originUid) is not null;
    }
}
