using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace Akka.Remote.Artery.Interfaces
{
    /// <summary>
    /// INTERNAL API
    /// Outbound association API that is used by the stream operators.
    /// Separate trait to facilitate testing without real transport.
    /// </summary>
    internal interface IOutboundContext
    {
        /// <summary>
        /// The local inbound address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

        /// <summary>
        /// The outbound address for this association.
        /// </summary>
        Address RemoteAddress { get; }

        AssociationState AssociationState { get; }

        void Quarantine(string reason);

        /// <summary>
        /// An inbound operator can send control message, e.g. a HandshakeReq, to the remote
        /// address of this association. It will be sent over the control sub-channel.
        /// </summary>
        /// <param name="message"></param>
        void SendControl(IControlMessage message);

        /// <summary>
        /// 
        /// </summary>
        /// <returns>`true` if any of the streams are active (not stopped due to idle)</returns>
        bool IsOrdinaryMessageStreamActive();

        InboundControlJunction.IControlMessageSubject ControlSubject { get; }

        ArterySettings Settings { get; }
    }
}
