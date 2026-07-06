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

        /// <summary>
        /// Registers <paramref name="subscriber"/> to receive every decoded inbound control message
        /// across every association (design.md gate G3) -- the seam <see cref="SystemMessageDeliveryStage"/>
        /// uses to observe <see cref="Ack"/>/<see cref="Nack"/> replies. Mirrors
        /// <c>ArteryRemoting.SubscribeControl</c>.
        /// </summary>
        void SubscribeControl(IControlMessageSubscriber subscriber);

        /// <summary>
        /// Reverses <see cref="SubscribeControl"/>.
        /// </summary>
        void UnsubscribeControl(IControlMessageSubscriber subscriber);

        /// <summary>
        /// Quarantines this association's CURRENT remote uid (design.md gate G3's "give-up (overflow
        /// OR timeout) -> quarantine, never a silent drop" invariant). A no-op if the association has
        /// no known peer uid yet (still <c>Associating</c> -- nothing to quarantine).
        /// </summary>
        void Quarantine();
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
        private readonly Action<IControlMessageSubscriber> _subscribeControl;
        private readonly Action<IControlMessageSubscriber> _unsubscribeControl;
        private readonly Action<Address, long> _quarantine;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssociationRegistryOutboundContext"/> class.
        /// </summary>
        /// <param name="registry">The shared association registry.</param>
        /// <param name="localAddress">This system's own unique address.</param>
        /// <param name="remoteAddress">The remote address this outbound association targets.</param>
        /// <param name="sendControl">Sends a message over the control channel to <paramref name="remoteAddress"/>.</param>
        /// <param name="subscribeControl">
        /// Registers a global inbound-control-message subscriber (design.md gate G3's
        /// <see cref="SystemMessageDeliveryStage"/> seam). Defaults to a no-op so pre-G3 callers
        /// (existing tests) do not need to supply it.
        /// </param>
        /// <param name="unsubscribeControl">Reverses <paramref name="subscribeControl"/>. Defaults to a no-op.</param>
        /// <param name="quarantine">
        /// Quarantines <paramref name="remoteAddress"/> for a given uid (design.md gate G3's
        /// give-up-&gt;quarantine invariant). Defaults to a no-op.
        /// </param>
        public AssociationRegistryOutboundContext(
            AssociationRegistry registry,
            UniqueAddress localAddress,
            Address remoteAddress,
            Action<object> sendControl,
            Action<IControlMessageSubscriber>? subscribeControl = null,
            Action<IControlMessageSubscriber>? unsubscribeControl = null,
            Action<Address, long>? quarantine = null)
        {
            _registry = registry;
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            _sendControl = sendControl;
            _subscribeControl = subscribeControl ?? (static _ => { });
            _unsubscribeControl = unsubscribeControl ?? (static _ => { });
            _quarantine = quarantine ?? (static (_, _) => { });
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

        /// <inheritdoc/>
        public void SubscribeControl(IControlMessageSubscriber subscriber) => _subscribeControl(subscriber);

        /// <inheritdoc/>
        public void UnsubscribeControl(IControlMessageSubscriber subscriber) => _unsubscribeControl(subscriber);

        /// <inheritdoc/>
        public void Quarantine()
        {
            if (AssociationState.UniqueRemoteAddress is { } peer)
                _quarantine(RemoteAddress, peer.Uid);
        }
    }
}
