//-----------------------------------------------------------------------
// <copyright file="IOutboundContext.cs" company="Akka.NET Project">
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
    /// The seam <see cref="OutboundHandshakeStage"/> needs from the surrounding association:
    /// who "we" are, who the peer is, a read view of the shared <see cref="Artery.AssociationState"/>
    /// (so the stage can detect handshake completion — see the "Handshake-completion notification
    /// mechanism" note on <see cref="OutboundHandshakeStage"/>), and a hook to send a control
    /// message. Sized down to exactly what G2 needs; mirrors the role Pekko's
    /// <c>OutboundContext</c> plays for the handshake stage.
    /// </summary>
    internal interface IOutboundContext
    {
        /// <summary>
        /// This system's own unique address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

        /// <summary>
        /// The remote address this outbound association targets.
        /// </summary>
        Address RemoteAddress { get; }

        /// <summary>
        /// A read view of the association's current state. Lock-free and safe to read from any
        /// thread — see <see cref="Artery.AssociationState"/>.
        /// </summary>
        AssociationState AssociationState { get; }

        /// <summary>
        /// Completes the handshake with <paramref name="peer"/> for this outbound association.
        /// Exposed for symmetry/testability with <see cref="IInboundContext"/>; the normal G2
        /// flow completes an association's handshake from the INBOUND side (a
        /// <see cref="HandshakeRsp"/> arriving on the inbound pipeline for the return direction),
        /// not from <see cref="OutboundHandshakeStage"/> itself.
        /// </summary>
        AssociationState CompleteHandshake(UniqueAddress peer);

        /// <summary>
        /// Sends <paramref name="message"/> over the control channel to the remote peer this
        /// context is bound to.
        /// </summary>
        void SendControl(object message);
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Default <see cref="IOutboundContext"/> backed by an <see cref="AssociationRegistry"/>.
    /// The control-send mechanics (real control-stream wiring lands at G3 — see design.md
    /// "G2 staging") are not this class's concern; it is handed a delegate so callers (tests, or
    /// a later chunk's control-stream sender) supply the actual transport.
    /// </summary>
    internal sealed class AssociationRegistryOutboundContext : IOutboundContext
    {
        private readonly AssociationRegistry _registry;
        private readonly Action<object> _sendControl;

        public AssociationRegistryOutboundContext(
            AssociationRegistry registry,
            UniqueAddress localAddress,
            Address remoteAddress,
            Action<object> sendControl)
        {
            _registry = registry;
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            _sendControl = sendControl;
        }

        /// <inheritdoc/>
        public UniqueAddress LocalAddress { get; }

        /// <inheritdoc/>
        public Address RemoteAddress { get; }

        /// <inheritdoc/>
        public AssociationState AssociationState => _registry.AssociationFor(RemoteAddress).CurrentState;

        /// <inheritdoc/>
        public AssociationState CompleteHandshake(UniqueAddress peer) => _registry.CompleteHandshake(RemoteAddress, peer);

        /// <inheritdoc/>
        public void SendControl(object message) => _sendControl(message);
    }
}
