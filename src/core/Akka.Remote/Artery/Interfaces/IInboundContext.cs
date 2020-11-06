using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Akka.Remote.Artery.Interfaces
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Inbound API that is used by the stream operators.
    /// Separate trait to facilitate testing without real transport.
    /// </summary>
    internal interface IInboundContext
    {
        /// <summary>
        /// The local inbound address.
        /// </summary>
        UniqueAddress LocalAddress { get; }

        /// <summary>
        /// An inbound operator can send control message, e.g. a reply, to the origin
        /// address with this method. It will be sent over the control sub-channel.
        /// </summary>
        /// <param name="to"></param>
        /// <param name="message"></param>
        void SendControl(Address to, IControlMessage message);

        /// <summary>
        /// Lookup the outbound association for a given address.
        /// </summary>
        /// <param name="remoteAddress"></param>
        /// <returns></returns>
        IOutboundContext Association(Address remoteAddress);

        /// <summary>
        /// Lookup the outbound association for a given UID.
        /// Will return `null` if the UID is unknown, i.e.
        /// handshake not completed.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        IOutboundContext Association(long uid);

        Task<Done> CompleteHandshake(UniqueAddress peer);

        ArterySettings Settings { get; }

        void PublishDropped(IInboundEnvelope inbound, string reason);
    }

}
